"""Linking through httpx, the desktop's and the Mini App's routes, over the in-memory fakes; bodies
are checked against src/contract/bot-service.yaml as in test_miniapp_api.py."""

from collections.abc import AsyncIterator
from typing import Any

import pytest
from aiogram import Bot
from httpx import ASGITransport, AsyncClient, Response
from jsonschema import Draft202012Validator

from stickersnap_bot.api.app import Polling, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.domain.links import LINK_LIFETIME
from tests.fakes import (
    TOKEN,
    FakeClock,
    Tables,
    bot_chat,
    linking,
    miniapp,
    notifications,
    send_feedback,
    sign_in,
    signed_launch,
)
from tests.integration.test_miniapp_api import COMPONENTS, conforms

ME, SOMEONE = 42, 7
PC = {
    "name": "DESKTOP-7F3K2Q",
    "os": "windows",
    "installationId": "0b9a3c1e-5d7f-4e2a-9c21-7d4e5f6a8b90",
    "platform": "Windows 11",
    "appVersion": "0.3.1",
}


def tma(user_id: int) -> dict[str, str]:
    return {"Authorization": f"tma {signed_launch(user_id)}"}


def bearer(secret: str) -> dict[str, str]:
    return {"Authorization": f"Bearer {secret}"}


def is_schema(schema: str, body: dict[str, Any]) -> dict[str, Any]:
    """What the tests send is the contract's body too."""
    Draft202012Validator(
        {"$ref": f"#/components/schemas/{schema}", "components": COMPONENTS}
    ).validate(body)
    return body


def problem(response: Response, status: int) -> str:
    assert response.status_code == status
    code: str = conforms(response, "Problem")["code"]
    return code


@pytest.fixture
def clock() -> FakeClock:
    return FakeClock()


@pytest.fixture
async def client(clock: FakeClock) -> AsyncIterator[AsyncClient]:
    bot = Bot(TOKEN)
    tables = Tables()
    app = create_app(
        bot,
        create_dispatcher(bot_chat(tables)),
        Polling(),
        sign_in(tables),
        miniapp(tables),
        notifications(tables),
        send_feedback(tables),
        linking(tables, clock=clock),
    )
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as http:
        yield http
    await bot.session.close()


async def new_link(client: AsyncClient) -> dict[str, Any]:
    response = await client.post("/v1/links", json=is_schema("LinkRequestBody", PC))
    assert response.status_code == 201
    return conforms(response, "LinkCreated")  # type: ignore[no-any-return]


async def test_a_desktop_links_itself(client: AsyncClient) -> None:
    link = await new_link(client)
    code, poll = link["code"], bearer(link["pollToken"])
    assert link["deepLink"] == f"https://t.me/StickerSnapAppBot?startapp=link_{code}"

    waiting = await client.get(f"/v1/links/{code}", headers=poll)
    question = await client.get(f"/v1/miniapp/links/{code}", headers=tma(ME))
    confirmed = await client.post(
        f"/v1/miniapp/links/{code}/confirm",
        headers=tma(ME),
        json=is_schema("LinkConfirmBody", {"notifications": True}),
    )
    linked = await client.get(f"/v1/links/{code}?wait=0", headers=poll)

    assert conforms(waiting, "LinkState") == {"status": "pending", "expiresAt": link["expiresAt"]}
    assert conforms(question, "LinkRequest") == {
        "code": code,
        "name": "DESKTOP-7F3K2Q",
        "os": "windows",
        "platform": "Windows 11",
        "appVersion": "0.3.1",
        "requestedAt": "2026-10-18T09:00:00Z",
        "expiresAt": link["expiresAt"],
        "linkedHere": False,
    }
    assert confirmed.status_code == 204
    state = conforms(linked, "LinkState")
    assert state["status"] == "linked"
    assert state["user"] == {"id": ME, "firstName": "Дима", "botBlocked": False}
    assert state["device"]["name"] == "DESKTOP-7F3K2Q"
    key = bearer(state["deviceKey"])
    device = await client.get("/v1/device", headers=key)
    assert conforms(device, "LinkedDevice") == {"device": state["device"], "user": state["user"]}
    devices = await client.get("/v1/miniapp/devices", headers=tma(ME))
    assert [d["id"] for d in devices.json()["devices"]] == [state["device"]["id"]]


async def test_the_key_is_handed_out_once(client: AsyncClient) -> None:
    link = await new_link(client)
    await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm", headers=tma(ME), json={"notifications": True}
    )
    await client.get(f"/v1/links/{link['code']}", headers=bearer(link["pollToken"]))

    again = await client.get(f"/v1/links/{link['code']}", headers=bearer(link["pollToken"]))

    assert conforms(again, "LinkState") == {"status": "expired"}


