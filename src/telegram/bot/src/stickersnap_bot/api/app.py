import asyncio
from collections.abc import AsyncIterator, Awaitable, Callable, Sequence
from contextlib import asynccontextmanager
from dataclasses import dataclass

from aiogram import Bot, Dispatcher
from fastapi import FastAPI

from stickersnap_bot.api import health
from stickersnap_bot.api import miniapp as miniapp_api
from stickersnap_bot.api.problems import add_problem_handlers
from stickersnap_bot.api.telegram import WEBHOOK_PATH, create_webhook_router
from stickersnap_bot.application.miniapp import MiniApp
from stickersnap_bot.application.sign_in import SignIn


@dataclass(frozen=True)
class Polling:
    """Development: the service reads updates itself with getUpdates."""


@dataclass(frozen=True)
class Webhook:
    """Production: Telegram posts updates to {base_url}/telegram/webhook."""

    base_url: str
    secret: str


type UpdateDelivery = Polling | Webhook


def create_app(
    bot: Bot,
    dispatcher: Dispatcher,
    delivery: UpdateDelivery,
    sign_in: SignIn,
    miniapp: MiniApp,
    closing: Sequence[Callable[[], Awaitable[None]]] = (),
) -> FastAPI:
    """`closing` runs on shutdown after the bot stops: the database engine and the like."""

    async def close() -> None:
        await bot.session.close()
        for resource in closing:
            await resource()

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
            await close()

    app = FastAPI(title="StickerSnap bot service", lifespan=lifespan)
    add_problem_handlers(app)
    app.include_router(health.router)
    app.state.sign_in = sign_in
    app.state.miniapp = miniapp
    # what the lifespan closes on shutdown, for a host that never ran it
    app.state.close = close
    app.include_router(miniapp_api.router)
    if isinstance(delivery, Webhook):
        app.include_router(create_webhook_router(bot, dispatcher, delivery.secret))
    return app
