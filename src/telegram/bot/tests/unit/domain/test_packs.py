from datetime import UTC, datetime, timedelta

import pytest

from stickersnap_bot.domain.errors import InvalidValueError
from stickersnap_bot.domain.packs import Pack, PackKind, pack_name, pack_title

BOT = "StickerSnapAppBot"
NOW = datetime(2026, 10, 18, 9, 0, tzinfo=UTC)


def test_limits_are_telegrams() -> None:
    assert PackKind.STICKERS.limit == 120
    assert PackKind.EMOJI.limit == 200


@pytest.mark.parametrize(
    "name",
    ["reactions_call_by_StickerSnapAppBot", "a_by_stickersnapappbot", "Cats2_by_StickerSnapAppBot"],
)
def test_accepts_a_set_name_of_the_bot(name: str) -> None:
    assert pack_name(name, BOT) == name


@pytest.mark.parametrize(
    "name",
    [
        "cats_by_OtherBot",  # another bot's set
        "_by_StickerSnapAppBot",  # nothing before the suffix
        "by_StickerSnapAppBot",
        "1cats_by_StickerSnapAppBot",  # must start with a letter
        "cats__x_by_StickerSnapAppBot",  # double underscore
        "cats-x_by_StickerSnapAppBot",
        "c" * 44 + "_by_StickerSnapAppBot",  # 65 characters
    ],
)
def test_refuses_a_foreign_or_broken_set_name(name: str) -> None:
    with pytest.raises(InvalidValueError):
        pack_name(name, BOT)


def test_name_of_64_characters_fits() -> None:
    name = "c" * 43 + "_by_StickerSnapAppBot"

    assert pack_name(name, BOT) == name


def test_title_is_trimmed_and_limited() -> None:
    assert pack_title("  Реакции  ") == "Реакции"
    with pytest.raises(InvalidValueError):
        pack_title(" ")
    with pytest.raises(InvalidValueError):
        pack_title("x" * 65)


def test_retitle_and_touch_move_updated_at() -> None:
    pack = Pack.create("a_by_StickerSnapAppBot", 42, "Коты", PackKind.STICKERS, NOW)
    later = NOW + timedelta(hours=1)

    assert pack.created_at == pack.updated_at == NOW
    assert pack.retitled("Кошки", later).title == "Кошки"
    assert pack.retitled("Кошки", later).updated_at == later
    assert pack.touched(later).updated_at == later
    assert pack.touched(later).created_at == NOW