async def test_declining_reaches_the_desktop(client: AsyncClient) -> None:
    link = await new_link(client)

    declined = await client.post(f"/v1/miniapp/links/{link['code']}/decline", headers=tma(ME))
    state = await client.get(f"/v1/links/{link['code']}", headers=bearer(link["pollToken"]))

    assert declined.status_code == 204
    assert conforms(state, "LinkState") == {"status": "declined"}


async def test_cancelling_makes_the_link_out_of_date(client: AsyncClient) -> None:
    link = await new_link(client)

    cancelled = await client.delete(f"/v1/links/{link['code']}", headers=bearer(link["pollToken"]))
    question = await client.get(f"/v1/miniapp/links/{link['code']}", headers=tma(ME))

    assert cancelled.status_code == 204
    assert problem(question, 404) == "link_not_found"


async def test_an_expired_link_is_out_of_date_for_the_mini_app(
    client: AsyncClient, clock: FakeClock
) -> None:
    link = await new_link(client)
    clock.advance(LINK_LIFETIME)

    question = await client.get(f"/v1/miniapp/links/{link['code']}", headers=tma(ME))
    confirmed = await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm", headers=tma(ME), json={"notifications": True}
    )

    assert problem(question, 404) == "link_not_found"
    assert problem(confirmed, 404) == "link_not_found"


@pytest.mark.parametrize("headers", [{}, bearer("wrong"), {"Authorization": "tma x"}])
async def test_without_its_poll_token_a_link_is_not_found(
    client: AsyncClient, headers: dict[str, str]
) -> None:
    link = await new_link(client)

    state = await client.get(f"/v1/links/{link['code']}", headers=headers)
    cancelled = await client.delete(f"/v1/links/{link['code']}", headers=headers)

    assert problem(state, 404) == "link_not_found"
    assert problem(cancelled, 404) == "link_not_found"


async def test_the_mini_app_routes_need_launch_data(client: AsyncClient) -> None:
    link = await new_link(client)

    question = await client.get(f"/v1/miniapp/links/{link['code']}")
    declined = await client.post(
        f"/v1/miniapp/links/{link['code']}/decline", headers=bearer(link["pollToken"])
    )

    assert problem(question, 401) == "unauthorized"
    assert problem(declined, 401) == "unauthorized"


@pytest.mark.parametrize("headers", [{}, bearer(""), bearer("unknown"), tma(ME)])
async def test_the_device_routes_need_a_known_key(
    client: AsyncClient, headers: dict[str, str]
) -> None:
    assert problem(await client.get("/v1/device", headers=headers), 401) == "unauthorized"
    assert problem(await client.delete("/v1/device", headers=headers), 401) == "unauthorized"


async def test_unlinking_revokes_the_key(client: AsyncClient) -> None:
    link = await new_link(client)
    await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm", headers=tma(ME), json={"notifications": False}
    )
    state = (
        await client.get(f"/v1/links/{link['code']}", headers=bearer(link["pollToken"]))
    ).json()

    unlinked = await client.delete("/v1/device", headers=bearer(state["deviceKey"]))
    after = await client.get("/v1/device", headers=bearer(state["deviceKey"]))

    assert unlinked.status_code == 204
    assert problem(after, 401) == "unauthorized"


@pytest.mark.parametrize(
    "body",
    [
        {**PC, "name": ""},
        {**PC, "name": "x" * 65},
        {**PC, "os": "beos"},
        {**PC, "installationId": "not-a-uuid"},
        {**PC, "platform": "x" * 65},
        {**PC, "extra": 1},
        {"name": "PC", "os": "linux"},
    ],
)
async def test_a_link_request_out_of_contract_is_a_bad_request(
    client: AsyncClient, body: dict[str, Any]
) -> None:
    assert problem(await client.post("/v1/links", json=body), 400) == "bad_request"


@pytest.mark.parametrize("query", ["wait=-1", "wait=26", "wait=x"])
async def test_a_wait_out_of_range_is_a_bad_request(client: AsyncClient, query: str) -> None:
    link = await new_link(client)

    response = await client.get(f"/v1/links/{link['code']}?{query}", headers=bearer("x"))

    assert problem(response, 400) == "bad_request"


async def test_confirm_needs_the_notifications_switch(client: AsyncClient) -> None:
    link = await new_link(client)

    response = await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm", headers=tma(ME), json={}
    )

    assert problem(response, 400) == "bad_request"


async def test_too_many_codes_are_rate_limited(client: AsyncClient) -> None:
    for _ in range(10):
        await new_link(client)

    response = await client.post("/v1/links", json=PC)

    assert problem(response, 429) == "rate_limited"
    assert response.headers["Retry-After"] == "3600"


async def test_someone_else_cannot_take_a_confirmed_link(client: AsyncClient) -> None:
    link = await new_link(client)
    await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm", headers=tma(ME), json={"notifications": True}
    )

    response = await client.post(
        f"/v1/miniapp/links/{link['code']}/confirm",
        headers=tma(SOMEONE),
        json={"notifications": True},
    )

    assert problem(response, 404) == "link_not_found"
