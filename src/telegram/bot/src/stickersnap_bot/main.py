"""The only composition root: the one module that knows every layer."""

from datetime import UTC, datetime

import uvicorn
from aiogram import Bot
from aiogram.client.session.aiohttp import AiohttpSession
from aiogram.client.telegram import TelegramAPIServer
from fastapi import FastAPI

from stickersnap_bot.api.app import Polling, UpdateDelivery, Webhook, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.application.init_data import InitDataVerifier
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.miniapp import MiniApp
from stickersnap_bot.infrastructure.database.migrate import migrate
from stickersnap_bot.infrastructure.database.unit_of_work import SqlUnitOfWork, create_engine
from stickersnap_bot.infrastructure.settings import Settings
from stickersnap_bot.infrastructure.telegram.sticker_sets import BotStickerSets


def _now() -> datetime:
    return datetime.now(UTC)


def build_app(settings: Settings) -> FastAPI:
    session = (
        AiohttpSession(api=TelegramAPIServer.from_base(settings.telegram_api_url))
        if settings.telegram_api_url
        else AiohttpSession()
    )
    token = settings.bot_token.get_secret_value()
    bot = Bot(token, session=session)
    delivery: UpdateDelivery = Polling()
    if settings.bot_mode == "webhook":
        # Settings already refused webhook mode without them
        assert settings.webhook_base_url is not None
        assert settings.webhook_secret is not None
        delivery = Webhook(settings.webhook_base_url, settings.webhook_secret.get_secret_value())
    engine = create_engine(settings.database_path)
    miniapp = MiniApp(
        unit_of_work=lambda: SqlUnitOfWork(engine),
        sticker_sets=BotStickerSets(bot),
        verifier=InitDataVerifier(token, max_age=settings.init_data_max_age),
        media=MediaLinks(token),
        clock=_now,
    )
    return create_app(bot, create_dispatcher(), delivery, miniapp, closing=[engine.dispose])


def main() -> None:
    settings = Settings()
    # before the app: the schema is ready when the first update or request arrives
    migrate(settings.database_path)
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)


if __name__ == "__main__":
    main()
