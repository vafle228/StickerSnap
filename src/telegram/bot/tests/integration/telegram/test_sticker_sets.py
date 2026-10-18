from typing import Any

import pytest
from aiogram import Bot
from aiogram.methods import (
    AddStickerToSet,
    CreateNewStickerSet,
    ReplaceStickerInSet,
    UploadStickerFile,
)
from aiogram.types import BufferedInputFile

from stickersnap_bot.application.errors import (
    ApplicationError,
    BotBlockedError,
    PackFullError,
    PackNameTakenError,
    PackNotFoundError,
    RateLimitedError,
    StickerNotFoundError,
    TelegramUnavailableError,
)
from stickersnap_bot.application.sticker_sets import NewSticker
from stickersnap_bot.domain.packs import PackKind
from stickersnap_bot.infrastructure.telegram.sticker_sets import BotStickerSets
from tests.integration.telegram.bot_api import FakeBotApi, bad_request, ok

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
    assert [(s.id, s.unique_id, s.thumb_file_id, s.emoji) for s in found.stickers] == [
        ("a", "ua", "ta", ("😴",)),
        ("b", "ub", None, ("😴",)),
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


SMILE = NewSticker("uploaded", ("😂", "🐱"))
ME = 42


def forbidden(description: str) -> tuple[int, dict[str, Any]]:
    return 403, {"ok": False, "error_code": 403, "description": f"Forbidden: {description}"}


async def test_knows_the_bots_username(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["GetMe"] = ok(
        {"id": 1, "is_bot": True, "first_name": "S", "username": "StickerSnapAppBot"}
    )

    assert await sets.bot_username() == "StickerSnapAppBot"
    assert await sets.bot_username() == "StickerSnapAppBot"
    assert api.calls == ["GetMe"]


async def test_uploads_a_video_sticker_for_its_owner(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["UploadStickerFile"] = ok({"file_id": "uploaded", "file_unique_id": "uu"})

    assert await sets.upload(ME, b"webm") == "uploaded"

    [method] = api.methods
    assert isinstance(method, UploadStickerFile)
    assert (method.user_id, method.sticker_format) == (ME, "video")
    assert isinstance(method.sticker, BufferedInputFile)
    assert method.sticker.data == b"webm"


async def test_creates_a_set_of_video_stickers(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["CreateNewStickerSet"] = ok(True)

    await sets.create(ME, CATS, "Котики", PackKind.EMOJI, [SMILE])

    [method] = api.methods
    assert isinstance(method, CreateNewStickerSet)
    assert (method.user_id, method.name, method.title) == (ME, CATS, "Котики")
    assert method.sticker_type == "custom_emoji"
    [made] = method.stickers
    assert (made.sticker, made.format, made.emoji_list) == ("uploaded", "video", ["😂", "🐱"])


async def test_adds_and_replaces_for_the_owner(api: FakeBotApi, sets: BotStickerSets) -> None:
    api.answers["AddStickerToSet"] = ok(True)
    api.answers["ReplaceStickerInSet"] = ok(True)

    await sets.add(ME, CATS, SMILE)
    await sets.replace(ME, CATS, "old", SMILE)

    added, replaced = api.methods
    assert isinstance(added, AddStickerToSet)
    assert (added.user_id, added.name, added.sticker.sticker) == (ME, CATS, "uploaded")
    assert isinstance(replaced, ReplaceStickerInSet)
    assert (replaced.old_sticker, replaced.sticker.emoji_list) == ("old", ["😂", "🐱"])


@pytest.mark.parametrize(
    ("answer", "refusal"),
    [
        (forbidden("bot was blocked by the user"), BotBlockedError),
        (bad_request("PEER_ID_INVALID"), BotBlockedError),
        (bad_request("user not found"), BotBlockedError),
        (forbidden("bot can't initiate conversation with a user"), BotBlockedError),
        (bad_request("sticker set name is already occupied"), PackNameTakenError),
        (bad_request("STICKERS_TOO_MUCH"), PackFullError),
        (bad_request("STICKERSET_INVALID"), PackNotFoundError),
        (bad_request("STICKER_VIDEO_LONG"), TelegramUnavailableError),
    ],
)
async def test_telegrams_refusals_become_contract_errors(
    api: FakeBotApi,
    sets: BotStickerSets,
    answer: tuple[int, dict[str, Any]],
    refusal: type[ApplicationError],
) -> None:
    api.answers["AddStickerToSet"] = answer

    with pytest.raises(refusal):
        await sets.add(ME, CATS, SMILE)


async def test_flood_control_on_a_change_is_rate_limited(
    api: FakeBotApi, sets: BotStickerSets
) -> None:
    api.answers["UploadStickerFile"] = (
        429,
        {
            "ok": False,
            "error_code": 429,
            "description": "Too Many Requests: retry after 7",
            "parameters": {"retry_after": 7},
        },
    )

    with pytest.raises(RateLimitedError) as raised:
        await sets.upload(ME, b"webm")

    assert raised.value.retry_after_sec == 7
