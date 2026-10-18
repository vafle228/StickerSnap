"""The Mini App's API, «Облачный сервис · API Mini App» in the contract (bot-service.yaml).

Every route but the media links takes the caller from `Authorization: tma <initData>`
(TelegramUserId); the media links are signed themselves, because <img> and <video> send no
header.
"""

from datetime import datetime
from typing import Annotated, Literal, cast

from fastapi import APIRouter, Depends, Path, Request, Response, status

from stickersnap_bot.api.bodies import Body
from stickersnap_bot.api.telegram_user import TelegramUserId
from stickersnap_bot.application.miniapp import MiniApp, PackView, StickerView
from stickersnap_bot.domain.devices import Device

PREFIX = "/v1/miniapp"
MEDIA_PATH = "/media/{token}"
# Telegram's set name: a letter, then letters, digits and underscores (contract, PackName)
PACK_NAME = r"^[A-Za-z][A-Za-z0-9_]{0,63}$"


class StickerBody(Body):
    id: str
    emoji: list[str]
    thumb_url: str
    video_url: str


class PackBody(Body):
    name: str
    title: str
    kind: Literal["stickers", "emoji"]
    count: int
    limit: int
    state: Literal["ok", "missing"]
    cover_url: str | None = None
    updated_at: datetime


class PackDetailBody(PackBody):
    stickers: list[StickerBody]


class PackListBody(Body):
    packs: list[PackBody]


class DeviceBody(Body):
    id: str
    name: str
    os: Literal["windows", "macos", "linux"]
    linked_at: datetime


class DeviceListBody(Body):
    devices: list[DeviceBody]


def media_url(token: str) -> str:
    return PREFIX + MEDIA_PATH.format(token=token)


def _pack(view: PackView) -> PackBody:
    return PackBody(
        name=view.name,
        title=view.title,
        kind=view.kind.value,
        count=view.count,
        limit=view.limit,
        state=view.state.value,
        cover_url=None if view.cover is None else media_url(view.cover),
        updated_at=view.updated_at,
    )


def _sticker(view: StickerView) -> StickerBody:
    return StickerBody(
        id=view.id,
        emoji=list(view.emoji),
        thumb_url=media_url(view.thumb),
        video_url=media_url(view.video),
    )


def device_body(device: Device) -> DeviceBody:
    return DeviceBody(
        id=device.id, name=device.name, os=device.os.value, linked_at=device.linked_at
    )


def _miniapp(request: Request) -> MiniApp:
    # create_app puts the use cases on the app's state
    return cast(MiniApp, request.app.state.miniapp)


type _MiniAppDep = Annotated[MiniApp, Depends(_miniapp)]


type _PackName = Annotated[str, Path(pattern=PACK_NAME)]

router = APIRouter(prefix=PREFIX, tags=["miniapp"])


@router.get("/packs", response_model_exclude_none=True, operation_id="listPacks")
async def list_packs(miniapp: _MiniAppDep, user: TelegramUserId) -> PackListBody:
    return PackListBody(packs=[_pack(view) for view in await miniapp.list_packs(user)])


@router.get("/packs/{name}", response_model_exclude_none=True, operation_id="getPack")
async def get_pack(miniapp: _MiniAppDep, user: TelegramUserId, name: _PackName) -> PackDetailBody:
    view = await miniapp.get_pack(user, name)
    return PackDetailBody(**_pack(view).model_dump(), stickers=[_sticker(s) for s in view.stickers])


@router.delete("/packs/{name}", status_code=status.HTTP_204_NO_CONTENT, operation_id="forgetPack")
async def forget_pack(miniapp: _MiniAppDep, user: TelegramUserId, name: _PackName) -> None:
    await miniapp.forget_pack(user, name)


@router.delete(
    "/packs/{name}/stickers/{stickerId}",
    status_code=status.HTTP_204_NO_CONTENT,
    operation_id="deleteSticker",
)
async def delete_sticker(
    miniapp: _MiniAppDep,
    user: TelegramUserId,
    name: _PackName,
    sticker_id: Annotated[str, Path(alias="stickerId")],
) -> None:
    await miniapp.delete_sticker(user, name, sticker_id)


@router.get("/devices", operation_id="listDevices")
async def list_devices(miniapp: _MiniAppDep, user: TelegramUserId) -> DeviceListBody:
    return DeviceListBody(devices=[device_body(d) for d in await miniapp.list_devices(user)])


@router.delete(
    "/devices/{deviceId}", status_code=status.HTTP_204_NO_CONTENT, operation_id="unlinkDevice"
)
async def unlink_device(
    miniapp: _MiniAppDep, user: TelegramUserId, device_id: Annotated[str, Path(alias="deviceId")]
) -> None:
    await miniapp.unlink_device(user, device_id)


@router.get(MEDIA_PATH, include_in_schema=False)
async def get_media(miniapp: _MiniAppDep, token: str) -> Response:
    opened = await miniapp.open_media(token)
    if opened is None:
        return Response(status_code=status.HTTP_404_NOT_FOUND)
    file, seconds = opened
    return Response(
        file.content,
        media_type=file.media_type,
        headers={"Cache-Control": f"private, max-age={seconds}"},
    )
