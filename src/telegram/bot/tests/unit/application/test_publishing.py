from collections.abc import Awaitable, Callable
from dataclasses import replace
from datetime import timedelta

import pytest

from stickersnap_bot.application.errors import (
    BadRequestError,
    BotBlockedError,
    PackFullError,
    PackNameInvalidError,
    PackNameTakenError,
    PackNotFoundError,
    RateLimitedError,
    StickerNotFoundError,
    UnauthorizedError,
    ValidationFailedError,
)
from stickersnap_bot.application.publishing import MAX_STICKER_BYTES, Publishing
from stickersnap_bot.application.sticker_sets import (
    NewSticker,
    StickerFile,
    StickerFormat,
    StickerSet,
)
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import (
    NOW,
    FakeChatMessages,
    FakeClock,
    FakeStickerSets,
    Tables,
    publishing,
    sticker,
    sticker_set,
)

ME, SOMEONE = 42, 7
KEY = "device-key"
CATS = "cats_by_StickerSnapAppBot"
WEBM = b"\x1a\x45\xdf\xa3" + b"\x00" * 100
SMILE = NewSticker("file1", ("😂",))


@pytest.fixture
def tables() -> Tables:
    tables = Tables()
    for user_id in (ME, SOMEONE):
        tables.users[user_id] = User.first_seen(user_id, TelegramProfile("Дима"), NOW)
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
def clock() -> FakeClock:
    return FakeClock()


@pytest.fixture
def slept() -> list[float]:
    return []


@pytest.fixture
def service(
    tables: Tables,
    sets: FakeStickerSets,
    messages: FakeChatMessages,
    clock: FakeClock,
    slept: list[float],
) -> Publishing:
    return publishing(tables, sets, messages, clock, slept)


def own(tables: Tables, sets: FakeStickerSets, count: int = 2, owner: int = ME) -> Pack:
    pack = Pack.create(CATS, owner, "Коты", PackKind.STICKERS, NOW)
    tables.packs[CATS] = pack
    sets.sets[CATS] = sticker_set(CATS, count)
    return pack


def stop_the_bot(tables: Tables) -> None:
    tables.users[ME] = tables.users[ME].with_bot_blocked(True, NOW)


# Upload


async def test_uploads_the_webm_for_the_keys_owner(
    service: Publishing, sets: FakeStickerSets
) -> None:
    file_id = await service.upload(KEY, WEBM)

    assert file_id == "file1"
    assert sets.uploaded == [(ME, WEBM)]


@pytest.mark.parametrize(
    "webm",
    [
        b"\x1a\x45\xdf\xa3" + b"\x00" * MAX_STICKER_BYTES,
        b"GIF89a" + b"\x00" * 100,
    ],
    ids=["too large", "not webm"],
)
async def test_refuses_a_file_telegram_would_refuse(
    service: Publishing, sets: FakeStickerSets, webm: bytes
) -> None:
    with pytest.raises(ValidationFailedError):
        await service.upload(KEY, webm)

    assert sets.uploaded == []


async def test_an_empty_body_is_a_bad_request(service: Publishing) -> None:
    with pytest.raises(BadRequestError):
        await service.upload(KEY, b"")


async def test_an_unknown_key_publishes_nothing(service: Publishing, sets: FakeStickerSets) -> None:
    with pytest.raises(UnauthorizedError):
        await service.upload("revoked", WEBM)

    assert sets.uploaded == []


