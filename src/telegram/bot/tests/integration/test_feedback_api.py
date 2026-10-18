"""POST /v1/feedback through httpx, over the in-memory fakes; bodies are checked against
src/contract/bot-service.yaml as in test_miniapp_api.py."""

from collections.abc import AsyncIterator
from typing import Any

import pytest
from aiogram import Bot
from httpx import ASGITransport, AsyncClient
from jsonschema import Draft202012Validator

from stickersnap_bot.api.app import Polling, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.feedback import FeedbackSource
from stickersnap_bot.domain.secrets import hash_secret
from tests.fakes import (
    NOW,
    TOKEN,
    FakeFeedbackInbox,
    Tables,
    bot_chat,
    linking,
    miniapp,
    notifications,
    publishing,
    send_feedback,
    sign_in,
    signed_launch,
)
from tests.integration.test_miniapp_api import COMPONENTS, conforms

ME = 42
KEY = "device-key"
INSTALLATION = "0b9a3c1e-5d7f-4e2a-9c21-7d4e5f6a8b90"
IDEA = {"type": "idea", "text": "Хочу менять порядок стикеров", "canReply": True}


@pytest.fixture
def tables() -> Tables:
    tables = Tables()
    tables.devices["pc"] = Device(
        "pc", ME, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, hash_secret(KEY), NOW
    )
    return tables


@pytest.fixture
def inbox() -> FakeFeedbackInbox:
    return FakeFeedbackInbox()


@pytest.fixture
async def client(tables: Tables, inbox: FakeFeedbackInbox) -> AsyncIterator[AsyncClient]:
    bot = Bot(TOKEN)
    app = create_app(
        bot,
        create_dispatcher(bot_chat(tables)),
        Polling(),
        sign_in(tables),
        miniapp(tables),
        notifications(tables),
        send_feedback(tables, inbox),
        linking(tables),
        publishing(tables),
    )
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as http:
        yield http
    await bot.session.close()


def is_request(body: dict[str, Any]) -> dict[str, Any]:
    """What the tests send is a FeedbackRequest of the contract too."""
    Draft202012Validator(
        {"$ref": "#/components/schemas/FeedbackRequest", "components": COMPONENTS}
    ).validate(body)
    return body


async def test_the_miniapp_sends_feedback_with_its_launch_data(
    client: AsyncClient, tables: Tables, inbox: FakeFeedbackInbox
) -> None:
    response = await client.post(
        "/v1/feedback",
        json=is_request(IDEA | {"platform": "ios"}),
        headers={"Authorization": f"tma {signed_launch(ME)}"},
    )

    assert response.status_code == 201
    number = conforms(response, "FeedbackCreated")["id"]
    assert tables.feedback[number].author.source is FeedbackSource.MINIAPP
    assert inbox.delivered[0][1] == tables.users[ME]


async def test_a_linked_computer_sends_a_bug_with_its_key(
    client: AsyncClient, tables: Tables
) -> None:
    bug = {
        "type": "bug",
        "text": "",
        "canReply": False,
        "installationId": INSTALLATION,
        "appVersion": "0.3.1",
        "platform": "Windows 11",
        "error": {"code": "render_failed", "traceId": "7f3a-c21e"},
        "diagnostics": "журнал",
    }

    response = await client.post(
        "/v1/feedback", json=is_request(bug), headers={"Authorization": f"Bearer {KEY}"}
    )

    assert response.status_code == 201
    stored = tables.feedback[response.json()["id"]]
    assert (stored.author.device_id, stored.author.installation_id) == ("pc", INSTALLATION)
    assert stored.error is not None
    assert stored.error.trace_id == "7f3a-c21e"


async def test_a_desktop_without_a_link_sends_without_authorization(
    client: AsyncClient, tables: Tables
) -> None:
    body = IDEA | {"installationId": INSTALLATION, "contact": "me@example.com"}

    response = await client.post("/v1/feedback", json=is_request(body))

    assert response.status_code == 201
    stored = tables.feedback[response.json()["id"]]
    assert stored.author.user_id is None
    # the test client's address, hashed
    assert stored.author.ip_hash == hash_secret("127.0.0.1")


@pytest.mark.parametrize(
    "authorization", ["Bearer revoked", "Bearer ", "tma ", f"tma {signed_launch(ME)}x", "Basic x"]
)
async def test_a_bad_authorization_is_unauthorized(
    client: AsyncClient, tables: Tables, authorization: str
) -> None:
    response = await client.post(
        "/v1/feedback", json=IDEA, headers={"Authorization": authorization}
    )

    assert response.status_code == 401
    assert conforms(response, "Problem")["code"] == "unauthorized"
    assert tables.feedback == {}


@pytest.mark.parametrize(
    "body",
    [
        # the schema's rules
        IDEA | {"type": "praise"},
        IDEA | {"text": "x" * 2001},
        IDEA | {"installationId": "not-a-uuid"},
        IDEA | {"extra": 1},
        {"type": "idea", "text": "Идея"},
        # the rules beyond the schema
        IDEA | {"text": "  "},
        IDEA | {"error": {"code": "render_failed"}},
        # no link and no installation
        IDEA,
    ],
)
async def test_a_body_out_of_contract_is_a_bad_request(
    client: AsyncClient, body: dict[str, Any]
) -> None:
    response = await client.post("/v1/feedback", json=body)

    assert response.status_code == 400
    assert conforms(response, "Problem")["code"] == "bad_request"


async def test_the_sixth_in_an_hour_waits(client: AsyncClient) -> None:
    headers = {"Authorization": f"Bearer {KEY}"}
    for _ in range(5):
        assert (await client.post("/v1/feedback", json=IDEA, headers=headers)).status_code == 201

    response = await client.post("/v1/feedback", json=IDEA, headers=headers)

    assert response.status_code == 429
    problem = conforms(response, "Problem")
    assert problem["code"] == "rate_limited"
    assert problem["params"]["retryAfterSec"] == 3600
    assert response.headers["Retry-After"] == "3600"
