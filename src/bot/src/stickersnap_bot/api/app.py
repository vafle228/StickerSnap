import asyncio
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from dataclasses import dataclass

from aiogram import Bot, Dispatcher
from fastapi import FastAPI

from stickersnap_bot.api import health
from stickersnap_bot.api.telegram import WEBHOOK_PATH, create_webhook_router


@dataclass(frozen=True)
class Polling:
    """Development: the service reads updates itself with getUpdates."""


@dataclass(frozen=True)
class Webhook:
    """Production: Telegram posts updates to {base_url}/telegram/webhook."""

    base_url: str
    secret: str


type UpdateDelivery = Polling | Webhook


def create_app(bot: Bot, dispatcher: Dispatcher, delivery: UpdateDelivery) -> FastAPI:
    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        polling: asyncio.Task[None] | None = None
        if isinstance(delivery, Webhook):
            await bot.set_webhook(
                delivery.base_url.rstrip("/") + WEBHOOK_PATH,
                secret_token=delivery.secret,
                allowed_updates=dispatcher.resolve_used_update_types(),
            )
        else:
            # A webhook left over from production would make getUpdates answer 409
            await bot.delete_webhook()
            polling = asyncio.create_task(dispatcher.start_polling(bot, handle_signals=False))
        try:
            yield
        finally:
            if polling is not None:
                await dispatcher.stop_polling()
                await polling
            await bot.session.close()

    app = FastAPI(title="StickerSnap bot service", lifespan=lifespan)
    app.include_router(health.router)
    if isinstance(delivery, Webhook):
        app.include_router(create_webhook_router(bot, dispatcher, delivery.secret))
    return app
