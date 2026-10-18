from typing import Protocol


class AppLinks(Protocol):
    """Links that open the bot's Mini App on a screen: what the desktop shows as a button and a QR.

    The bot's username lives in Telegram, so the adapter asks it (getMe) and keeps it.
    """

    async def startapp(self, param: str | None = None) -> str:
        """https://t.me/<bot>?startapp=<param>, the Mini App reads the param on start; without
        one the Mini App opens on «Мои паки»."""
        ...
