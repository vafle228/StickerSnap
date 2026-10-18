"""`Authorization: Bearer <device key>`: the linked computer behind a call from the desktop.

Feedback (F1) and publishing (S5) use it; the link (S4) hands the key out.
"""

from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.application.persistence import UnitOfWork
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.secrets import hash_secret


async def device_for_key(uow: UnitOfWork, key: str) -> Device:
    """The device the key belongs to; UnauthorizedError for an unknown or revoked key (unlinking
    deletes the device)."""
    device = await uow.devices.find_by_key_hash(hash_secret(key)) if key else None
    if device is None:
        raise UnauthorizedError("unknown device key")
    return device
