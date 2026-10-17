"""The only composition root: the one module that knows every layer."""

import uvicorn
from aiogram import Bot
from aiogram.client.session.aiohttp import AiohttpSession
from aiogram.client.telegram import TelegramAPIServer
from fastapi import FastAPI

from stickersnap_bot.api.app import Polling, UpdateDelivery, Webhook, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.infrastructure.settings import Settings


def build_app(settings: Settings) -> FastAPI:
    session = (
        AiohttpSession(api=TelegramAPIServer.from_base(settings.telegram_api_url))
        if settings.telegram_api_url
        else AiohttpSession()
    )
    bot = Bot(settings.bot_token.get_secret_value(), session=session)
    delivery: UpdateDelivery = Polling()
    if settings.bot_mode == "webhook":
        # Settings already refused webhook mode without them
        assert settings.webhook_base_url is not None
        assert settings.webhook_secret is not None
        delivery = Webhook(settings.webhook_base_url, settings.webhook_secret.get_secret_value())
    return create_app(bot, create_dispatcher(), delivery)


def main() -> None:
    settings = Settings()
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)


if __name__ == "__main__":
    main()
