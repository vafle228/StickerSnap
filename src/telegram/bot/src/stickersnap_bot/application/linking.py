"""Linking a computer to a Telegram account (bot plan, «Привязка»; board 18).

The desktop asks for a code and waits on it with its poll token. The link opens the Mini App,
where the user presses «Привязать» or «Это не я». The desktop's next poll after «Привязать»
claims the code: the service makes the device, hands its key out once and the bot writes
«Компьютер привязан». From then on the desktop calls with `Bearer <device key>`.

A desktop waits on its code without polling the database: an answer from the Mini App wakes the
waiting request (LinkSignals). The service is one process, so the signals live in memory.
"""

import asyncio
import hmac
import secrets
import uuid
from collections import defaultdict
from collections.abc import Callable
from dataclasses import dataclass
from datetime import datetime, timedelta
from enum import StrEnum

from stickersnap_bot.application.app_links import AppLinks
from stickersnap_bot.application.chat import Notifications
from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.device_keys import device_for_key
from stickersnap_bot.application.errors import BadRequestError, LinkNotFoundError
from stickersnap_bot.application.limits import keep_to_limit
from stickersnap_bot.application.persistence import UnitOfWork, UnitOfWorkFactory
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.errors import DomainError, InvalidValueError
from stickersnap_bot.domain.links import LinkRequest, LinkStatus
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import User

LINKS_PER_INSTALLATION_PER_HOUR = 10
LINKS_PER_IP_PER_HOUR = 30
# the longest a desktop's poll is held open
MAX_WAIT = timedelta(seconds=25)
STARTAPP_PREFIX = "link_"
_HOUR = timedelta(hours=1)


@dataclass(frozen=True)
class Computer:
    """What the desktop tells about itself when it asks for a code."""

    name: str
    os: DeviceOs
    installation_id: str
    platform: str | None = None
    app_version: str | None = None


@dataclass(frozen=True)
class NewLink:
    code: str
    deep_link: str
    # the desktop's secret for waiting and cancelling; the service keeps only its hash
    poll_token: str
    expires_at: datetime


@dataclass(frozen=True)
class LinkedComputer:
    device: Device
    user: User


class PollStatus(StrEnum):
    PENDING = "pending"
    # «Привязать»: the key is in this answer, and only in this one
    LINKED = "linked"
    DECLINED = "declined"
    # the code ran out, or its key was already handed out
    EXPIRED = "expired"


@dataclass(frozen=True)
class LinkPoll:
    status: PollStatus
    expires_at: datetime | None = None
    device_key: str | None = None
    linked: LinkedComputer | None = None


@dataclass(frozen=True)
class LinkQuestion:
    """«Привязать этот компьютер?» (18·1)."""

    link: LinkRequest
    # this computer is the user's already: the Mini App confirms without asking
    linked_here: bool


class LinkSignals:
    """Wakes the desktops waiting on a code when it is answered."""

    def __init__(self) -> None:
        self._waiting: defaultdict[str, set[asyncio.Event]] = defaultdict(set)

    async def wait(self, code: str, at_most: timedelta) -> None:
        """Returns when the code is answered or the time is up, whichever comes first."""
        answered = asyncio.Event()
        self._waiting[code].add(answered)
        try:
            await asyncio.wait_for(answered.wait(), at_most.total_seconds())
        except TimeoutError:
            pass
        finally:
            waiting = self._waiting[code]
            waiting.discard(answered)
            if not waiting:
                del self._waiting[code]

    def answered(self, code: str) -> None:
        for event in self._waiting.get(code, ()):
            event.set()


