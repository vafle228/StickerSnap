"""Publishing from a linked desktop, «Облачный сервис · Публикация» in the contract
(bot-service.yaml). Every route takes the desktop's `Bearer <device key>`."""

from typing import Annotated, Literal, cast

from fastapi import APIRouter, Depends, Path, Request, Response, status
from pydantic import ConfigDict, Field

from stickersnap_bot.api.bodies import Body
from stickersnap_bot.api.device_key import DeviceKey
from stickersnap_bot.api.miniapp import PACK_NAME
from stickersnap_bot.application.publishing import (
    MAX_STICKER_BYTES,
    MAX_STICKERS_AT_CREATION,
    Publishing,
)
from stickersnap_bot.application.sticker_sets import NewSticker, StickerSet
from stickersnap_bot.domain.packs import PACK_TITLE_MAX_LENGTH, PackKind

# what Telegram takes for one sticker
MAX_EMOJI = 20


class InputStickerBody(Body):
    model_config = ConfigDict(extra="forbid")

    file: str = Field(min_length=1)
    emoji: list[Annotated[str, Field(min_length=1)]] = Field(min_length=1, max_length=MAX_EMOJI)

    def sticker(self) -> NewSticker:
        return NewSticker(self.file, tuple(self.emoji))


class NewPackBody(Body):
    model_config = ConfigDict(extra="forbid")

    name: str = Field(pattern=PACK_NAME)
    # the domain trims and checks the title again
    title: str = Field(min_length=1, max_length=PACK_TITLE_MAX_LENGTH)
    kind: Literal["stickers", "emoji"]
    stickers: list[InputStickerBody] = Field(min_length=1, max_length=MAX_STICKERS_AT_CREATION)


class CopyPackBody(Body):
    model_config = ConfigDict(extra="forbid")

    # any set: from @Stickers, another bot or ours
    source: str = Field(pattern=PACK_NAME)
    name: str = Field(pattern=PACK_NAME)
    title: str = Field(min_length=1, max_length=PACK_TITLE_MAX_LENGTH)


class UploadedStickerBody(Body):
    id: str


class PublishedStickerBody(Body):
    id: str
    unique_id: str
    emoji: list[str]


class PublishedPackBody(Body):
    name: str
    title: str
    kind: Literal["stickers", "emoji"]
    stickers: list[PublishedStickerBody]


class PackNameStateBody(Body):
    name: str
    taken: bool


def _pack(found: StickerSet) -> PublishedPackBody:
    return PublishedPackBody(
        name=found.name,
        title=found.title,
        kind=found.kind.value,
        stickers=[
            PublishedStickerBody(id=s.id, unique_id=s.unique_id, emoji=list(s.emoji))
            for s in found.stickers
        ],
    )


async def _webm(request: Request) -> bytes:
    """The body, read no further than one byte past the limit: enough to refuse a larger file."""
    read = bytearray()
    async for chunk in request.stream():
        read += chunk
        if len(read) > MAX_STICKER_BYTES:
            break
    return bytes(read)


def _publishing(request: Request) -> Publishing:
    # create_app puts the use cases on the app's state
    return cast(Publishing, request.app.state.publishing)


type _PublishingDep = Annotated[Publishing, Depends(_publishing)]
type _PackName = Annotated[str, Path(pattern=PACK_NAME)]
type _StickerId = Annotated[str, Path(alias="stickerId")]

router = APIRouter(prefix="/v1", tags=["publish"])


@router.post("/stickers/upload", status_code=status.HTTP_201_CREATED, operation_id="uploadSticker")
async def upload_sticker(
    publishing: _PublishingDep, key: DeviceKey, webm: Annotated[bytes, Depends(_webm)]
) -> UploadedStickerBody:
    return UploadedStickerBody(id=await publishing.upload(key, webm))


@router.post("/packs", status_code=status.HTTP_201_CREATED, operation_id="createPack")
async def create_pack(
    publishing: _PublishingDep, key: DeviceKey, body: NewPackBody
) -> PublishedPackBody:
    made = await publishing.create(
        key,
        body.name,
        body.title,
        PackKind(body.kind),
        [sticker.sticker() for sticker in body.stickers],
    )
    return _pack(made)


@router.post("/packs/copies", status_code=status.HTTP_201_CREATED, operation_id="copyPack")
async def copy_pack(
    publishing: _PublishingDep, key: DeviceKey, body: CopyPackBody
) -> PublishedPackBody:
    return _pack(await publishing.copy(key, body.source, body.name, body.title))


@router.get("/packs/{name}", operation_id="getPublishedPack")
async def get_published_pack(
    publishing: _PublishingDep, key: DeviceKey, name: _PackName
) -> PublishedPackBody:
    return _pack(await publishing.get(key, name))


@router.get("/pack-names/{name}", operation_id="checkPackName")
async def check_pack_name(
    publishing: _PublishingDep, key: DeviceKey, name: _PackName
) -> PackNameStateBody:
    return PackNameStateBody(name=name, taken=await publishing.is_name_taken(key, name))


@router.post("/packs/{name}/stickers", operation_id="addSticker")
async def add_sticker(
    publishing: _PublishingDep, key: DeviceKey, name: _PackName, body: InputStickerBody
) -> PublishedPackBody:
    return _pack(await publishing.add(key, name, body.sticker()))


@router.put("/packs/{name}/stickers/{stickerId}", operation_id="replaceSticker")
async def replace_sticker(
    publishing: _PublishingDep,
    key: DeviceKey,
    name: _PackName,
    sticker_id: _StickerId,
    body: InputStickerBody,
) -> PublishedPackBody:
    return _pack(await publishing.replace(key, name, sticker_id, body.sticker()))


@router.delete(
    "/packs/{name}/stickers/{stickerId}",
    status_code=status.HTTP_204_NO_CONTENT,
    operation_id="removeSticker",
)
async def remove_sticker(
    publishing: _PublishingDep, key: DeviceKey, name: _PackName, sticker_id: _StickerId
) -> None:
    await publishing.delete(key, name, sticker_id)


@router.get("/stickers/{stickerId}/file", operation_id="downloadSticker")
async def download_sticker(
    publishing: _PublishingDep, key: DeviceKey, sticker_id: _StickerId
) -> Response:
    found = await publishing.download(key, sticker_id)
    return Response(found.content, media_type=found.media_type)
