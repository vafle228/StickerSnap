"""The feedback inbox port over the Bot API: a message to FEEDBACK_CHAT_ID per feedback, its
diagnostics as a file in reply (artboard 18, section 7)."""

from html import escape

from aiogram import Bot
from aiogram.exceptions import TelegramAPIError
from aiogram.types import BufferedInputFile, LinkPreviewOptions, ReplyParameters
from aiohttp import ClientError

from stickersnap_bot.application.errors import TelegramUnavailableError
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.feedback import Feedback, FeedbackSource, FeedbackType
from stickersnap_bot.domain.users import User
from stickersnap_bot.infrastructure.telegram.errors import translate

_TYPES = {FeedbackType.BUG: "Ошибка", FeedbackType.IDEA: "Идея", FeedbackType.OTHER: "Другое"}
_SOURCES = {FeedbackSource.DESKTOP: "десктоп", FeedbackSource.MINIAPP: "Mini App"}


class TelegramFeedbackInbox:
    def __init__(self, bot: Bot, chat_id: int) -> None:
        self._bot = bot
        self._chat_id = chat_id

    async def deliver(self, feedback: Feedback, user: User | None, device: Device | None) -> None:
        try:
            sent = await self._bot.send_message(
                self._chat_id,
                feedback_message(feedback, user, device),
                parse_mode="HTML",
                link_preview_options=LinkPreviewOptions(is_disabled=True),
            )
            if feedback.diagnostics is not None:
                await self._bot.send_document(
                    self._chat_id,
                    BufferedInputFile(
                        feedback.diagnostics.encode(), filename=f"diagnostics-{feedback.id}.txt"
                    ),
                    reply_parameters=ReplyParameters(message_id=sent.message_id),
                )
        except TelegramAPIError as error:
            raise translate(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error


def feedback_message(feedback: Feedback, user: User | None, device: Device | None) -> str:
    """The message in HTML: what and where from, who and whether to answer, the text, the code."""
    if feedback.author.source is FeedbackSource.DESKTOP:
        version = None if feedback.app_version is None else f"StickerSnap {feedback.app_version}"
    else:
        version = feedback.app_version
    head = [
        f"<b>{_TYPES[feedback.type]}</b>",
        _SOURCES[feedback.author.source],
        *(escape(part) for part in (version, feedback.platform) if part),
    ]
    lines = [" · ".join(head), "От: " + " · ".join(_sender(feedback, user, device))]
    if feedback.text:
        lines += ["", f"«{escape(feedback.text)}»"]
    tail = []
    if feedback.error is not None:
        tail.append(f"Код: {escape(feedback.error.code)}")
        if feedback.error.trace_id:
            tail.append(escape(feedback.error.trace_id))
    tail.append(f"#{feedback.id}")
    lines += ["", " · ".join(tail)]
    return "\n".join(lines)


def _sender(feedback: Feedback, user: User | None, device: Device | None) -> list[str]:
    if feedback.author.user_id is None:
        who = ["без привязки"]
        if feedback.can_reply and feedback.contact:
            return [*who, f"ответить на {escape(feedback.contact)}"]
        return [*who, "не отвечать"]
    profile = None if user is None else user.profile
    if profile is None:
        who = [f'<a href="tg://user?id={feedback.author.user_id}">{feedback.author.user_id}</a>']
    elif profile.username:
        who = [f"@{escape(profile.username)}"]
    else:
        name = " ".join(part for part in (profile.first_name, profile.last_name) if part)
        who = [f'<a href="tg://user?id={feedback.author.user_id}">{escape(name)}</a>']
    if device is not None:
        who.append(escape(device.name))
    return [*who, "можно писать" if feedback.can_reply else "не писать"]
