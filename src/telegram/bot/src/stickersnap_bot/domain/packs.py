import re
from dataclasses import dataclass, replace
from datetime import datetime
from enum import StrEnum

from stickersnap_bot.domain.errors import InvalidValueError

PACK_TITLE_MAX_LENGTH = 64
PACK_NAME_MAX_LENGTH = 64
# Telegram's rule for a set name: a letter first, then letters, digits and single underscores
_PACK_NAME = re.compile(r"[A-Za-z](?:_?[A-Za-z0-9])*")


class PackKind(StrEnum):
    STICKERS = "stickers"
    EMOJI = "emoji"

    @property
    def limit(self) -> int:
        """How many stickers Telegram takes into a set of this kind; count = limit is «Заполнен»."""
        return 120 if self is PackKind.STICKERS else 200


def pack_name(name: str, bot_username: str) -> str:
    """A set name the bot may own: Telegram's pattern, the `_by_<bot>` suffix, 64 chars."""
    suffix = f"_by_{bot_username}"
    if (
        len(name) > PACK_NAME_MAX_LENGTH
        or len(name) <= len(suffix)
        or not name.lower().endswith(suffix.lower())
        or not _PACK_NAME.fullmatch(name)
    ):
        raise InvalidValueError(f"{name!r} is not a set name of @{bot_username}")
    return name


def pack_title(title: str) -> str:
    trimmed = title.strip()
    if not trimmed or len(trimmed) > PACK_TITLE_MAX_LENGTH:
        raise InvalidValueError(f"pack title must be 1..{PACK_TITLE_MAX_LENGTH} characters")
    return trimmed


@dataclass(frozen=True)
class Pack:
    """A sticker set the bot created for a user; the service is the truth about who owns it.

    The stickers, their count and whether the set still exists are Telegram's: the service reads
    them with getStickerSet and keeps only what Telegram does not know.
    """

    name: str
    owner_id: int
    title: str
    kind: PackKind
    created_at: datetime
    # the last change made through the service: «Мои паки» lists the freshest first
    updated_at: datetime

    @classmethod
    def create(cls, name: str, owner_id: int, title: str, kind: PackKind, now: datetime) -> "Pack":
        return cls(
            name=name,
            owner_id=owner_id,
            title=pack_title(title),
            kind=kind,
            created_at=now,
            updated_at=now,
        )

    def retitled(self, title: str, now: datetime) -> "Pack":
        return replace(self, title=pack_title(title), updated_at=now)

    def touched(self, now: datetime) -> "Pack":
        return replace(self, updated_at=now)