class Linking:
    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        app_links: AppLinks,
        notifications: Notifications,
        signals: LinkSignals,
        clock: Clock,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._app_links = app_links
        self._notifications = notifications
        self._signals = signals
        self._clock = clock
        # two polls of one code at once must not both take the key
        self._claiming = asyncio.Lock()

    # The desktop

    async def create(self, computer: Computer, ip: str | None = None) -> NewLink:
        """A code for ten minutes; BadRequestError or RateLimitedError."""
        now = self._clock()
        ip_hash = None if ip is None else hash_secret(ip)
        code = secrets.token_urlsafe(16)
        poll_token = secrets.token_urlsafe(32)
        try:
            link = LinkRequest.open(
                code,
                hash_secret(poll_token),
                computer.name,
                computer.os,
                now,
                installation_id=computer.installation_id,
                platform=computer.platform,
                app_version=computer.app_version,
                ip_hash=ip_hash,
            )
        except InvalidValueError as error:
            raise BadRequestError(str(error)) from error
        async with self._unit_of_work() as uow:
            # the limit looks an hour back; older codes are of no use to anyone
            await uow.links.delete_expired(now - _HOUR)
            await _keep_to_limits(uow, link, now)
            await uow.links.save(link)
            await uow.commit()
        deep_link = await self._app_links.startapp(STARTAPP_PREFIX + code)
        return NewLink(code, deep_link, poll_token, link.expires_at)

    async def poll(self, code: str, poll_token: str, wait: timedelta = timedelta(0)) -> LinkPoll:
        """Where the code is; waits up to `wait` for an answer while it is pending.
        LinkNotFoundError for an unknown code or someone else's poll token."""
        found = await self._check(code, poll_token)
        if found.status is not PollStatus.PENDING or wait <= timedelta(0):
            return found
        assert found.expires_at is not None
        left = found.expires_at - self._clock()
        await self._signals.wait(code, min(wait, MAX_WAIT, left))
        return await self._check(code, poll_token)

    async def cancel(self, code: str, poll_token: str) -> None:
        """«Отмена» on the desktop: the code is gone, the Mini App shows «Ссылка устарела»."""
        async with self._unit_of_work() as uow:
            await _own_link(uow, code, poll_token)
            await uow.links.delete(code)
            await uow.commit()
        self._signals.answered(code)

    async def linked(self, device_key: str) -> LinkedComputer:
        """The computer behind the key and its account; UnauthorizedError for a revoked key."""
        async with self._unit_of_work() as uow:
            device = await device_for_key(uow, device_key)
            return LinkedComputer(device, await _user(uow, device.user_id))

    async def unlink(self, device_key: str) -> None:
        """«Отвязать» on the desktop: the key stops working at once."""
        async with self._unit_of_work() as uow:
            device = await device_for_key(uow, device_key)
            await uow.devices.delete(device.id)
            await uow.commit()

    # The Mini App, for the user its launch data names

    async def question(self, user_id: int, code: str) -> LinkQuestion:
        """The code waiting for an answer; LinkNotFoundError once it ran out, was answered,
        cancelled or never existed (18·3)."""
        async with self._unit_of_work() as uow:
            link = await uow.links.get(code)
            if link is None or link.status is not LinkStatus.PENDING:
                raise LinkNotFoundError(code)
            if link.is_expired(self._clock()):
                raise LinkNotFoundError(code)
            here = (
                await uow.devices.list_for_installation(link.installation_id)
                if link.installation_id
                else ()
            )
        return LinkQuestion(link, any(device.user_id == user_id for device in here))

    async def confirm(self, user_id: int, code: str, notifications: bool | None = None) -> None:
        """«Привязать»; pressing it again changes nothing. `notifications` is the switch
        «Уведомления в чат», remembered for every notification of the bot; None keeps what the
        user chose before (the Mini App confirms a computer linked here without asking)."""
        now = self._clock()
        async with self._unit_of_work() as uow:
            link = await uow.links.get(code)
            if link is not None and link.is_answered_by(user_id):
                return
            confirmed = _answer(link, code, lambda found: found.confirm(user_id, now))
            await uow.links.save(confirmed)
            if notifications is not None:
                user = await _user(uow, user_id)
                await uow.users.save(user.with_notifications(notifications, now))
            await uow.commit()
        self._signals.answered(code)

    async def decline(self, user_id: int, code: str) -> None:
        """«Это не я»: the desktop hears «declined» (18·4)."""
        now = self._clock()
        async with self._unit_of_work() as uow:
            link = await uow.links.get(code)
            declined = _answer(link, code, lambda found: found.decline(user_id, now))
            await uow.links.save(declined)
            await uow.commit()
        self._signals.answered(code)

    async def _check(self, code: str, poll_token: str) -> LinkPoll:
        async with self._claiming:
            now = self._clock()
            async with self._unit_of_work() as uow:
                link = await _own_link(uow, code, poll_token)
                match link.status:
                    case LinkStatus.PENDING if not link.is_expired(now):
                        return LinkPoll(PollStatus.PENDING, expires_at=link.expires_at)
                    case LinkStatus.CONFIRMED if not link.is_expired(now):
                        key, linked = await _claim(uow, link, now)
                        await uow.commit()
                    case LinkStatus.DECLINED:
                        return LinkPoll(PollStatus.DECLINED)
                    case _:
                        return LinkPoll(PollStatus.EXPIRED)
        # after the commit: a notification never fails the linking
        await self._notifications.device_linked(linked.device)
        return LinkPoll(PollStatus.LINKED, device_key=key, linked=linked)


