"""Port to the sticker sets Telegram keeps: what is in a set, changing it on the owner's behalf,
a sticker's file.

The adapter raises RateLimitedError and TelegramUnavailableError from application.errors, and the
refusals each method names.
"""

from collections.abc import Sequence
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
    # file_unique_id: the same for every bot and over time, what the desktop's packs.json keeps
    unique_id: str


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


@dataclass(frozen=True)
class NewSticker:
    """A sticker to put into a set: an uploaded .webm and the emoji it answers to."""

    file_id: str
    emoji: tuple[str, ...]


class StickerSets(Protocol):
    async def get(self, name: str) -> StickerSet | None:
        """The set as Telegram has it now, or None when Telegram does not know it."""
        ...

    async def delete_sticker(self, sticker_id: str) -> None: ...

    async def download(self, file_id: str) -> StickerFile | None:
        """A sticker's or a thumbnail's file, or None when Telegram has no such file."""
        ...

    async def bot_username(self) -> str:
        """The bot whose sets these are: their names end in `_by_<username>`."""
        ...

    # Changes on the owner's behalf: BotBlockedError when the bot may not act for the owner

    async def upload(self, owner_id: int, webm: bytes) -> str:
        """Sends a video sticker's .webm once; the file id goes into NewSticker."""
        ...

    async def create(
        self,
        owner_id: int,
        name: str,
        title: str,
        kind: PackKind,
        stickers: Sequence[NewSticker],
    ) -> None:
        """A new set of video stickers; PackNameTakenError when the name is taken."""
        ...

    async def add(self, owner_id: int, name: str, sticker: NewSticker) -> None:
        """At the end of the set; PackFullError, PackNotFoundError for a set Telegram lost."""
        ...

    async def replace(
        self, owner_id: int, name: str, old_sticker_id: str, sticker: NewSticker
    ) -> None:
        """In the old sticker's place, which the caller made sure is in the set;
        PackNotFoundError for a set Telegram lost."""
        ...
