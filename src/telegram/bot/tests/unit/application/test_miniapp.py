import json
from dataclasses import replace
from datetime import timedelta

import pytest

from stickersnap_bot.application.errors import (
    DeviceNotFoundError,
    PackInTelegramError,
    PackNotFoundError,
    RateLimitedError,
    StickerNotFoundError,
    UnauthorizedError,
)
from stickersnap_bot.application.init_data import InitDataVerifier, sign_init_data
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.miniapp import MiniApp, PackState
from stickersnap_bot.application.sticker_sets import StickerFile
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import NOW, FakeStickerSets, Tables, fake_unit_of_work, sticker_set

TOKEN = "123:secret"
ME, SOMEONE = 42, 7
CATS = "cats_by_StickerSnapAppBot"
DOGS = "dogs_by_StickerSnapAppBot"
media = MediaLinks(TOKEN)


@pytest.fixture
def tables() -> Tables:
    return Tables()


@pytest.fixture
def sets() -> FakeStickerSets:
    return FakeStickerSets()


@pytest.fixture
def app(tables: Tables, sets: FakeStickerSets) -> MiniApp:
    verifier = InitDataVerifier(TOKEN, max_age=timedelta(days=1))
    return MiniApp(fake_unit_of_work(tables), sets, verifier, media, clock=lambda: NOW)


def pack(name: str, owner: int = ME, hours_ago: int = 0) -> Pack:
    return Pack.create(
        name, owner, "Old title", PackKind.STICKERS, NOW - timedelta(hours=hours_ago)
    )


def device(device_id: str, owner: int = ME, hours_ago: int = 0) -> Device:
    return Device(
        id=device_id,
        user_id=owner,
        name=f"PC-{device_id}",
        os=DeviceOs.WINDOWS,
        key_hash="h" + device_id,
        linked_at=NOW - timedelta(hours=hours_ago),
    )


def launch(first_name: str = "Дима") -> str:
    user = json.dumps({"id": ME, "first_name": first_name})
    return sign_init_data(TOKEN, {"user": user, "auth_date": str(int(NOW.timestamp()))})


async def test_sign_in_remembers_a_new_user(app: MiniApp, tables: Tables) -> None:
    assert await app.sign_in(launch()) == ME

    assert tables.users[ME].profile == TelegramProfile(first_name="Дима")


async def test_sign_in_updates_a_changed_profile(app: MiniApp, tables: Tables) -> None:
    earlier = NOW - timedelta(days=3)
    tables.users[ME] = User.first_seen(ME, TelegramProfile(first_name="Old"), earlier)

    await app.sign_in(launch("New"))

    assert tables.users[ME].profile.first_name == "New"
    assert tables.users[ME].created_at == earlier


async def test_sign_in_refuses_unsigned_data(app: MiniApp, tables: Tables) -> None:
    with pytest.raises(UnauthorizedError):
        await app.sign_in("user=%7B%22id%22%3A42%7D&hash=forged")

    assert tables.users == {}


async def test_lists_only_my_packs_freshest_first(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {
        CATS: pack(CATS, hours_ago=5),
        DOGS: pack(DOGS, hours_ago=1),
        "x_by_StickerSnapAppBot": pack("x_by_StickerSnapAppBot", owner=SOMEONE),
    }
    sets.sets = {CATS: sticker_set(CATS, 3), DOGS: sticker_set(DOGS, 120)}

    packs = await app.list_packs(ME)

    assert [p.name for p in packs] == [DOGS, CATS]
    assert packs[0].count == packs[0].limit == 120
    assert packs[1].title == f"Title of {CATS}"
    assert packs[1].stickers == ()


async def test_cover_is_the_first_stickers_thumbnail_or_the_sets_own(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS), DOGS: pack(DOGS)}
    sets.sets = {
        CATS: sticker_set(CATS, 2),
        DOGS: replace(sticker_set(DOGS, 2), thumb_file_id="dogcover"),
    }

    covers = {p.name: p.cover for p in await app.list_packs(ME)}

    assert covers[CATS] == media.token("thumb1", NOW)
    assert covers[DOGS] == media.token("dogcover", NOW)


