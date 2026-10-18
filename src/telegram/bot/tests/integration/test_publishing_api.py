"""Publishing through httpx, the desktop's routes over the in-memory fakes; bodies are checked
against src/contract/bot-service.yaml as in test_miniapp_api.py."""

from collections.abc import AsyncIterator

import pytest
from aiogram import Bot
from httpx import ASGITransport, AsyncClient

from stickersnap_bot.api.app import Polling, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.application.errors import RateLimitedError
from stickersnap_bot.application.publishing import MAX_STICKER_BYTES
from stickersnap_bot.application.sticker_sets import StickerFile
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import (
    NOW,
    TOKEN,
    FakeChatMessages,
    FakeStickerSets,
    Tables,
    bot_chat,
    linking,
    miniapp,
    notifications,
    publishing,
    send_feedback,
    sign_in,
)
from tests.integration.test_links_api import bearer, is_schema, problem
from tests.integration.test_miniapp_api import conforms

ME = 42
KEY = "device-key"
CATS = "cats_by_StickerSnapAppBot"
WEBM = b"\x1a\x45\xdf\xa3" + b"\x00" * 100


@pytest.fixture
def tables() -> Tables:
    tables = Tables()
    tables.users[ME] = User.first_seen(ME, TelegramProfile("Дима"), NOW)
    tables.devices["pc"] = Device(
        "pc", ME, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, hash_secret(KEY), NOW
    )
    return tables


@pytest.fixture
def sets() -> FakeStickerSets:
    return FakeStickerSets()


@pytest.fixture
def messages() -> FakeChatMessages:
    return FakeChatMessages()


