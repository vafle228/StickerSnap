"""The Mini App's routes through httpx, over the in-memory fakes; every body is checked against
the contract's schema in src/contract/bot-service.yaml."""

from collections.abc import AsyncIterator
from datetime import timedelta
from pathlib import Path
from typing import Any

import pytest
import yaml
from aiogram import Bot
from httpx import ASGITransport, AsyncClient, Response
from jsonschema import Draft202012Validator

from stickersnap_bot.api.app import Polling, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.application.errors import RateLimitedError, TelegramUnavailableError
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.sticker_sets import StickerFile
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.packs import Pack, PackKind
from tests.fakes import (
    NOW,
    TOKEN,
    FakeStickerSets,
    Tables,
    bot_chat,
    miniapp,
    notifications,
    send_feedback,
    sign_in,
    signed_launch,
    sticker_set,
)

CONTRACT = Path(__file__).parents[5] / "src" / "contract" / "bot-service.yaml"
COMPONENTS = yaml.safe_load(CONTRACT.read_text(encoding="utf-8"))["components"]
ME, SOMEONE = 42, 7
CATS = "cats_by_StickerSnapAppBot"


def conforms(response: Response, schema: str) -> Any:
    body = response.json()
    validator = Draft202012Validator(
        {"$ref": f"#/components/schemas/{schema}", "components": COMPONENTS}
    )
    validator.validate(body)
    return body


@pytest.fixture
def tables() -> Tables:
    return Tables()


@pytest.fixture
def sets() -> FakeStickerSets:
    return FakeStickerSets()


@pytest.fixture
async def client(tables: Tables, sets: FakeStickerSets) -> AsyncIterator[AsyncClient]:
    # The lifespan (polling) is not run, so nothing reaches Telegram
    bot = Bot(TOKEN)
    app = create_app(
        bot,
        create_dispatcher(bot_chat(tables)),
        Polling(),
        sign_in(tables),
        miniapp(tables, sets),
        notifications(tables),
        send_feedback(tables),
    )
    headers = {"Authorization": f"tma {signed_launch(ME)}"}
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test", headers=headers) as http:
        yield http
    await bot.session.close()


def add_pack(tables: Tables, name: str = CATS, owner: int = ME) -> None:
    tables.packs[name] = Pack.create(name, owner, "Котики", PackKind.STICKERS, NOW)


@pytest.mark.parametrize(
    "authorization",
    [None, "", "tma ", "Bearer x", f"tma {signed_launch(ME)}x", "tma user=1&hash=00"],
)
async def test_a_call_without_valid_launch_data_is_unauthorized(
    client: AsyncClient, authorization: str | None
) -> None:
    headers = {} if authorization is None else {"Authorization": authorization}
    client.headers.pop("Authorization")

    response = await client.get("/v1/miniapp/packs", headers=headers)

    assert response.status_code == 401
    assert response.headers["content-type"] == "application/problem+json"
    assert conforms(response, "Problem")["code"] == "unauthorized"


async def test_old_launch_data_is_unauthorized(client: AsyncClient) -> None:
    old = signed_launch(ME, now=NOW - timedelta(days=2))

    response = await client.get("/v1/miniapp/packs", headers={"Authorization": f"tma {old}"})

    assert response.status_code == 401