async def _claim(uow: UnitOfWork, link: LinkRequest, now: datetime) -> tuple[str, LinkedComputer]:
    """The device for a confirmed link, and its key. The installation's earlier device goes,
    whoever it was linked to: its key stops working and it leaves that user's «Устройства»."""
    assert link.user_id is not None, "a confirmed link knows who confirmed it"
    key = secrets.token_urlsafe(32)
    device = Device(
        id=uuid.uuid4().hex,
        user_id=link.user_id,
        name=link.device_name,
        os=link.device_os,
        key_hash=hash_secret(key),
        linked_at=now,
        installation_id=link.installation_id,
    )
    if link.installation_id:
        for earlier in await uow.devices.list_for_installation(link.installation_id):
            await uow.devices.delete(earlier.id)
    await uow.devices.save(device)
    await uow.links.save(link.claim(device.id, now))
    return key, LinkedComputer(device, await _user(uow, link.user_id))


async def _own_link(uow: UnitOfWork, code: str, poll_token: str) -> LinkRequest:
    link = await uow.links.get(code)
    # compare_digest: the time taken tells nothing about the stored hash
    if link is None or not hmac.compare_digest(link.poll_token_hash, hash_secret(poll_token)):
        raise LinkNotFoundError(code)
    return link


def _answer(
    link: LinkRequest | None, code: str, answer: Callable[[LinkRequest], LinkRequest]
) -> LinkRequest:
    if link is None:
        raise LinkNotFoundError(code)
    try:
        return answer(link)
    except DomainError as error:
        # answered, claimed or out of time: «Ссылка устарела» all the same
        raise LinkNotFoundError(code) from error


async def _user(uow: UnitOfWork, user_id: int) -> User:
    user = await uow.users.get(user_id)
    # SignIn remembered whoever confirmed; a device's user cannot be deleted
    assert user is not None, f"user {user_id} is unknown"
    return user


async def _keep_to_limits(uow: UnitOfWork, link: LinkRequest, now: datetime) -> None:
    since = now - _HOUR
    if link.installation_id is not None:
        sent = await uow.links.created_since(since, installation_id=link.installation_id)
        keep_to_limit(sent, LINKS_PER_INSTALLATION_PER_HOUR, _HOUR, now)
    if link.ip_hash is not None:
        sent = await uow.links.created_since(since, ip_hash=link.ip_hash)
        keep_to_limit(sent, LINKS_PER_IP_PER_HOUR, _HOUR, now)
