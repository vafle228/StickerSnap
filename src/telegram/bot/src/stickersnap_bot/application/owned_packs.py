"""What the Mini App and the desktop do alike to a user's packs.

A pack is the user's when the service's row says so; any other pack, someone else's or one the
service never made, is «not found».
"""

from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.errors import PackNotFoundError, StickerNotFoundError
from stickersnap_bot.application.persistence import UnitOfWork, UnitOfWorkFactory
from stickersnap_bot.application.sticker_sets import StickerSets
from stickersnap_bot.domain.packs import Pack


async def owned_pack(uow: UnitOfWork, user_id: int, name: str) -> Pack:
    pack = await uow.packs.get(name)
    if pack is None or pack.owner_id != user_id:
        raise PackNotFoundError(name)
    return pack


async def delete_sticker(
    unit_of_work: UnitOfWorkFactory,
    sticker_sets: StickerSets,
    clock: Clock,
    user_id: int,
    name: str,
    sticker_id: str,
) -> None:
    async with unit_of_work() as uow:
        pack = await owned_pack(uow, user_id, name)
    found = await sticker_sets.get(pack.name)
    if found is None:
        raise PackNotFoundError(pack.name)
    if all(sticker.id != sticker_id for sticker in found.stickers):
        raise StickerNotFoundError(sticker_id)
    await sticker_sets.delete_sticker(sticker_id)
    async with unit_of_work() as uow:
        # The last sticker takes the set with it: Telegram drops an empty set
        if len(found.stickers) == 1:
            await uow.packs.delete(pack.name)
        else:
            await uow.packs.save(pack.touched(clock()))
        await uow.commit()
