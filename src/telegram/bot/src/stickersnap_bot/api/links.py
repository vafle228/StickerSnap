"""Linking a computer, «Облачный сервис · Привязка» in the contract (bot-service.yaml).

The desktop asks for a code with no header, waits on it and cancels it with
`Bearer <pollToken>`, then calls with `Bearer <device key>`. The Mini App answers the code with
`tma <initData>` (TelegramUserId).
"""

from datetime import datetime, timedelta
from typing import Annotated, Literal, cast
from uuid import UUID

from fastapi import APIRouter, Depends, Path, Query, Request, status
from pydantic import ConfigDict, Field

from stickersnap_bot.api.bodies import Body
from stickersnap_bot.api.device_key import DeviceKey, bearer
from stickersnap_bot.api.miniapp import PREFIX as MINIAPP_PREFIX
from stickersnap_bot.api.miniapp import DeviceBody, device_body
from stickersnap_bot.api.telegram_user import TelegramUserId
from stickersnap_bot.application.linking import (
    MAX_WAIT,
    Computer,
    LinkedComputer,
    Linking,
    LinkPoll,
)
from stickersnap_bot.domain.devices import DEVICE_NAME_MAX_LENGTH, DeviceOs

# what secrets.token_urlsafe makes, and what fits Telegram's startapp
LINK_CODE = r"^[A-Za-z0-9_-]{1,64}$"


class LinkRequestBody(Body):
    model_config = ConfigDict(extra="forbid")

    # the domain trims and checks the name again
    name: str = Field(min_length=1, max_length=DEVICE_NAME_MAX_LENGTH)
    os: Literal["windows", "macos", "linux"]
    installation_id: UUID
    platform: str | None = Field(default=None, max_length=64)
    app_version: str | None = Field(default=None, max_length=32)

    def computer(self) -> Computer:
        return Computer(
            name=self.name,
            os=DeviceOs(self.os),
            installation_id=str(self.installation_id),
            platform=self.platform,
            app_version=self.app_version,
        )


class LinkCreatedBody(Body):
    code: str
    deep_link: str
    poll_token: str
    expires_at: datetime


class LinkedUserBody(Body):
    id: int
    first_name: str
    last_name: str | None = None
    username: str | None = None
    bot_blocked: bool


class LinkedDeviceBody(Body):
    device: DeviceBody
    user: LinkedUserBody


class LinkStateBody(Body):
    status: Literal["pending", "linked", "declined", "expired"]
    expires_at: datetime | None = None
    device_key: str | None = None
    device: DeviceBody | None = None
    user: LinkedUserBody | None = None


class LinkQuestionBody(Body):
    code: str
    name: str
    os: Literal["windows", "macos", "linux"]
    platform: str | None = None
    app_version: str | None = None
    requested_at: datetime
    expires_at: datetime
    linked_here: bool


class LinkConfirmBody(Body):
    model_config = ConfigDict(extra="forbid")

    notifications: bool


def _linked(linked: LinkedComputer) -> LinkedDeviceBody:
    profile = linked.user.profile
    return LinkedDeviceBody(
        device=device_body(linked.device),
        user=LinkedUserBody(
            id=linked.user.id,
            first_name=profile.first_name,
            last_name=profile.last_name,
            username=profile.username,
            bot_blocked=linked.user.bot_blocked,
        ),
    )


def _state(poll: LinkPoll) -> LinkStateBody:
    linked = None if poll.linked is None else _linked(poll.linked)
    return LinkStateBody(
        status=poll.status.value,
        expires_at=poll.expires_at,
        device_key=poll.device_key,
        device=None if linked is None else linked.device,
        user=None if linked is None else linked.user,
    )


def _linking(request: Request) -> Linking:
    # create_app puts the use cases on the app's state
    return cast(Linking, request.app.state.linking)


type _LinkingDep = Annotated[Linking, Depends(_linking)]
type _LinkCode = Annotated[str, Path(pattern=LINK_CODE)]
# a missing or foreign poll token is link_not_found, as for an unknown code
type _PollToken = Annotated[str, Depends(bearer)]

router = APIRouter(prefix="/v1", tags=["link"])


@router.post("/links", status_code=status.HTTP_201_CREATED, operation_id="createLink")
async def create_link(
    request: Request, linking: _LinkingDep, body: LinkRequestBody
) -> LinkCreatedBody:
    ip = request.client.host if request.client else None
    link = await linking.create(body.computer(), ip)
    return LinkCreatedBody(
        code=link.code,
        deep_link=link.deep_link,
        poll_token=link.poll_token,
        expires_at=link.expires_at,
    )


@router.get("/links/{code}", response_model_exclude_none=True, operation_id="getLinkState")
async def get_link_state(
    linking: _LinkingDep,
    code: _LinkCode,
    poll_token: _PollToken,
    wait: Annotated[int, Query(ge=0, le=int(MAX_WAIT.total_seconds()))] = 0,
) -> LinkStateBody:
    return _state(await linking.poll(code, poll_token, timedelta(seconds=wait)))


@router.delete("/links/{code}", status_code=status.HTTP_204_NO_CONTENT, operation_id="cancelLink")
async def cancel_link(linking: _LinkingDep, code: _LinkCode, poll_token: _PollToken) -> None:
    await linking.cancel(code, poll_token)


@router.get("/device", response_model_exclude_none=True, operation_id="getDevice")
async def get_device(linking: _LinkingDep, key: DeviceKey) -> LinkedDeviceBody:
    return _linked(await linking.linked(key))


@router.delete("/device", status_code=status.HTTP_204_NO_CONTENT, operation_id="unlinkThisDevice")
async def unlink_this_device(linking: _LinkingDep, key: DeviceKey) -> None:
    await linking.unlink(key)


miniapp_router = APIRouter(prefix=MINIAPP_PREFIX, tags=["miniapp"])


@miniapp_router.get(
    "/links/{code}", response_model_exclude_none=True, operation_id="getLinkRequest"
)
async def get_link_request(
    linking: _LinkingDep, user: TelegramUserId, code: _LinkCode
) -> LinkQuestionBody:
    question = await linking.question(user, code)
    link = question.link
    return LinkQuestionBody(
        code=link.code,
        name=link.device_name,
        os=link.device_os.value,
        platform=link.platform,
        app_version=link.app_version,
        requested_at=link.created_at,
        expires_at=link.expires_at,
        linked_here=question.linked_here,
    )


@miniapp_router.post(
    "/links/{code}/confirm", status_code=status.HTTP_204_NO_CONTENT, operation_id="confirmLink"
)
async def confirm_link(
    linking: _LinkingDep, user: TelegramUserId, code: _LinkCode, body: LinkConfirmBody
) -> None:
    await linking.confirm(user, code, body.notifications)


@miniapp_router.post(
    "/links/{code}/decline", status_code=status.HTTP_204_NO_CONTENT, operation_id="declineLink"
)
async def decline_link(linking: _LinkingDep, user: TelegramUserId, code: _LinkCode) -> None:
    await linking.decline(user, code)
