from collections.abc import AsyncIterator

import pytest
from aiogram import Bot
from httpx import ASGITransport, AsyncClient

from stickersnap_bot.api.app import Webhook, create_app
from stickersnap_bot.api.telegram import WEBHOOK_PATH, create_dispatcher
from tests.fakes import Tables, miniapp, sign_in

SECRET = "webhook-secret"
TEXT_MESSAGE = {
    "update_id": 1,
    "message": {"message_id": 1, "date": 0, "chat": {"id": 1, "type": "private"}, "text": "hi"},
}


@pytest.fixture
async def client() -> AsyncIterator[AsyncClient]:
    # The lifespan (setWebhook, polling) is not run, so nothing reaches Telegram
    bot = Bot("123:token")
    app = create_app(
        bot,
        create_dispatcher(),
        Webhook("https://bot.example", SECRET),
        sign_in(Tables()),
        miniapp(Tables()),
    )
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as http:
        yield http
    await bot.session.close()


async def test_health_is_ok(client: AsyncClient) -> None:
    response = await client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}


async def test_webhook_rejects_a_call_without_the_secret(client: AsyncClient) -> None:
    response = await client.post(WEBHOOK_PATH, json={"update_id": 1})

    assert response.status_code == 401


async def test_webhook_accepts_an_update_with_the_secret(client: AsyncClient) -> None:
    response = await client.post(
        WEBHOOK_PATH,
        json=TEXT_MESSAGE,
        headers={"X-Telegram-Bot-Api-Secret-Token": SECRET},
    )

    assert response.status_code == 200
