"""Publishing from a linked desktop: bot plan «Публикация», contract «Облачный сервис · Публикация».

The desktop calls with its device key; the packs are made and filled on behalf of the account the
key is linked to, never anyone the request names. The steps follow the desktop's port
IStickerPublisher one to one: upload a .webm once, then create a pack with it, add it or put it in
an old sticker's place. Each change answers with the set after it, so the desktop finds the new
sticker without asking again.

Only packs in the service's registry are written to (Dmitriu chose this 2026-10-18): Telegram does
not say who owns a set, and deleteStickerFromSet does not check it at all.
"""

import asyncio
from collections.abc import Awaitable, Callable, Sequence
from functools import partial

from stickersnap_bot.application.chat import Notifications
from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.device_keys import device_for_key
from stickersnap_bot.application.errors import (
    BadRequestError,
    BotBlockedError,
    PackNameInvalidError,
    PackNameTakenError,
    PackNotFoundError,
    RateLimitedError,
    StickerNotFoundError,
    TelegramUnavailableError,
    UnauthorizedError,
    ValidationFailedError,
)
from stickersnap_bot.application.owned_packs import delete_sticker, owned_pack
from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.application.sticker_sets import (
    NewSticker,
    StickerFile,
    StickerSet,
    StickerSets,
)
from stickersnap_bot.domain.errors import InvalidValueError
from stickersnap_bot.domain.packs import Pack, PackKind, pack_name, pack_title
from stickersnap_bot.domain.users import User

# what Telegram takes for a video sticker, and the preset limit on the desktop
MAX_STICKER_BYTES = 256 * 1024
# what createNewStickerSet takes at once
MAX_STICKERS_AT_CREATION = 50
# every WebM file starts with an EBML header
_EBML_MAGIC = b"\x1a\x45\xdf\xa3"
# how long a copy waits out Telegram's «too many requests» by itself; a longer wait is the caller's
MAX_COPY_WAIT_SEC = 30
# what a copied sticker answers to when Telegram gave it no emoji
_NO_EMOJI = ("🙂",)

type Sleep = Callable[[float], Awaitable[None]]


