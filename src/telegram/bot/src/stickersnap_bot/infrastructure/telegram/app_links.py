"""t.me links to the Main Mini App: the chat's buttons and the desktop's link code use them."""

from aiogram import Bot
from aiogram.exceptions import TelegramAPIError
from aiohttp import ClientError

from stickersnap_bot.application.errors import TelegramUnavailableError
from stickersnap_bot.infrastructure.telegram.errors import translate


class BotAppLinks:
    def __init__(self, bot: Bot) -> None:
        self._bot = bot

    async def startapp(self, param: str | None = None) -> str:
        # Only the Main Mini App opens from https://t.me/<bot>?startapp, so it must be on in
        # @BotFather. The bot's username comes from getMe, cached by aiogram after the first call.
        try:
            me = await self._bot.me()
        except TelegramAPIError as error:
            raise translate(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error
        return f"https://t.me/{me.username}?startapp" + (f"={param}" if param else "")
