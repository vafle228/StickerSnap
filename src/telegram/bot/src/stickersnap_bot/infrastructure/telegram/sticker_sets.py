"""The sticker sets port over the Bot API, through aiogram's Bot."""

from collections import OrderedDict
from collections.abc import Awaitable, Sequence
from pathlib import PurePosixPath

from aiogram import Bot
from aiogram.exceptions import TelegramAPIError, TelegramBadRequest, TelegramRetryAfter
from aiogram.types import BufferedInputFile, InputSticker, Sticker
from aiohttp import ClientError

from stickersnap_bot.application.errors import (
    ApplicationError,
    BotBlockedError,
    PackFullError,
    PackNameTakenError,
    PackNotFoundError,
    StickerNotFoundError,
    TelegramUnavailableError,
)
from stickersnap_bot.application.sticker_sets import (
    NewSticker,
    SetSticker,
    StickerFile,
    StickerSet,
)
from stickersnap_bot.domain.packs import PackKind
from stickersnap_bot.infrastructure.telegram.errors import translate

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
_VIDEO = "video"
# Telegram tells its refusals apart only by the description, so the table matches its words, as
# the desktop's BotApiErrors did
_REFUSALS: tuple[tuple[str, type[ApplicationError]], ...] = (
    ("bot was blocked by the user", BotBlockedError),
    # the user never pressed Start, so the bot cannot act for them
    ("PEER_ID_INVALID", BotBlockedError),
    ("user not found", BotBlockedError),
    ("bot can't initiate conversation with a user", BotBlockedError),
    ("sticker set name is already occupied", PackNameTakenError),
    ("STICKERS_TOO_MUCH", PackFullError),
    ("STICKERSET_INVALID", PackNotFoundError),
)


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
            raise translate(error) from error
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
            raise translate(error) from error

    async def bot_username(self) -> str:
        # aiogram keeps getMe's answer after the first call
        me = await self._call(self._bot.me())
        assert me.username is not None, "a bot always has a username"
        return me.username

    async def upload(self, owner_id: int, webm: bytes) -> str:
        sticker = BufferedInputFile(webm, "sticker.webm")
        uploaded = await self._call(self._bot.upload_sticker_file(owner_id, sticker, _VIDEO))
        return uploaded.file_id

    async def create(
        self,
        owner_id: int,
        name: str,
        title: str,
        kind: PackKind,
        stickers: Sequence[NewSticker],
    ) -> None:
        await self._call(
            self._bot.create_new_sticker_set(
                owner_id,
                name,
                title,
                [_input(sticker) for sticker in stickers],
                sticker_type="custom_emoji" if kind is PackKind.EMOJI else "regular",
            )
        )

    async def add(self, owner_id: int, name: str, sticker: NewSticker) -> None:
        await self._call(self._bot.add_sticker_to_set(owner_id, name, _input(sticker)))

    async def replace(
        self, owner_id: int, name: str, old_sticker_id: str, sticker: NewSticker
    ) -> None:
        await self._call(
            self._bot.replace_sticker_in_set(owner_id, name, old_sticker_id, _input(sticker))
        )

    async def _call[T](self, call: Awaitable[T]) -> T:
        try:
            return await call
        except TelegramRetryAfter as error:
            raise translate(error) from error
        except TelegramAPIError as error:
            raise _refused(error) from error
        except (ClientError, TimeoutError) as error:
            raise TelegramUnavailableError(str(error)) from error

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
            raise translate(error) from error
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
        unique_id=sticker.file_unique_id,
    )


def _input(sticker: NewSticker) -> InputSticker:
    return InputSticker(sticker=sticker.file_id, format=_VIDEO, emoji_list=list(sticker.emoji))


def _refused(error: TelegramAPIError) -> ApplicationError:
    words = error.message.lower()
    for known, refusal in _REFUSALS:
        if known.lower() in words:
            return refusal(error.message)
    return TelegramUnavailableError(error.message)
