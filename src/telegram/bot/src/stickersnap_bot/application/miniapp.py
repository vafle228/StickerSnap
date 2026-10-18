"""The Mini App's use cases: who is calling, their packs and stickers, their computers.

A pack is the user's when the service's row says so; any other pack, someone else's included,
is «not found». What a set holds comes from Telegram on every call.
"""

import asyncio
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum

from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.errors import (
    DeviceNotFoundError,
    PackInTelegramError,
    PackNotFoundError,
    StickerNotFoundError,
)
from stickersnap_bot.application.init_data import InitDataVerifier
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.persistence import UnitOfWork, UnitOfWorkFactory
from stickersnap_bot.application.sticker_sets import StickerFile, StickerSet, StickerSets
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import User


class PackState(StrEnum):
    OK = "ok"
    # Telegram does not know the set any more (deleted through @Stickers)
    MISSING = "missing"


@dataclass(frozen=True)
class StickerView:
    id: str
    emoji: tuple[str, ...]
    # media tokens (MediaLinks); the API turns them into links
    thumb: str
    video: str


@dataclass(frozen=True)
class PackView:
    name: str
    title: str
    kind: PackKind
    count: int
    limit: int
    state: PackState
    updated_at: datetime
    cover: str | None = None
    stickers: tuple[StickerView, ...] = ()


class MiniApp:
    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        sticker_sets: StickerSets,
        verifier: InitDataVerifier,
        media: MediaLinks,
        clock: Clock,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._sets = sticker_sets
        self._verifier = verifier
        self._media = media
        self._clock = clock

    async def sign_in(self, init_data: str) -> int:
        """The Telegram user the launch data was signed for; remembers them on the first visit."""
        now = self._clock()
        launch = self._verifier.verify(init_data, now)
        async with self._unit_of_work() as uow:
            known = await uow.users.get(launch.id)
            user = (
                User.first_seen(launch.id, launch.profile, now)
                if known is None
                else known.with_profile(launch.profile, now)
            )
            if user is not known:
                await uow.users.save(user)
                await uow.commit()
        return launch.id

    async def list_packs(self, user_id: int) -> Sequence[PackView]:
        async with self._unit_of_work() as uow:
            packs = await uow.packs.list_for_owner(user_id)
        sets = await asyncio.gather(*(self._sets.get(pack.name) for pack in packs))
        now = self._clock()
        return [
            self._view(pack, found, now, with_stickers=False)
            for pack, found in zip(packs, sets, strict=True)
        ]

    async def get_pack(self, user_id: int, name: str) -> PackView:
        async with self._unit_of_work() as uow:
            pack = await _owned_pack(uow, user_id, name)
        found = await self._sets.get(pack.name)
        return self._view(pack, found, self._clock(), with_stickers=True)

    async def forget_pack(self, user_id: int, name: str) -> None:
        async with self._unit_of_work() as uow:
            pack = await _owned_pack(uow, user_id, name)
            if await self._sets.get(pack.name) is not None:
                raise PackInTelegramError(pack.name)
            await uow.packs.delete(pack.name)
            await uow.commit()

    async def delete_sticker(self, user_id: int, name: str, sticker_id: str) -> None:
        async with self._unit_of_work() as uow:
            pack = await _owned_pack(uow, user_id, name)
        found = await self._sets.get(pack.name)
        if found is None:
            raise PackNotFoundError(pack.name)
        if all(sticker.id != sticker_id for sticker in found.stickers):
            raise StickerNotFoundError(sticker_id)
        await self._sets.delete_sticker(sticker_id)
        async with self._unit_of_work() as uow:
            # The last sticker takes the set with it: Telegram drops an empty set
            if len(found.stickers) == 1:
                await uow.packs.delete(pack.name)
            else:
                await uow.packs.save(pack.touched(self._clock()))
            await uow.commit()

    async def list_devices(self, user_id: int) -> Sequence[Device]:
        async with self._unit_of_work() as uow:
            return await uow.devices.list_for_user(user_id)

    async def unlink_device(self, user_id: int, device_id: str) -> None:
        async with self._unit_of_work() as uow:
            device = await uow.devices.get(device_id)
            if device is None or device.user_id != user_id:
                raise DeviceNotFoundError(device_id)
            # the key goes with the device: the computer gets 401 on its next call
            await uow.devices.delete(device.id)
            await uow.commit()

    async def open_media(self, token: str) -> tuple[StickerFile, int] | None:
        """A signed link's file and the seconds the link has left; None for a bad link or a lost
        file."""
        now = self._clock()
        opened = self._media.open(token, now)
        if opened is None:
            return None
        file_id, expires_at = opened
        found = await self._sets.download(file_id)
        return None if found is None else (found, int((expires_at - now).total_seconds()))

    def _view(
        self, pack: Pack, found: StickerSet | None, now: datetime, *, with_stickers: bool
    ) -> PackView:
        if found is None:
            return PackView(
                name=pack.name,
                title=pack.title,
                kind=pack.kind,
                count=0,
                limit=pack.kind.limit,
                state=PackState.MISSING,
                updated_at=pack.updated_at,
            )
        stickers = tuple(
            StickerView(
                id=sticker.id,
                emoji=sticker.emoji,
                thumb=self._media.token(sticker.thumb_file_id or sticker.video_file_id, now),
                video=self._media.token(sticker.video_file_id, now),
            )
            for sticker in found.stickers
        )
        first = found.stickers[0].thumb_file_id if found.stickers else None
        cover = found.thumb_file_id or first
        return PackView(
            name=pack.name,
            # Telegram's title is the truth: the set may have been renamed through @Stickers
            title=found.title,
            kind=found.kind,
            count=len(found.stickers),
            limit=found.kind.limit,
            state=PackState.OK,
            updated_at=pack.updated_at,
            cover=None if cover is None else self._media.token(cover, now),
            stickers=stickers if with_stickers else (),
        )


async def _owned_pack(uow: UnitOfWork, user_id: int, name: str) -> Pack:
    pack = await uow.packs.get(name)
    if pack is None or pack.owner_id != user_id:
        raise PackNotFoundError(name)
    return pack
