import secrets
from typing import Annotated, Any

from aiogram import Bot, Dispatcher, Router
from aiogram.types import Update
from fastapi import APIRouter, Header, Response, status

WEBHOOK_PATH = "/telegram/webhook"


def create_dispatcher() -> Dispatcher:
    """The bot's chat: commands and callbacks are added by S3 as routers of their own."""
    dispatcher = Dispatcher()
    dispatcher.include_router(Router(name="chat"))
    return dispatcher


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
