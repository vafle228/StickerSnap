from dataclasses import dataclass
from datetime import datetime
from enum import StrEnum

from stickersnap_bot.domain.errors import InvalidValueError

DEVICE_NAME_MAX_LENGTH = 64


class DeviceOs(StrEnum):
    WINDOWS = "windows"
    MACOS = "macos"
    LINUX = "linux"


def device_name(name: str) -> str:
    """The computer's name as the desktop sent it, trimmed; shown in the chat and «Устройства»."""
    trimmed = name.strip()
    if not trimmed or len(trimmed) > DEVICE_NAME_MAX_LENGTH:
        raise InvalidValueError(f"device name must be 1..{DEVICE_NAME_MAX_LENGTH} characters")
    return trimmed


@dataclass(frozen=True)
class Device:
    """A computer linked to a user. It calls the service with its key; only the hash is stored.

    Unlinking deletes the device, so its key stops working at once.
    """

    id: str
    user_id: int
    name: str
    os: DeviceOs
    key_hash: str
    linked_at: datetime
