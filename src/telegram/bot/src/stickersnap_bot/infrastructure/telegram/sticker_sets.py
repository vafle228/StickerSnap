"""The sticker sets port over the Bot API, through aiogram's Bot."""

from collections import OrderedDict
from pathlib import PurePosixPath

from aiogram import Bot
from aiogram.exceptions import TelegramAPIError, TelegramBadRequest, TelegramRetryAfter
from aiogram.types import Sticker
from aiohttp import ClientError

from stickersnap_bot.application.errors import (
    RateLimitedError,
    StickerNotFoundError,
    TelegramUnavailableError,
)
from stickersnap_bot.application.sticker_sets import SetSticker, StickerFile, StickerSet
from stickersnap_bot.domain.packs import PackKind

_MEDIA_TYPES = {
    ".webm": "video/webm",
    ".webp": "image/webp",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".png": "image/png",
    ".tgs": "application/x-tgsticker",
}
# A file id always names the same bytes, so a downloaded file never goes stale
_CACHE_BYTES = 16 * 1024 * 1024


class BotStickerSets:
    def __init__(self, bot: Bot, cache_bytes: int = _CACHE_BYTES) -> None:
        self._bot = bot
        self._cache: OrderedDict[str, StickerFile] = OrderedDict()
        self._cache_bytes = cache_bytes
        self._cached_bytes = 0

    async def get(self, name: str) -> StickerSet | None:
        try:
            found = await self._bot.get_sticker_set(name)
        except TelegramBadRequest:
            # STICKERSET_INVALID: Telegram does not know the set (deleted through @Stickers)
            return None
        except TelegramAPIError as error:
            raise _translate(error) from error
        return StickerSet(
            name=found.name,
            title=found.title,
            kind=PackKind.EMOJI if found.sticker_type == "custom_emoji" else PackKind.STICKERS,
            stickers=tuple(_sticker(s) for s in found.stickers),
            thumb_file_id=found.thumbnail.file_id if found.thumbnail else None,
        )

    async def delete_sticker(self, sticker_id: str) -> None:
        try:
            await self._bot.delete_sticker_from_set(sticker_id)
        except TelegramBadRequest as error:
            raise StickerNotFoundError(sticker_id) from error
        except TelegramAPIError as error:
            raise _translate(error) from error

    async def download(self, file_id: str) -> StickerFile | None:
        cached = self._cache.get(file_id)
        if cached is not None:
            self._cache.move_to_end(file_id)
            return cached
        try:
            file = await self._bot.get_file(file_id)
            if file.file_path is None:
                return None
            content = await self._bot.download_file(file.file_path)
        except TelegramBadRequest:
            return None
        except TelegramAPIError as error:
            raise _translate(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error
        if content is None:
            return None
        suffix = PurePosixPath(file.file_path).suffix.lower()
        downloaded = StickerFile(
            content.read(), _MEDIA_TYPES.get(suffix, "application/octet-stream")
        )
        self._remember(file_id, downloaded)
        return downloaded

    def _remember(self, file_id: str, file: StickerFile) -> None:
        self._cache[file_id] = file
        self._cached_bytes += len(file.content)
        while self._cached_bytes > self._cache_bytes and self._cache:
            _, oldest = self._cache.popitem(last=False)
            self._cached_bytes -= len(oldest.content)


def _sticker(sticker: Sticker) -> SetSticker:
    return SetSticker(
        id=sticker.file_id,
        emoji=(sticker.emoji,) if sticker.emoji else (),
        thumb_file_id=sticker.thumbnail.file_id if sticker.thumbnail else None,
        video_file_id=sticker.file_id,
    )


def _translate(error: TelegramAPIError) -> Exception:
    if isinstance(error, TelegramRetryAfter):
        return RateLimitedError(error.retry_after)
    return TelegramUnavailableError(str(error))
