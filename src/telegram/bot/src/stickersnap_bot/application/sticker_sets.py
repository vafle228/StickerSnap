"""Port to the sticker sets Telegram keeps: what is in a set, removing a sticker, a sticker's file.

The adapter raises RateLimitedError and TelegramUnavailableError from application.errors.
"""

from dataclasses import dataclass
from typing import Protocol

from stickersnap_bot.domain.packs import PackKind


@dataclass(frozen=True)
class SetSticker:
    # file_id: the sticker's id in the contract and what deleteStickerFromSet takes
    id: str
    emoji: tuple[str, ...]
    # file ids of the first frame and of the .webm
    thumb_file_id: str | None
    video_file_id: str


@dataclass(frozen=True)
class StickerSet:
    name: str
    title: str
    kind: PackKind
    # in Telegram's order
    stickers: tuple[SetSticker, ...]
    # the set's own cover, when one was set
    thumb_file_id: str | None = None


@dataclass(frozen=True)
class StickerFile:
    content: bytes
    media_type: str


class StickerSets(Protocol):
    async def get(self, name: str) -> StickerSet | None:
        """The set as Telegram has it now, or None when Telegram does not know it."""
        ...

    async def delete_sticker(self, sticker_id: str) -> None: ...

    async def download(self, file_id: str) -> StickerFile | None:
        """A sticker's or a thumbnail's file, or None when Telegram has no such file."""
        ...