@pytest.fixture
async def client(
    tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> AsyncIterator[AsyncClient]:
    bot = Bot(TOKEN)
    app = create_app(
        bot,
        create_dispatcher(bot_chat(tables)),
        Polling(),
        sign_in(tables),
        miniapp(tables, sets),
        notifications(tables),
        send_feedback(tables),
        linking(tables),
        publishing(tables, sets, messages),
    )
    transport = ASGITransport(app=app)
    headers = bearer(KEY)
    async with AsyncClient(transport=transport, base_url="http://test", headers=headers) as http:
        yield http
    await bot.session.close()


async def upload(client: AsyncClient, webm: bytes = WEBM) -> str:
    response = await client.post(
        "/v1/stickers/upload", content=webm, headers={"Content-Type": "video/webm"}
    )
    assert response.status_code == 201
    file_id: str = conforms(response, "UploadedSticker")["id"]
    return file_id


async def test_a_desktop_publishes_a_pack(
    client: AsyncClient, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    first = await upload(client)
    created = await client.post(
        "/v1/packs",
        json=is_schema(
            "NewPackBody",
            {
                "name": CATS,
                "title": "Коты",
                "kind": "stickers",
                "stickers": [{"file": first, "emoji": ["😂", "🐱"]}],
            },
        ),
    )
    second = await upload(client)
    added = await client.post(
        f"/v1/packs/{CATS}/stickers",
        json=is_schema("InputSticker", {"file": second, "emoji": ["🔥"]}),
    )
    third = await upload(client)
    replaced = await client.put(
        f"/v1/packs/{CATS}/stickers/sticker-{first}", json={"file": third, "emoji": ["😴"]}
    )
    read = await client.get(f"/v1/packs/{CATS}")

    assert created.status_code == 201
    assert conforms(created, "PublishedPack") == {
        "name": CATS,
        "title": "Коты",
        "kind": "stickers",
        "stickers": [{"id": "sticker-file1", "uniqueId": "unique-file1", "emoji": ["😂", "🐱"]}],
    }
    assert added.status_code == 200
    assert [s["id"] for s in conforms(added, "PublishedPack")["stickers"]] == [
        "sticker-file1",
        "sticker-file2",
    ]
    assert replaced.status_code == 200
    assert conforms(read, "PublishedPack") == conforms(replaced, "PublishedPack")
    assert [s["id"] for s in read.json()["stickers"]] == ["sticker-file3", "sticker-file2"]
    assert [u for u, _ in sets.uploaded] == [ME, ME, ME]
    assert [m.message.title for m in messages.sent] == ["Пак «Коты» создан", None]


async def test_deletes_a_sticker_and_downloads_a_file(
    client: AsyncClient, sets: FakeStickerSets
) -> None:
    file_id = await upload(client)
    await client.post(
        "/v1/packs",
        json={
            "name": CATS,
            "title": "Коты",
            "kind": "emoji",
            "stickers": [{"file": file_id, "emoji": ["😂"]}] * 2,
        },
    )
    sets.files["sticker-file1"] = StickerFile(b"webm bytes", "video/webm")

    downloaded = await client.get("/v1/stickers/sticker-file1/file")
    deleted = await client.delete(f"/v1/packs/{CATS}/stickers/sticker-file1")
    missing = await client.get("/v1/stickers/sticker-gone/file")

    assert downloaded.status_code == 200
    assert downloaded.content == b"webm bytes"
    assert downloaded.headers["content-type"] == "video/webm"
    assert deleted.status_code == 204
    assert sets.deleted == ["sticker-file1"]
    assert problem(missing, 404) == "sticker_not_found"


async def test_every_route_wants_the_device_key(client: AsyncClient) -> None:
    sticker = {"file": "file1", "emoji": ["😂"]}
    pack = {"name": CATS, "title": "Коты", "kind": "stickers", "stickers": [sticker]}
    for revoked in ({"Authorization": ""}, bearer("revoked")):
        calls = [
            client.post("/v1/stickers/upload", content=WEBM, headers=revoked),
            client.post("/v1/packs", json=pack, headers=revoked),
            client.get(f"/v1/packs/{CATS}", headers=revoked),
            client.post(f"/v1/packs/{CATS}/stickers", json=sticker, headers=revoked),
            client.put(f"/v1/packs/{CATS}/stickers/a", json=sticker, headers=revoked),
            client.delete(f"/v1/packs/{CATS}/stickers/a", headers=revoked),
            client.get("/v1/stickers/a/file", headers=revoked),
        ]
        for call in calls:
            assert problem(await call, 401) == "unauthorized"


async def test_a_file_over_the_limit_is_refused(client: AsyncClient) -> None:
    response = await client.post("/v1/stickers/upload", content=WEBM + b"\x00" * MAX_STICKER_BYTES)

    assert problem(response, 422) == "validation_failed"


async def test_contract_codes_of_publishing(
    client: AsyncClient, tables: Tables, sets: FakeStickerSets
) -> None:
    sticker = {"file": "file1", "emoji": ["😂"]}

    def pack(name: str) -> dict[str, object]:
        return {"name": name, "title": "Коты", "kind": "stickers", "stickers": [sticker]}

    bad_name = await client.post("/v1/packs", json=pack("cats_by_OtherBot"))
    not_ours = await client.post(f"/v1/packs/{CATS}/stickers", json=sticker)
    made = await client.post("/v1/packs", json=pack(CATS))
    taken = await client.post("/v1/packs", json=pack(CATS))
    no_emoji = await client.post(f"/v1/packs/{CATS}/stickers", json={"file": "file1", "emoji": []})
    tables.users[ME] = tables.users[ME].with_bot_blocked(True, NOW)
    blocked = await client.post(f"/v1/packs/{CATS}/stickers", json=sticker)
    tables.users[ME] = tables.users[ME].with_bot_blocked(False, NOW)
    sets.failure = RateLimitedError(24)
    waiting = await client.post(f"/v1/packs/{CATS}/stickers", json=sticker)

    assert problem(bad_name, 422) == "pack_name_invalid"
    assert problem(not_ours, 404) == "pack_not_found"
    assert made.status_code == 201
    assert problem(taken, 409) == "pack_name_taken"
    assert problem(no_emoji, 400) == "bad_request"
    assert problem(blocked, 403) == "bot_blocked"
    assert problem(waiting, 429) == "rate_limited"
    assert waiting.headers["Retry-After"] == "24"
    assert waiting.json()["params"] == {"retryAfterSec": 24}
