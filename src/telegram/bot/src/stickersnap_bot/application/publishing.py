"""Publishing from a linked desktop: bot plan «Публикация», contract «Облачный сервис · Публикация».

The desktop calls with its device key; the packs are made and filled on behalf of the account the
key is linked to, never anyone the request names. The steps follow the desktop's port
IStickerPublisher one to one: upload a .webm once, then create a pack with it, add it or put it in
an old sticker's place. Each change answers with the set after it, so the desktop finds the new
sticker without asking again.

Only packs in the service's registry are written to (Dmitriu chose this 2026-10-18): Telegram does
not say who owns a set, and deleteStickerFromSet does not check it at all.
"""

from collections.abc import Sequence

from stickersnap_bot.application.chat import Notifications
from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.device_keys import device_for_key
from stickersnap_bot.application.errors import (
    BadRequestError,
    BotBlockedError,
    PackNameInvalidError,
    PackNameTakenError,
    PackNotFoundError,
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
from stickersnap_bot.domain.packs import Pack, PackKind, pack_name
from stickersnap_bot.domain.users import User

# what Telegram takes for a video sticker, and the preset limit on the desktop
MAX_STICKER_BYTES = 256 * 1024
# what createNewStickerSet takes at once
MAX_STICKERS_AT_CREATION = 50
# every WebM file starts with an EBML header
_EBML_MAGIC = b"\x1a\x45\xdf\xa3"


class Publishing:
    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        sticker_sets: StickerSets,
        notifications: Notifications,
        clock: Clock,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._sets = sticker_sets
        self._notifications = notifications
        self._clock = clock

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

    async def get(self, device_key: str, name: str) -> StickerSet:
        owner = await self._owner(device_key)
        async with self._unit_of_work() as uow:
            pack = await owned_pack(uow, owner.id, name)
        found = await self._sets.get(pack.name)
        if found is None:
            raise PackNotFoundError(pack.name)
        return found

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
