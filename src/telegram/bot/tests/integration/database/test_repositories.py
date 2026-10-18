from datetime import UTC, datetime, timedelta, timezone

import pytest
from sqlalchemy.exc import IntegrityError

from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.links import LINK_LIFETIME, LinkRequest
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import TelegramProfile, User

NOW = datetime(2026, 10, 18, 9, 0, 0, 123456, tzinfo=UTC)
HOUR = timedelta(hours=1)
ANNA = User.first_seen(42, TelegramProfile("Anna", "K.", "anna", "ru"), NOW)
BORIS = User.first_seen(7, TelegramProfile("Boris"), NOW)


def device(device_id: str, user: User, linked_at: datetime) -> Device:
    return Device(
        device_id, user.id, f"PC-{device_id}", DeviceOs.WINDOWS, f"hash-{device_id}", linked_at
    )


def pack(name: str, owner: User, updated_at: datetime) -> Pack:
    return Pack.create(
        f"{name}_by_StickerSnapAppBot", owner.id, name, PackKind.STICKERS, NOW
    ).touched(updated_at)


async def save_users(unit_of_work: UnitOfWorkFactory, *users: User) -> None:
    async with unit_of_work() as uow:
        for user in users:
            await uow.users.save(user)
        await uow.commit()


async def test_user_round_trips_with_aware_utc_times(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)

    async with unit_of_work() as uow:
        assert await uow.users.get(42) == ANNA
        assert await uow.users.get(1) is None


async def test_saving_a_user_again_updates_it(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    changed = ANNA.with_profile(TelegramProfile("Anna"), NOW + HOUR).with_bot_blocked(
        True, NOW + HOUR
    )

    await save_users(unit_of_work, changed)

    async with unit_of_work() as uow:
        assert await uow.users.get(42) == changed


async def test_times_in_another_zone_come_back_as_utc(unit_of_work: UnitOfWorkFactory) -> None:
    moscow = datetime(2026, 10, 18, 12, 0, tzinfo=timezone(timedelta(hours=3)))
    await save_users(unit_of_work, User.first_seen(1, TelegramProfile("M"), moscow))

    async with unit_of_work() as uow:
        user = await uow.users.get(1)

    assert user is not None
    assert user.created_at == datetime(2026, 10, 18, 9, 0, tzinfo=UTC)
    assert user.created_at.tzinfo is UTC


async def test_leaving_without_commit_rolls_back(unit_of_work: UnitOfWorkFactory) -> None:
    async with unit_of_work() as uow:
        await uow.users.save(ANNA)

    async with unit_of_work() as uow:
        assert await uow.users.get(42) is None


async def test_an_error_inside_rolls_back(unit_of_work: UnitOfWorkFactory) -> None:
    async def save_then_fail() -> None:
        async with unit_of_work() as uow:
            await uow.users.save(ANNA)
            raise RuntimeError

    with pytest.raises(RuntimeError):
        await save_then_fail()

    async with unit_of_work() as uow:
        assert await uow.users.get(42) is None


async def test_devices_are_listed_newest_first_and_found_by_key(
    unit_of_work: UnitOfWorkFactory,
) -> None:
    await save_users(unit_of_work, ANNA, BORIS)
    async with unit_of_work() as uow:
        await uow.devices.save(device("old", ANNA, NOW))
        await uow.devices.save(device("new", ANNA, NOW + HOUR))
        await uow.devices.save(device("boris", BORIS, NOW))
        await uow.commit()

    async with unit_of_work() as uow:
        assert [d.id for d in await uow.devices.list_for_user(42)] == ["new", "old"]
        assert await uow.devices.find_by_key_hash("hash-boris") == device("boris", BORIS, NOW)
        assert await uow.devices.find_by_key_hash("nobody") is None
        assert await uow.devices.get("old") == device("old", ANNA, NOW)


async def test_unlinking_deletes_the_device(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    async with unit_of_work() as uow:
        await uow.devices.save(device("pc", ANNA, NOW))
        await uow.commit()

    async with unit_of_work() as uow:
        await uow.devices.delete("pc")
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.devices.get("pc") is None
        assert await uow.devices.find_by_key_hash("hash-pc") is None


async def test_a_device_needs_its_user(unit_of_work: UnitOfWorkFactory) -> None:
    # foreign keys are on for every connection
    async with unit_of_work() as uow:
        with pytest.raises(IntegrityError):
            await uow.devices.save(device("pc", ANNA, NOW))


async def test_two_devices_cannot_share_a_key(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    twin = Device("twin", ANNA.id, "PC", DeviceOs.MACOS, "hash-pc", NOW)

    async with unit_of_work() as uow:
        await uow.devices.save(device("pc", ANNA, NOW))
        with pytest.raises(IntegrityError):
            await uow.devices.save(twin)


async def test_a_link_goes_through_its_states(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    link = LinkRequest.open("code", "poll-hash", "DESKTOP-ABC", DeviceOs.WINDOWS, NOW)
    async with unit_of_work() as uow:
        await uow.links.save(link)
        await uow.commit()

    claimed = link.confirm(42, NOW).claim("pc", NOW)
    async with unit_of_work() as uow:
        await uow.devices.save(device("pc", ANNA, NOW))
        await uow.links.save(claimed)
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.links.get("code") == claimed
        assert await uow.links.get("other") is None


async def test_expired_links_are_forgotten(unit_of_work: UnitOfWorkFactory) -> None:
    async with unit_of_work() as uow:
        await uow.links.save(LinkRequest.open("old", "h1", "PC", DeviceOs.LINUX, NOW))
        await uow.links.save(LinkRequest.open("fresh", "h2", "PC", DeviceOs.LINUX, NOW + HOUR))
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.links.delete_expired(NOW + LINK_LIFETIME) == 1
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.links.get("old") is None
        assert await uow.links.get("fresh") is not None


async def test_packs_are_listed_freshest_first_per_owner(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA, BORIS)
    async with unit_of_work() as uow:
        await uow.packs.save(pack("cats", ANNA, NOW))
        await uow.packs.save(pack("dogs", ANNA, NOW + HOUR))
        await uow.packs.save(pack("birds", BORIS, NOW))
        await uow.commit()

    async with unit_of_work() as uow:
        names = [p.name for p in await uow.packs.list_for_owner(42)]

    assert names == ["dogs_by_StickerSnapAppBot", "cats_by_StickerSnapAppBot"]


async def test_a_pack_is_found_by_name_without_case(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    cats = pack("cats", ANNA, NOW)
    async with unit_of_work() as uow:
        await uow.packs.save(cats)
        await uow.commit()

    async with unit_of_work() as uow:
        # Telegram compares set names without case, so the service does too
        assert await uow.packs.get("CATS_by_stickersnapappbot") == cats


async def test_a_pack_is_retitled_and_deleted(unit_of_work: UnitOfWorkFactory) -> None:
    await save_users(unit_of_work, ANNA)
    cats = pack("cats", ANNA, NOW)
    async with unit_of_work() as uow:
        await uow.packs.save(cats)
        await uow.packs.save(cats.retitled("Кошки", NOW + HOUR))
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.packs.get(cats.name) == cats.retitled("Кошки", NOW + HOUR)
        await uow.packs.delete(cats.name)
        await uow.commit()

    async with unit_of_work() as uow:
        assert await uow.packs.get(cats.name) is None
