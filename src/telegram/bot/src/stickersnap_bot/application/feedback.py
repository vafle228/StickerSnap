"""«Написать отзыв» from the desktop and the Mini App: store it, keep to the limits, forward it.

Feedback is the user's once it is stored: a feedback chat that does not answer leaves it in the
database unforwarded, and the caller still hears «отправлено».
"""

import logging
import math
from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timedelta

from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.device_keys import device_for_key
from stickersnap_bot.application.errors import ApplicationError, BadRequestError, RateLimitedError
from stickersnap_bot.application.feedback_inbox import FeedbackInbox
from stickersnap_bot.application.persistence import UnitOfWork, UnitOfWorkFactory
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.errors import InvalidValueError
from stickersnap_bot.domain.feedback import (
    Author,
    Feedback,
    FeedbackSource,
    FeedbackType,
    ReportedError,
)
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import User

# per Telegram account (the Mini App and its computers together) or per installation without a link
FEEDBACK_PER_HOUR = 5
# without a link the installation id is the caller's word, so its IP is counted too
ANONYMOUS_FEEDBACK_PER_IP_PER_HOUR = 10
_HOUR = timedelta(hours=1)

_log = logging.getLogger(__name__)


@dataclass(frozen=True)
class MiniAppCaller:
    """Signed in with the Mini App's launch data (SignIn)."""

    user_id: int


@dataclass(frozen=True)
class DeviceCaller:
    """A linked computer, by its key."""

    key: str


@dataclass(frozen=True)
class AnonymousCaller:
    """A desktop that is not linked."""


type Caller = MiniAppCaller | DeviceCaller | AnonymousCaller


@dataclass(frozen=True)
class FeedbackForm:
    """The request's body, as the contract's FeedbackRequest has it."""

    type: FeedbackType
    text: str
    can_reply: bool
    contact: str | None = None
    installation_id: str | None = None
    app_version: str | None = None
    platform: str | None = None
    error: ReportedError | None = None
    diagnostics: str | None = None


class SendFeedback:
    def __init__(
        self, unit_of_work: UnitOfWorkFactory, inbox: FeedbackInbox | None, clock: Clock
    ) -> None:
        """Without an inbox (no FEEDBACK_CHAT_ID) feedback is only stored."""
        self._unit_of_work = unit_of_work
        self._inbox = inbox
        self._clock = clock

    async def __call__(self, caller: Caller, form: FeedbackForm, ip: str | None = None) -> int:
        """The new feedback's number; BadRequestError, UnauthorizedError or RateLimitedError."""
        now = self._clock()
        async with self._unit_of_work() as uow:
            user_id, user, device = await _account(uow, caller)
            author = Author(
                source=(
                    FeedbackSource.MINIAPP
                    if isinstance(caller, MiniAppCaller)
                    else FeedbackSource.DESKTOP
                ),
                user_id=user_id,
                device_id=None if device is None else device.id,
                installation_id=form.installation_id,
                # a hash, not the address: it only has to tell callers apart for an hour
                ip_hash=None if ip is None else hash_secret(ip),
            )
            try:
                feedback = Feedback.create(
                    form.type,
                    form.text,
                    author,
                    form.can_reply,
                    now,
                    contact=form.contact,
                    app_version=form.app_version,
                    platform=form.platform,
                    error=form.error,
                    diagnostics=form.diagnostics,
                )
            except InvalidValueError as error:
                raise BadRequestError(str(error)) from error
            await _keep_to_limits(uow, author, now)
            feedback = await uow.feedback.add(feedback)
            await uow.commit()
        assert feedback.id is not None
        await self._forward(feedback, user, device)
        return feedback.id

    async def _forward(self, feedback: Feedback, user: User | None, device: Device | None) -> None:
        if self._inbox is None:
            _log.warning("FEEDBACK_CHAT_ID is not set: feedback #%s is only stored", feedback.id)
            return
        try:
            await self._inbox.deliver(feedback, user, device)
        except ApplicationError:
            _log.exception("feedback #%s stays unforwarded", feedback.id)
            return
        async with self._unit_of_work() as uow:
            await uow.feedback.save(feedback.forwarded(self._clock()))
            await uow.commit()


async def _account(
    uow: UnitOfWork, caller: Caller
) -> tuple[int | None, User | None, Device | None]:
    """The caller's Telegram account: its id, its profile for the message, the computer."""
    match caller:
        case MiniAppCaller(user_id):
            # SignIn has just remembered the caller
            return user_id, await uow.users.get(user_id), None
        case DeviceCaller(key):
            device = await device_for_key(uow, key)
            return device.user_id, await uow.users.get(device.user_id), device
        case AnonymousCaller():
            return None, None, None


async def _keep_to_limits(uow: UnitOfWork, author: Author, now: datetime) -> None:
    since = now - _HOUR
    if author.user_id is not None:
        _check(await uow.feedback.sent_since(since, user_id=author.user_id), FEEDBACK_PER_HOUR, now)
        return
    assert author.installation_id is not None, "Feedback.create asks for it"
    _check(
        await uow.feedback.sent_since(since, installation_id=author.installation_id),
        FEEDBACK_PER_HOUR,
        now,
    )
    if author.ip_hash is not None:
        _check(
            await uow.feedback.sent_since(since, ip_hash=author.ip_hash),
            ANONYMOUS_FEEDBACK_PER_IP_PER_HOUR,
            now,
        )


def _check(sent: Sequence[datetime], limit: int, now: datetime) -> None:
    if len(sent) < limit:
        return
    # one more is allowed once this one is an hour old
    frees_at = sent[len(sent) - limit] + _HOUR
    raise RateLimitedError(max(1, math.ceil((frees_at - now).total_seconds())))