async def test_a_stopped_bot_is_refused_before_telegram(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    stop_the_bot(tables)
    own(tables, sets)

    with pytest.raises(BotBlockedError):
        await service.upload(KEY, WEBM)
    with pytest.raises(BotBlockedError):
        await service.create(KEY, "dogs_by_StickerSnapAppBot", "Псы", PackKind.STICKERS, [SMILE])
    with pytest.raises(BotBlockedError):
        await service.add(KEY, CATS, SMILE)
    with pytest.raises(BotBlockedError):
        await service.replace(KEY, CATS, "sticker1", SMILE)

    assert (sets.uploaded, sets.calls) == ([], [])


# Create


async def test_creates_a_pack_of_the_keys_owner(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    made = await service.create(KEY, CATS, " Коты ", PackKind.EMOJI, [SMILE])

    assert sets.calls == [("create", ME, CATS, (SMILE,))]
    assert (made.name, made.title, made.kind) == (CATS, "Коты", PackKind.EMOJI)
    assert [(s.id, s.unique_id, s.emoji) for s in made.stickers] == [
        ("sticker-file1", "unique-file1", ("😂",))
    ]
    assert tables.packs[CATS] == Pack(CATS, ME, "Коты", PackKind.EMOJI, NOW, NOW)
    [notice] = messages.sent
    assert (notice.chat_id, notice.message.title) == (ME, "Пак «Коты» создан")
    assert notice.message.text.startswith("В нём первый стикер.")


@pytest.mark.parametrize(
    "name",
    ["cats_by_OtherBot", "cats", "1cats_by_StickerSnapAppBot", "_by_StickerSnapAppBot"],
)
async def test_a_name_the_bot_may_not_own_is_invalid(
    service: Publishing, sets: FakeStickerSets, name: str
) -> None:
    with pytest.raises(PackNameInvalidError):
        await service.create(KEY, name, "Коты", PackKind.STICKERS, [SMILE])

    assert sets.calls == []


async def test_a_name_in_the_registry_is_taken_whoever_has_it(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets, owner=SOMEONE)
    del sets.sets[CATS]

    with pytest.raises(PackNameTakenError):
        await service.create(KEY, CATS.upper(), "Коты", PackKind.STICKERS, [SMILE])

    assert sets.calls == []


async def test_a_name_telegram_has_is_taken_and_nothing_is_kept(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    sets.sets[CATS] = sticker_set(CATS, 1)

    with pytest.raises(PackNameTakenError):
        await service.create(KEY, CATS, "Коты", PackKind.STICKERS, [SMILE])

    assert CATS not in tables.packs
    assert messages.sent == []


@pytest.mark.parametrize("count", [0, 51])
async def test_a_pack_is_made_with_one_to_fifty_stickers(
    service: Publishing, sets: FakeStickerSets, count: int
) -> None:
    with pytest.raises(BadRequestError):
        await service.create(KEY, CATS, "Коты", PackKind.STICKERS, [SMILE] * count)

    assert sets.calls == []


async def test_a_notification_that_fails_keeps_the_pack(
    service: Publishing, tables: Tables, messages: FakeChatMessages
) -> None:
    messages.refused.add(ME)

    made = await service.create(KEY, CATS, "Коты", PackKind.STICKERS, [SMILE])

    assert len(made.stickers) == 1
    assert CATS in tables.packs


# Read


async def test_reads_an_own_pack_as_telegram_has_it(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets, count=2)

    found = await service.get(KEY, CATS)

    assert [s.unique_id for s in found.stickers] == ["unique1", "unique2"]


async def test_someone_elses_pack_is_not_found(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets, owner=SOMEONE)

    with pytest.raises(PackNotFoundError):
        await service.get(KEY, CATS)
    with pytest.raises(PackNotFoundError):
        await service.add(KEY, CATS, SMILE)
    with pytest.raises(PackNotFoundError):
        await service.delete(KEY, CATS, "sticker1")

    assert (sets.calls, sets.deleted) == ([], [])


async def test_a_bot_pack_outside_the_registry_is_not_found(
    service: Publishing, sets: FakeStickerSets
) -> None:
    # made by a desktop before the service: Telegram does not say whose it is
    sets.sets[CATS] = sticker_set(CATS, 2)

    with pytest.raises(PackNotFoundError):
        await service.get(KEY, CATS)
    with pytest.raises(PackNotFoundError):
        await service.add(KEY, CATS, SMILE)


async def test_a_pack_telegram_lost_is_not_found(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets)
    del sets.sets[CATS]

    with pytest.raises(PackNotFoundError):
        await service.get(KEY, CATS)
    with pytest.raises(PackNotFoundError):
        await service.add(KEY, CATS, SMILE)


# Add


async def test_adds_at_the_end_and_notifies(
    service: Publishing,
    tables: Tables,
    sets: FakeStickerSets,
    messages: FakeChatMessages,
    clock: FakeClock,
) -> None:
    own(tables, sets, count=2)
    clock.advance(timedelta(minutes=1))

    after = await service.add(KEY, CATS, SMILE)

    assert sets.calls == [("add", ME, CATS, SMILE)]
    assert [s.id for s in after.stickers] == ["sticker1", "sticker2", "sticker-file1"]
    assert tables.packs[CATS].updated_at == clock.now
    assert messages.texts(ME) == ["В пак «Коты» добавлен 1 стикер. Теперь в нём 3 из 120."]


async def test_additions_one_after_another_edit_one_notice(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    own(tables, sets, count=1)

    await service.add(KEY, CATS, SMILE)
    await service.add(KEY, CATS, replace(SMILE, file_id="file2"))

    assert messages.texts(ME) == ["В пак «Коты» добавлено 2 стикера. Теперь в нём 3 из 120."]


async def test_a_full_pack_takes_no_more(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    own(tables, sets, count=120)

    with pytest.raises(PackFullError):
        await service.add(KEY, CATS, SMILE)

    assert messages.sent == []


# Replace


async def test_replaces_in_place_without_a_notice(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    own(tables, sets, count=3)

    after = await service.replace(KEY, CATS, "sticker2", SMILE)

    assert [s.id for s in after.stickers] == ["sticker1", "sticker-file1", "sticker3"]
    assert messages.sent == []


async def test_replacing_a_sticker_not_in_the_pack_is_not_found(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets, count=2)

    with pytest.raises(StickerNotFoundError):
        await service.replace(KEY, CATS, "sticker9", SMILE)

    assert sets.calls == []


# Delete and download


async def test_deletes_with_the_bot_stopped_too(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    stop_the_bot(tables)
    own(tables, sets, count=2)

    await service.delete(KEY, CATS, "sticker1")

    assert sets.deleted == ["sticker1"]
    assert CATS in tables.packs


async def test_the_last_sticker_takes_the_pack_with_it(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    own(tables, sets, count=1)

    await service.delete(KEY, CATS, "sticker1")

    assert CATS not in tables.packs


async def test_downloads_a_stickers_file(service: Publishing, sets: FakeStickerSets) -> None:
    sets.files["sticker1"] = StickerFile(b"webm", "video/webm")

    assert await service.download(KEY, "sticker1") == StickerFile(b"webm", "video/webm")
    with pytest.raises(StickerNotFoundError):
        await service.download(KEY, "sticker9")
    with pytest.raises(UnauthorizedError):
        await service.download("revoked", "sticker1")


# Copy

OLD = "OldCats"
COPY = "old_cats_by_StickerSnapAppBot"


def old_pack(sets: FakeStickerSets, count: int, kind: PackKind = PackKind.STICKERS) -> StickerSet:
    """A set made by @Stickers: static, animated and video stickers by turns."""
    formats = list(StickerFormat)
    found = StickerSet(
        name=OLD,
        title="Старые коты",
        kind=kind,
        stickers=tuple(replace(sticker(n), format=formats[n % 3]) for n in range(1, count + 1)),
    )
    sets.sets[OLD] = found
    return found


async def test_copies_any_pack_into_a_new_pack_of_the_keys_owner(
    service: Publishing, tables: Tables, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    old = old_pack(sets, 3, PackKind.EMOJI)

    made = await service.copy(KEY, OLD, COPY, " Коты навсегда ")

    [(method, owner, name, stickers)] = sets.calls
    assert (method, owner, name) == ("create", ME, COPY)
    assert stickers == tuple(NewSticker(s.id, s.emoji, s.format) for s in old.stickers)
    assert (made.name, made.title, made.kind) == (COPY, "Коты навсегда", PackKind.EMOJI)
    assert [s.format for s in made.stickers] == [s.format for s in old.stickers]
    assert tables.packs[COPY] == Pack(COPY, ME, "Коты навсегда", PackKind.EMOJI, NOW, NOW)
    assert sets.sets[OLD] == old
    [notice] = messages.sent
    assert notice.message.title == "Пак «Коты навсегда» создан"


async def test_a_large_pack_is_made_with_fifty_and_filled_one_by_one(
    service: Publishing, sets: FakeStickerSets
) -> None:
    old = old_pack(sets, 120)

    made = await service.copy(KEY, OLD, COPY, "Коты")

    assert [call[0] for call in sets.calls] == ["create"] + ["add"] * 70
    assert sets.calls[0][3] == tuple(NewSticker(s.id, s.emoji, s.format) for s in old.stickers[:50])
    assert [s.id for s in made.stickers] == [f"sticker-{s.id}" for s in old.stickers]


async def test_a_sticker_without_emoji_gets_one(service: Publishing, sets: FakeStickerSets) -> None:
    sets.sets[OLD] = StickerSet(OLD, "Коты", PackKind.STICKERS, (replace(sticker(1), emoji=()),))

    made = await service.copy(KEY, OLD, COPY, "Коты")

    assert made.stickers[0].emoji == ("🙂",)


async def test_a_short_wait_is_waited_out(
    service: Publishing, sets: FakeStickerSets, slept: list[float]
) -> None:
    old_pack(sets, 52)
    sets.add = waiting_at(sets.add, 1, 3)  # type: ignore[method-assign,assignment]

    made = await service.copy(KEY, OLD, COPY, "Коты")

    assert slept == [3]
    assert len(made.stickers) == 52


async def test_a_long_wait_stops_the_copy_and_a_repeat_finishes_it(
    service: Publishing,
    tables: Tables,
    sets: FakeStickerSets,
    messages: FakeChatMessages,
    slept: list[float],
) -> None:
    old_pack(sets, 53)
    # the first add goes, the second meets a wait longer than the copy waits by itself
    sets.add = waiting_at(sets.add, 2, 120)  # type: ignore[method-assign,assignment]

    with pytest.raises(RateLimitedError):
        await service.copy(KEY, OLD, COPY, "Коты")
    assert len(sets.sets[COPY].stickers) == 51
    assert COPY in tables.packs
    assert slept == []

    del sets.add
    made = await service.copy(KEY, OLD, COPY, "Коты")

    assert [s.unique_id for s in made.stickers][-2:] == ["unique-sticker52", "unique-sticker53"]
    assert len(made.stickers) == 53
    assert messages.sent[-1].message.title != "Пак «Коты» создан"


def waiting_at(
    add: Callable[[int, str, NewSticker], Awaitable[None]], call: int, wait: int
) -> Callable[[int, str, NewSticker], Awaitable[None]]:
    """`add` whose `call`-th call Telegram answers with «wait `wait` s»."""
    made = 0

    async def waiting(owner_id: int, name: str, new: NewSticker) -> None:
        nonlocal made
        made += 1
        if made == call:
            raise RateLimitedError(wait)
        await add(owner_id, name, new)

    return waiting


async def test_a_repeat_of_a_finished_copy_changes_nothing(
    service: Publishing, sets: FakeStickerSets, messages: FakeChatMessages
) -> None:
    old_pack(sets, 2)
    await service.copy(KEY, OLD, COPY, "Коты")
    calls, sent = list(sets.calls), list(messages.sent)

    made = await service.copy(KEY, OLD, COPY, "Коты")

    assert len(made.stickers) == 2
    assert (sets.calls, messages.sent) == (calls, sent)


async def test_a_copy_never_writes_into_someone_elses_pack(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    old_pack(sets, 2)
    tables.packs[COPY] = Pack.create(COPY, SOMEONE, "Чужие", PackKind.STICKERS, NOW)
    sets.sets[COPY] = sticker_set(COPY, 1)

    with pytest.raises(PackNameTakenError):
        await service.copy(KEY, OLD, COPY, "Коты")

    assert sets.calls == []


async def test_an_unknown_source_is_not_found(service: Publishing, sets: FakeStickerSets) -> None:
    with pytest.raises(PackNotFoundError):
        await service.copy(KEY, OLD, COPY, "Коты")

    assert sets.calls == []


@pytest.mark.parametrize(
    ("name", "title", "error"),
    [
        ("old_cats_by_OtherBot", "Коты", PackNameInvalidError),
        (COPY, "   ", BadRequestError),
    ],
)
async def test_a_bad_name_or_title_copies_nothing(
    service: Publishing, sets: FakeStickerSets, name: str, title: str, error: type[Exception]
) -> None:
    old_pack(sets, 2)

    with pytest.raises(error):
        await service.copy(KEY, OLD, name, title)

    assert sets.calls == []


async def test_a_stopped_bot_copies_nothing(
    service: Publishing, tables: Tables, sets: FakeStickerSets
) -> None:
    stop_the_bot(tables)
    old_pack(sets, 2)

    with pytest.raises(BotBlockedError):
        await service.copy(KEY, OLD, COPY, "Коты")

    assert sets.calls == []
