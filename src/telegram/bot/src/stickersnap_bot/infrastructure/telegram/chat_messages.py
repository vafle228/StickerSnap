"""The chat port over the Bot API: HTML text and an inline keyboard, one button per row."""

from html import escape

from aiogram import Bot
from aiogram.enums import ParseMode
from aiogram.exceptions import TelegramAPIError, TelegramBadRequest, TelegramForbiddenError
from aiogram.types import InlineKeyboardButton, InlineKeyboardMarkup
from aiohttp import ClientError

from stickersnap_bot.application.chat_messages import Button, ChatMessage, OpenApp
from stickersnap_bot.application.errors import TelegramUnavailableError
from stickersnap_bot.infrastructure.telegram.errors import translate


class BotChatMessages:
    def __init__(self, bot: Bot) -> None:
        self._bot = bot

    async def send(self, chat_id: int, message: ChatMessage) -> int | None:
        try:
            sent = await self._bot.send_message(
                chat_id,
                _html(message),
                parse_mode=ParseMode.HTML,
                reply_markup=await self._keyboard(message),
            )
        except TelegramForbiddenError:
            # the bot was stopped, or the user never pressed «Начать»
            return None
        except TelegramAPIError as error:
            raise translate(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error
        return sent.message_id

    async def edit(self, chat_id: int, message_id: int, message: ChatMessage) -> bool:
        try:
            await self._bot.edit_message_text(
                text=_html(message),
                chat_id=chat_id,
                message_id=message_id,
                parse_mode=ParseMode.HTML,
                reply_markup=await self._keyboard(message),
            )
        except TelegramBadRequest as error:
            # the same text again is no failure; a deleted or a too old message is
            return "not modified" in error.message
        except TelegramForbiddenError:
            return False
        except TelegramAPIError as error:
            raise translate(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error
        return True

    async def _keyboard(self, message: ChatMessage) -> InlineKeyboardMarkup | None:
        if not message.buttons:
            return None
        return InlineKeyboardMarkup(
            inline_keyboard=[[await self._button(button)] for button in message.buttons]
        )

    async def _button(self, button: Button) -> InlineKeyboardButton:
        if not isinstance(button, OpenApp):
            return InlineKeyboardButton(text=button.text, url=button.url)
        # A web_app button carries no start_param: only a t.me link to the Main Mini App
        # opens it on a screen. The bot's username comes from getMe, cached by aiogram.
        me = await self._bot.me()
        param = button.start_param
        link = f"https://t.me/{me.username}?startapp" + (f"={param}" if param else "")
        return InlineKeyboardButton(text=button.text, url=link)


def _html(message: ChatMessage) -> str:
    text = escape(message.text, quote=False)
    if message.title is None:
        return text
    return f"<b>{escape(message.title, quote=False)}</b>\n{text}"