async def test_a_pack_telegram_lost_is_missing(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS)}

    (missing,) = await app.list_packs(ME)

    assert missing.state is PackState.MISSING
    assert missing.title == "Old title"
    assert missing.count == 0
    assert missing.cover is None


async def test_gets_a_pack_with_its_stickers_in_telegrams_order(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS)}
    sets.sets = {CATS: sticker_set(CATS, 3)}

    found = await app.get_pack(ME, CATS)

    assert found.state is PackState.OK
    assert [s.id for s in found.stickers] == ["sticker1", "sticker2", "sticker3"]
    assert found.stickers[0].thumb == media.token("thumb1", NOW)
    assert found.stickers[0].video == media.token("video1", NOW)
    assert found.stickers[0].emoji == ("😴",)


@pytest.mark.parametrize("owner", [SOMEONE, None])
async def test_someone_elses_or_an_unknown_pack_is_not_found(
    app: MiniApp, tables: Tables, sets: FakeStickerSets, owner: int | None
) -> None:
    if owner is not None:
        tables.packs = {CATS: pack(CATS, owner=owner)}
    sets.sets = {CATS: sticker_set(CATS, 1)}

    with pytest.raises(PackNotFoundError):
        await app.get_pack(ME, CATS)
    with pytest.raises(PackNotFoundError):
        await app.delete_sticker(ME, CATS, "sticker1")
    with pytest.raises(PackNotFoundError):
        await app.forget_pack(ME, CATS)
    assert sets.deleted == []


async def test_telegrams_refusal_reaches_the_caller(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS)}
    sets.failure = RateLimitedError(24)

    with pytest.raises(RateLimitedError) as raised:
        await app.list_packs(ME)

    assert raised.value.retry_after_sec == 24


async def test_deletes_a_sticker_and_touches_the_pack(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS, hours_ago=5)}
    sets.sets = {CATS: sticker_set(CATS, 2)}

    await app.delete_sticker(ME, CATS, "sticker2")

    assert sets.deleted == ["sticker2"]
    assert tables.packs[CATS].updated_at == NOW


async def test_the_last_sticker_takes_the_pack_with_it(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS)}
    sets.sets = {CATS: sticker_set(CATS, 1)}

    await app.delete_sticker(ME, CATS, "sticker1")

    assert tables.packs == {}


async def test_a_sticker_of_another_set_is_not_found(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS)}
    sets.sets = {CATS: sticker_set(CATS, 1)}

    with pytest.raises(StickerNotFoundError):
        await app.delete_sticker(ME, CATS, "sticker9")
    assert sets.deleted == []


async def test_forgets_only_a_missing_pack(
    app: MiniApp, tables: Tables, sets: FakeStickerSets
) -> None:
    tables.packs = {CATS: pack(CATS), DOGS: pack(DOGS)}
    sets.sets = {DOGS: sticker_set(DOGS, 1)}

    await app.forget_pack(ME, CATS)
    with pytest.raises(PackInTelegramError):
        await app.forget_pack(ME, DOGS)

    assert list(tables.packs) == [DOGS]


async def test_lists_my_devices_newest_first(app: MiniApp, tables: Tables) -> None:
    tables.devices = {
        "a": device("a", hours_ago=10),
        "b": device("b", hours_ago=1),
        "c": device("c", owner=SOMEONE),
    }

    assert [d.id for d in await app.list_devices(ME)] == ["b", "a"]


async def test_unlinks_only_my_device(app: MiniApp, tables: Tables) -> None:
    tables.devices = {"a": device("a"), "c": device("c", owner=SOMEONE)}

    await app.unlink_device(ME, "a")
    with pytest.raises(DeviceNotFoundError):
        await app.unlink_device(ME, "c")
    with pytest.raises(DeviceNotFoundError):
        await app.unlink_device(ME, "nope")

    assert list(tables.devices) == ["c"]


async def test_opens_a_signed_media_link(app: MiniApp, sets: FakeStickerSets) -> None:
    sets.files = {"thumb1": StickerFile(b"webp", "image/webp")}

    opened = await app.open_media(media.token("thumb1", NOW))

    assert opened is not None
    assert opened[0] == StickerFile(b"webp", "image/webp")
    assert await app.open_media(media.token("lost", NOW)) is None
    assert await app.open_media("thumb1.1.forged") is None