class Publishing:
    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        sticker_sets: StickerSets,
        notifications: Notifications,
        clock: Clock,
        sleep: Sleep = asyncio.sleep,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._sets = sticker_sets
        self._notifications = notifications
        self._clock = clock
        self._sleep = sleep

    async def upload(self, device_key: str, webm: bytes) -> str:
        """The uploaded file's id; ValidationFailedError for a file Telegram would refuse."""
        owner = await self._owner(device_key)
        if not webm:
            raise BadRequestError("no sticker file in the body")
        if len(webm) > MAX_STICKER_BYTES:
            raise ValidationFailedError(f"the sticker is over {MAX_STICKER_BYTES} bytes")
        if not webm.startswith(_EBML_MAGIC):
            raise ValidationFailedError("the sticker is not a WebM file")
        _may_act_for(owner)
        return await self._sets.upload(owner.id, webm)

    async def create(
        self,
        device_key: str,
        name: str,
        title: str,
        kind: PackKind,
        stickers: Sequence[NewSticker],
    ) -> StickerSet:
        """A new pack of the key's owner; the bot writes «Пак создан»."""
        owner = await self._owner(device_key)
        if not 1 <= len(stickers) <= MAX_STICKERS_AT_CREATION:
            raise BadRequestError(f"a pack is made with 1..{MAX_STICKERS_AT_CREATION} stickers")
        try:
            name = pack_name(name, await self._sets.bot_username())
        except InvalidValueError as error:
            raise PackNameInvalidError(str(error)) from error
        try:
            pack = Pack.create(name, owner.id, title, kind, self._clock())
        except InvalidValueError as error:
            raise BadRequestError(str(error)) from error
        _may_act_for(owner)
        async with self._unit_of_work() as uow:
            if await uow.packs.get(name) is not None:
                raise PackNameTakenError(name)
        await self._sets.create(owner.id, pack.name, pack.title, pack.kind, stickers)
        async with self._unit_of_work() as uow:
            await uow.packs.save(pack)
            await uow.commit()
        made = await self._set_after_change(pack.name)
        await self._notifications.pack_created(pack, len(made.stickers))
        return made

    async def copy(self, device_key: str, source: str, name: str, title: str) -> StickerSet:
        """A new pack of the key's owner with every sticker of `source`, any set at all, in its
        order, kind, emoji and formats. Telegram lets only the bot that made a set change it, so a
        set from @Stickers or another bot comes under the service only as a copy. The stickers go
        by file id, nothing is downloaded. A repeat with the name of the owner's copy adds what
        the copy still misses, so a copy stopped halfway is finished by asking again."""
        owner = await self._owner(device_key)
        try:
            name = pack_name(name, await self._sets.bot_username())
        except InvalidValueError as error:
            raise PackNameInvalidError(str(error)) from error
        try:
            title = pack_title(title)
        except InvalidValueError as error:
            raise BadRequestError(str(error)) from error
        _may_act_for(owner)
        original = await self._sets.get(source)
        if original is None or not original.stickers:
            raise PackNotFoundError(source)
        wanted = [NewSticker(s.id, s.emoji or _NO_EMOJI, s.format) for s in original.stickers][
            : original.kind.limit
        ]
        async with self._unit_of_work() as uow:
            pack = await uow.packs.get(name)
        if pack is not None and pack.owner_id != owner.id:
            raise PackNameTakenError(name)

        made_now = pack is None
        if pack is None:
            pack = Pack.create(name, owner.id, title, original.kind, self._clock())
            first = wanted[:MAX_STICKERS_AT_CREATION]
            await self._patiently(
                partial(self._sets.create, owner.id, name, pack.title, pack.kind, first)
            )
            async with self._unit_of_work() as uow:
                await uow.packs.save(pack)
                await uow.commit()
            done = len(first)
        else:
            copied = await self._sets.get(name)
            if copied is None:
                raise PackNotFoundError(name)
            done = len(copied.stickers)

        for sticker in wanted[done:]:
            await self._patiently(partial(self._sets.add, owner.id, name, sticker))
        if not made_now and done < len(wanted):
            pack = await self._touch(pack)
        after = await self._set_after_change(name)
        if made_now:
            await self._notifications.pack_created(pack, len(after.stickers))
        elif done < len(wanted):
            await self._notifications.stickers_added(pack, len(wanted) - done, len(after.stickers))
        return after

    async def get(self, device_key: str, name: str) -> StickerSet:
        owner = await self._owner(device_key)
        async with self._unit_of_work() as uow:
            pack = await owned_pack(uow, owner.id, name)
        found = await self._sets.get(pack.name)
        if found is None:
            raise PackNotFoundError(pack.name)
        return found

    async def is_name_taken(self, device_key: str, name: str) -> bool:
        """Whether a new pack may not take `name`: the service has a pack of that name, or
        Telegram knows a set of it, whoever made it. getPublishedPack cannot tell: it answers
        404 for every set that is not the caller's."""
        await self._owner(device_key)
        try:
            name = pack_name(name, await self._sets.bot_username())
        except InvalidValueError as error:
            raise PackNameInvalidError(str(error)) from error
        async with self._unit_of_work() as uow:
            if await uow.packs.get(name) is not None:
                return True
        return await self._sets.get(name) is not None

    async def add(self, device_key: str, name: str, sticker: NewSticker) -> StickerSet:
        """At the end of the pack; the bot writes that stickers were added."""
        owner = await self._owner(device_key)
        async with self._unit_of_work() as uow:
            pack = await owned_pack(uow, owner.id, name)
        _may_act_for(owner)
        await self._sets.add(owner.id, pack.name, sticker)
        touched = await self._touch(pack)
        after = await self._set_after_change(pack.name)
        await self._notifications.stickers_added(touched, 1, len(after.stickers))
        return after

    async def replace(
        self, device_key: str, name: str, sticker_id: str, sticker: NewSticker
    ) -> StickerSet:
        """In the old sticker's place, with no notification."""
        owner = await self._owner(device_key)
        async with self._unit_of_work() as uow:
            pack = await owned_pack(uow, owner.id, name)
        _may_act_for(owner)
        found = await self._sets.get(pack.name)
        if found is None:
            raise PackNotFoundError(pack.name)
        if all(old.id != sticker_id for old in found.stickers):
            raise StickerNotFoundError(sticker_id)
        await self._sets.replace(owner.id, pack.name, sticker_id, sticker)
        await self._touch(pack)
        return await self._set_after_change(pack.name)

    async def delete(self, device_key: str, name: str, sticker_id: str) -> None:
        """As in the Mini App, with no notification; works with the bot stopped too."""
        owner = await self._owner(device_key)
        await delete_sticker(
            self._unit_of_work, self._sets, self._clock, owner.id, name, sticker_id
        )

    async def download(self, device_key: str, sticker_id: str) -> StickerFile:
        await self._owner(device_key)
        found = await self._sets.download(sticker_id)
        if found is None:
            raise StickerNotFoundError(sticker_id)
        return found

    async def _owner(self, device_key: str) -> User:
        """The account behind the key; UnauthorizedError for an unknown or revoked key."""
        async with self._unit_of_work() as uow:
            device = await device_for_key(uow, device_key)
            user = await uow.users.get(device.user_id)
        if user is None:
            # a device is made only for a user who confirmed it in the Mini App
            raise UnauthorizedError("the key's account is gone")
        return user

    async def _patiently(self, change: Callable[[], Awaitable[None]]) -> None:
        """One change of a copy; a short «too many requests» is waited out and tried again."""
        while True:
            try:
                await change()
                return
            except RateLimitedError as error:
                if error.retry_after_sec > MAX_COPY_WAIT_SEC:
                    raise
                await self._sleep(error.retry_after_sec)

    async def _touch(self, pack: Pack) -> Pack:
        touched = pack.touched(self._clock())
        async with self._unit_of_work() as uow:
            await uow.packs.save(touched)
            await uow.commit()
        return touched

    async def _set_after_change(self, name: str) -> StickerSet:
        found = await self._sets.get(name)
        if found is None:
            raise TelegramUnavailableError(f"Telegram has no set {name} right after changing it")
        return found


def _may_act_for(owner: User) -> None:
    """A user who stopped the bot gets bot_blocked without a call to Telegram."""
    if owner.bot_blocked:
        raise BotBlockedError(f"user {owner.id} stopped the bot")