async def test_lists_packs_by_the_contract(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    add_pack(tables, "lost_by_StickerSnapAppBot")
    sets.sets = {CATS: sticker_set(CATS, 2)}

    response = await client.get("/v1/miniapp/packs")

    assert response.status_code == 200
    packs = conforms(response, "PackList")["packs"]
    by_name = {p["name"]: p for p in packs}
    assert by_name[CATS]["count"] == 2
    assert by_name[CATS]["limit"] == 120
    assert by_name[CATS]["coverUrl"].startswith("/v1/miniapp/media/thumb1.")
    assert by_name[CATS]["updatedAt"] == "2026-10-18T09:00:00Z"
    # an optional field is absent, never null
    assert "coverUrl" not in by_name["lost_by_StickerSnapAppBot"]
    assert by_name["lost_by_StickerSnapAppBot"]["state"] == "missing"
    assert tables.users[ME].profile.first_name == "Дима"


async def test_gets_a_pack_with_its_stickers(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.sets = {CATS: sticker_set(CATS, 2)}

    response = await client.get(f"/v1/miniapp/packs/{CATS}")

    assert response.status_code == 200
    pack = conforms(response, "PackDetail")
    assert [s["id"] for s in pack["stickers"]] == ["sticker1", "sticker2"]
    assert pack["stickers"][0]["emoji"] == ["😴"]
    assert pack["stickers"][0]["videoUrl"].startswith("/v1/miniapp/media/video1.")


async def test_someone_elses_pack_is_not_found(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables, owner=SOMEONE)
    sets.sets = {CATS: sticker_set(CATS, 2)}

    response = await client.get(f"/v1/miniapp/packs/{CATS}")

    assert response.status_code == 404
    assert conforms(response, "Problem")["code"] == "pack_not_found"


async def test_a_name_that_is_no_set_name_is_a_bad_request(client: AsyncClient) -> None:
    response = await client.get("/v1/miniapp/packs/1cats")

    assert response.status_code == 400
    assert conforms(response, "Problem")["code"] == "bad_request"


async def test_deletes_a_sticker(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.sets = {CATS: sticker_set(CATS, 2)}

    response = await client.delete(f"/v1/miniapp/packs/{CATS}/stickers/sticker1")

    assert response.status_code == 204
    assert sets.deleted == ["sticker1"]


async def test_an_unknown_sticker_is_not_found(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.sets = {CATS: sticker_set(CATS, 2)}

    response = await client.delete(f"/v1/miniapp/packs/{CATS}/stickers/nope")

    assert response.status_code == 404
    assert conforms(response, "Problem")["code"] == "sticker_not_found"


async def test_telegrams_flood_control_reaches_the_mini_app(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.failure = RateLimitedError(24)

    response = await client.delete(f"/v1/miniapp/packs/{CATS}/stickers/sticker1")

    assert response.status_code == 429
    assert response.headers["retry-after"] == "24"
    problem = conforms(response, "Problem")
    assert problem["code"] == "rate_limited"
    assert problem["params"] == {"retryAfterSec": 24}


async def test_telegram_down_is_a_telegram_error(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.failure = TelegramUnavailableError("timeout")

    response = await client.get("/v1/miniapp/packs")

    assert response.status_code == 502
    assert conforms(response, "Problem")["code"] == "telegram_error"


async def test_forgets_a_missing_pack_but_not_one_in_telegram(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    add_pack(tables)
    sets.sets = {CATS: sticker_set(CATS, 1)}

    kept = await client.delete(f"/v1/miniapp/packs/{CATS}")
    sets.sets = {}
    forgotten = await client.delete(f"/v1/miniapp/packs/{CATS}")

    assert kept.status_code == 409
    assert conforms(kept, "Problem")["code"] == "pack_in_telegram"
    assert forgotten.status_code == 204
    assert tables.packs == {}


async def test_lists_and_unlinks_devices(client: AsyncClient, tables: Tables) -> None:
    tables.devices["d1"] = Device("d1", ME, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, "h", NOW)

    listed = await client.get("/v1/miniapp/devices")
    unlinked = await client.delete("/v1/miniapp/devices/d1")
    again = await client.delete("/v1/miniapp/devices/d1")

    assert conforms(listed, "DeviceList")["devices"] == [
        {"id": "d1", "name": "DESKTOP-7F3K2Q", "os": "windows", "linkedAt": "2026-10-18T09:00:00Z"}
    ]
    assert unlinked.status_code == 204
    assert again.status_code == 404
    assert conforms(again, "Problem")["code"] == "device_not_found"


async def test_a_media_link_serves_the_file_without_a_header(
    client: AsyncClient, sets: FakeStickerSets
) -> None:
    sets.files["thumb1"] = StickerFile(b"RIFF....WEBP", "image/webp")
    client.headers.pop("Authorization")
    token = MediaLinks(TOKEN).token("thumb1", NOW)

    served = await client.get(f"/v1/miniapp/media/{token}")
    forged = await client.get(f"/v1/miniapp/media/{token[:-1]}x")

    assert served.status_code == 200
    assert served.content == b"RIFF....WEBP"
    assert served.headers["content-type"] == "image/webp"
    assert served.headers["cache-control"] == "private, max-age=7200"
    assert forged.status_code == 404
