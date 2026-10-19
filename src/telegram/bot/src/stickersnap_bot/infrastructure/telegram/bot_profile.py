"""The bot's profile in Telegram, set from code instead of by hand in @BotFather."""

from aiogram import Bot
from aiogram.types import MenuButtonWebApp, WebAppInfo

from stickersnap_bot.application.chat_texts import ABOUT, DESCRIPTION, MENU_BUTTON


class BotProfile:
    """Description, about and the menu button. Each is read first and written only when it
    differs: the setters are rate limited, and the service restarts on every deploy.
    The name, the avatar, the Main Mini App and the group mode have no Bot API method:
    they stay in @BotFather."""

    def __init__(self, bot: Bot, miniapp_url: str | None) -> None:
        self._bot = bot
        self._miniapp_url = miniapp_url

    async def apply(self) -> None:
        if (await self._bot.get_my_description()).description != DESCRIPTION:
            await self._bot.set_my_description(DESCRIPTION)
        if (await self._bot.get_my_short_description()).short_description != ABOUT:
            await self._bot.set_my_short_description(ABOUT)
        if self._miniapp_url is None:
            # the development bot keeps the commands menu: its Mini App runs on localhost
            return
        button = MenuButtonWebApp(text=MENU_BUTTON, web_app=WebAppInfo(url=self._miniapp_url))
        current = await self._bot.get_chat_menu_button()
        if not (
            isinstance(current, MenuButtonWebApp)
            and current.text == button.text
            and current.web_app.url == button.web_app.url
        ):
            await self._bot.set_chat_menu_button(menu_button=button)
