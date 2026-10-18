import json
from collections.abc import AsyncGenerator
from typing import Any

import pytest
from aiogram import Bot
from aiogram.client.session.base import BaseSession
from aiogram.methods import TelegramMethod
from aiogram.methods.base import TelegramType

from stickersnap_bot.application.errors import (
    RateLimitedError,
    StickerNotFoundError,
    TelegramUnavailableError,
)
from stickersnap_bot.domain.packs import PackKind
from stickersnap_bot.infrastructure.telegram.sticker_sets import BotStickerSets

CATS = "cats_by_StickerSnapAppBot"


def sticker(file_id: str, thumb: str | None) -> dict[str, Any]:
    found: dict[str, Any] = {
        "file_id": file_id,
        "file_unique_id": "u" + file_id,
        "type": "regular",
        "width": 512,
        "height": 512,
        "is_animated": False,
        "is_video": True,
        "emoji": "😴",
        "set_name": CATS,
    }
    if thumb:
        found["thumbnail"] = {
            "file_id": thumb,
            "file_unique_id": "u" + thumb,
            "width": 128,
            "height": 128,
        }
    return found


def ok(result: Any) -> tuple[int, dict[str, Any]]:
    return 200, {"ok": True, "result": result}


def bad_request(description: str) -> tuple[int, dict[str, Any]]:
    return 400, {"ok": False, "error_code": 400, "description": f"Bad Request: {description}"}


class FakeBotApi(BaseSession):
    """Answers Bot API methods with canned responses, checked by aiogram as real ones are."""

    def __init__(self) -> None:
        super().__init__()
        self.answers: dict[str, tuple[int, dict[str, Any]]] = {}
        self.files: dict[str, bytes] = {}
        self.calls: list[str] = []

    async def make_request(
        self,
        bot: Bot,
        method: TelegramMethod[TelegramType],
        timeout: int | None = None,  # noqa: ASYNC109
    ) -> TelegramType:
        name = type(method).__name__
        self.calls.append(name)
        status, body = self.answers[name]
        response = self.check_response(bot, method, status, json.dumps(body))
        return response.result  # type: ignore[return-value]

    async def stream_content(
        self,
        url: str,
        headers: dict[str, Any] | None = None,
        timeout: int = 30,  # noqa: ASYNC109
        chunk_size: int = 65536,
        raise_for_status: bool = True,
    ) -> AsyncGenerator[bytes]:
        self.calls.append(url.rsplit("/", 1)[-1])
        yield self.files[url.rsplit("/", 1)[-1]]

    async def close(self) -> None:
        pass


@pytest.fixture
def api() -> FakeBotApi:
    return FakeBotApi()


@pytest.fixture
def sets(api: FakeBotApi) -> BotStickerSets:
    return BotStickerSets(Bot("123:token", session=api))


async def test_reads_a_set_in_telegrams_order(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetStickerSet"] = ok(
        {
            "name": CATS,
            "title": "Котики",
            "sticker_type": "regular",
            "stickers": [sticker("a", "ta"), sticker("b", None)],
        }
    )

    found = await sets.get(CATS)

    assert found is not None
    assert found.title == "Котики"
    assert found.kind is PackKind.STICKERS
    assert [(s.id, s.thumb_file_id, s.emoji) for s in found.stickers] == [
        ("a", "ta", ("😴",)),
        ("b", None, ("😴",)),
    ]


async def test_an_emoji_set_is_emoji(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetStickerSet"] = ok(
        {"name": CATS, "title": "E", "sticker_type": "custom_emoji", "stickers": []}
    )

    found = await sets.get(CATS)

    assert found is not None
    assert found.kind is PackKind.EMOJI


async def test_a_set_telegram_does_not_know_is_none(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetStickerSet"] = bad_request("STICKERSET_INVALID")

    assert await sets.get(CATS) is None


async def test_flood_control_becomes_rate_limited(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetStickerSet"] = (
        429,
        {
            "ok": False,
            "error_code": 429,
            "description": "Too Many Requests: retry after 24",
            "parameters": {"retry_after": 24},
        },
    )

    with pytest.raises(RateLimitedError) as raised:
        await sets.get(CATS)

    assert raised.value.retry_after_sec == 24


async def test_a_server_error_becomes_unavailable(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["DeleteStickerFromSet"] = (
        500,
        {"ok": False, "error_code": 500, "description": "Internal Server Error"},
    )

    with pytest.raises(TelegramUnavailableError):
        await sets.delete_sticker("a")


async def test_deleting_an_unknown_sticker_is_not_found(
    api: FakeBotApi, sets: BotStickerSets
) -> None:
    api.answers["DeleteStickerFromSet"] = bad_request("STICKER_INVALID")

    with pytest.raises(StickerNotFoundError):
        await sets.delete_sticker("a")


async def test_downloads_a_file_once_and_keeps_it(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetFile"] = ok(
        {"file_id": "ta", "file_unique_id": "uta", "file_path": "thumbnails/file_1.webp"}
    )
    api.files["file_1.webp"] = b"RIFF....WEBP"

    first = await sets.download("ta")
    second = await sets.download("ta")

    assert first is not None
    assert first.media_type == "image/webp"
    assert first.content == b"RIFF....WEBP"
    assert second == first
    assert api.calls == ["GetFile", "file_1.webp"]


async def test_an_unknown_file_is_none(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetFile"] = bad_request("invalid file_id")

    assert await sets.download("nope") is None
