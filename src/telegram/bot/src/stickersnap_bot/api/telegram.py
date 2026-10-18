import secrets
from typing import Annotated, Any

from aiogram import Bot, Dispatcher, F, Router
from aiogram.enums import ChatMemberStatus, ChatType
from aiogram.filters import Command, CommandStart
from aiogram.types import BotCommand, ChatMemberUpdated, Message, Update
from aiogram.types import User as TelegramUser
from fastapi import APIRouter, Header, Response, status

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
    dispatcher.include_router(_chat_router(chat))
    return dispatcher


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
