import secrets
from collections.abc import Awaitable, Callable
from typing import Annotated, Any

from aiogram import BaseMiddleware, Bot, Dispatcher, F, Router
from aiogram.dispatcher.event.bases import UNHANDLED
from aiogram.enums import ChatMemberStatus, ChatType
from aiogram.filters import Command, CommandStart
from aiogram.types import BotCommand, ChatMemberUpdated, Message, TelegramObject, Update
from aiogram.types import User as TelegramUser
from fastapi import APIRouter, Header, Response, status
from opentelemetry import metrics, trace
from opentelemetry.trace import SpanKind

from stickersnap_bot.application.chat import BotChat, Incoming
from stickersnap_bot.domain.users import TelegramProfile

WEBHOOK_PATH = "/telegram/webhook"
# the menu of commands beside the input field, set on every start (board 17, section 4)
COMMANDS = (
    BotCommand(command="start", description="Открыть StickerSnap"),
    BotCommand(command="help", description="Что умеет бот"),
    BotCommand(command="feedback", description="Написать отзыв"),
)
# stickers from these are made on the computer; anything else is answered as text
_MEDIA = F.video | F.animation | F.document | F.video_note


def create_dispatcher(chat: BotChat) -> Dispatcher:
    dispatcher = Dispatcher()
    dispatcher.update.outer_middleware(UpdateTelemetry())
    dispatcher.include_router(_chat_router(chat))
    return dispatcher


class UpdateTelemetry(BaseMiddleware):
    """A span and a count for every update, whether it came by webhook (inside the request's span)
    or by polling (a trace of its own). Without telemetry (infrastructure/telemetry.py) the tracer
    and the meter are OpenTelemetry's no-op ones."""

    def __init__(
        self, tracer: trace.Tracer | None = None, meter: metrics.Meter | None = None
    ) -> None:
        self._tracer = tracer or trace.get_tracer(__name__)
        self._updates = (meter or metrics.get_meter(__name__)).create_counter(
            "stickersnap.bot.updates",
            unit="{update}",
            description="Telegram updates by type and outcome: handled, unhandled or error",
        )

    async def __call__(
        self,
        handler: Callable[[TelegramObject, dict[str, Any]], Awaitable[Any]],
        event: TelegramObject,
        data: dict[str, Any],
    ) -> Any:
        if not isinstance(event, Update):
            return await handler(event, data)
        kind = event.event_type
        with self._tracer.start_as_current_span(
            f"telegram {kind}",
            kind=SpanKind.CONSUMER,
            attributes={"telegram.update.type": kind, "telegram.update.id": event.update_id},
        ) as span:
            try:
                result = await handler(event, data)
            except Exception:
                # the span records the exception itself
                self._updates.add(1, {"telegram.update.type": kind, "outcome": "error"})
                raise
            outcome = "unhandled" if result is UNHANDLED else "handled"
            span.set_attribute("telegram.update.outcome", outcome)
            self._updates.add(1, {"telegram.update.type": kind, "outcome": outcome})
            return result


def _chat_router(chat: BotChat) -> Router:
    router = Router(name="chat")
    # groups are off in @BotFather; a chat with the bot is the user's private one
    router.message.filter(F.chat.type == ChatType.PRIVATE, F.from_user)
    router.my_chat_member.filter(F.chat.type == ChatType.PRIVATE)

    @router.message(CommandStart())
    async def start(message: Message) -> None:
        user = _sender(message)
        await chat.start(user.id, _profile(user))

    @router.message(Command("help"))
    async def help_command(message: Message) -> None:
        await chat.help(_sender(message).id)

    @router.message(Command("feedback"))
    async def feedback(message: Message) -> None:
        await chat.feedback(_sender(message).id)

    @router.message(_MEDIA)
    async def media(message: Message) -> None:
        await chat.answer(_sender(message).id, Incoming.MEDIA)

    @router.message()
    async def anything_else(message: Message) -> None:
        await chat.answer(_sender(message).id, Incoming.TEXT)

    @router.my_chat_member()
    async def member_changed(event: ChatMemberUpdated) -> None:
        stopped = event.new_chat_member.status == ChatMemberStatus.KICKED
        await chat.member_changed(event.from_user.id, _profile(event.from_user), stopped)

    return router


def _sender(message: Message) -> TelegramUser:
    # the router's filter lets through only messages with a sender
    assert message.from_user is not None
    return message.from_user


def _profile(user: TelegramUser) -> TelegramProfile:
    return TelegramProfile(
        first_name=user.first_name,
        last_name=user.last_name,
        username=user.username,
        language_code=user.language_code,
    )


def create_webhook_router(bot: Bot, dispatcher: Dispatcher, secret: str) -> APIRouter:
    router = APIRouter(tags=["telegram"])

    @router.post(WEBHOOK_PATH, include_in_schema=False)
    async def post_update(
        update: dict[str, Any],
        x_telegram_bot_api_secret_token: Annotated[str | None, Header()] = None,
    ) -> Response:
        # Only Telegram knows the secret it was given in setWebhook
        if not secrets.compare_digest(x_telegram_bot_api_secret_token or "", secret):
            return Response(status_code=status.HTTP_401_UNAUTHORIZED)
        await dispatcher.feed_update(bot, Update.model_validate(update, context={"bot": bot}))
        return Response(status_code=status.HTTP_200_OK)

    return router
