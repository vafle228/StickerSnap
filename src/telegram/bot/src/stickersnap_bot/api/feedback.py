"""Feedback from the desktop and the Mini App, «Облачный сервис · Отзывы» in the contract
(bot-service.yaml).

The caller is whoever the Authorization header says: `tma <initData>` from the Mini App,
`Bearer <device key>` from a linked computer, nothing from a desktop that is not linked.
"""

from typing import Annotated, Literal, cast
from uuid import UUID

from fastapi import APIRouter, Depends, Header, Request, status
from pydantic import ConfigDict, Field

from stickersnap_bot.api.bodies import Body
from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.application.feedback import (
    AnonymousCaller,
    Caller,
    DeviceCaller,
    FeedbackForm,
    MiniAppCaller,
    SendFeedback,
)
from stickersnap_bot.application.sign_in import SignIn
from stickersnap_bot.domain.feedback import (
    CONTACT_MAX_LENGTH,
    FEEDBACK_TEXT_MAX_LENGTH,
    FeedbackType,
    ReportedError,
)


class FeedbackErrorBody(Body):
    model_config = ConfigDict(extra="forbid")

    code: str = Field(min_length=1, max_length=64)
    trace_id: str | None = Field(default=None, min_length=1, max_length=64)


class FeedbackBody(Body):
    model_config = ConfigDict(extra="forbid")

    type: Literal["bug", "idea", "other"]
    text: str = Field(max_length=FEEDBACK_TEXT_MAX_LENGTH)
    can_reply: bool
    contact: str | None = Field(default=None, min_length=1, max_length=CONTACT_MAX_LENGTH)
    installation_id: UUID | None = None
    app_version: str | None = Field(default=None, max_length=32)
    platform: str | None = Field(default=None, max_length=64)
    error: FeedbackErrorBody | None = None
    # the byte limit is the domain's: the schema cannot count UTF-8
    diagnostics: str | None = None

    def form(self) -> FeedbackForm:
        return FeedbackForm(
            type=FeedbackType(self.type),
            text=self.text,
            can_reply=self.can_reply,
            contact=self.contact,
            installation_id=None if self.installation_id is None else str(self.installation_id),
            app_version=self.app_version,
            platform=self.platform,
            error=None
            if self.error is None
            else ReportedError(self.error.code, self.error.trace_id),
            diagnostics=self.diagnostics,
        )


class FeedbackCreatedBody(Body):
    id: int


async def _caller(
    request: Request, authorization: Annotated[str | None, Header()] = None
) -> Caller:
    if not authorization:
        return AnonymousCaller()
    scheme, _, credentials = authorization.partition(" ")
    match scheme.lower():
        case "tma" if credentials:
            # create_app puts it on the app's state
            sign_in = cast(SignIn, request.app.state.sign_in)
            return MiniAppCaller(await sign_in(credentials))
        case "bearer" if credentials:
            return DeviceCaller(credentials)
        case _:
            raise UnauthorizedError(f"unknown Authorization: {scheme or 'empty'}")


def _send_feedback(request: Request) -> SendFeedback:
    return cast(SendFeedback, request.app.state.send_feedback)


router = APIRouter(prefix="/v1", tags=["feedback"])


@router.post("/feedback", status_code=status.HTTP_201_CREATED, operation_id="sendFeedback")
async def send_feedback(
    request: Request,
    body: FeedbackBody,
    send: Annotated[SendFeedback, Depends(_send_feedback)],
    caller: Annotated[Caller, Depends(_caller)],
) -> FeedbackCreatedBody:
    ip = request.client.host if request.client else None
    return FeedbackCreatedBody(id=await send(caller, body.form(), ip))
