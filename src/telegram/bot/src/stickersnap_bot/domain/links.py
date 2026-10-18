from dataclasses import dataclass, replace
from datetime import datetime, timedelta
from enum import StrEnum

from stickersnap_bot.domain.devices import DeviceOs, device_name
from stickersnap_bot.domain.errors import (
    LinkExpiredError,
    LinkNotConfirmedError,
    LinkNotPendingError,
)

LINK_LIFETIME = timedelta(minutes=10)


class LinkStatus(StrEnum):
    # the desktop waits, the user has not answered in the chat yet
    PENDING = "pending"
    # the user pressed «Привязать»; the desktop has not taken its key yet
    CONFIRMED = "confirmed"
    # the user pressed «Это не я»
    DECLINED = "declined"
    # the desktop took its key: the link became a device
    CLAIMED = "claimed"


@dataclass(frozen=True)
class LinkRequest:
    """A one-time code a desktop asked for to link itself (bot plan, «Привязка»).

    The desktop polls with a secret poll token (only its hash is kept). The user confirms in the
    chat, then the desktop claims the link once and gets a device key. A code that is neither
    declined nor claimed expires after LINK_LIFETIME.
    """

    code: str
    poll_token_hash: str
    device_name: str
    device_os: DeviceOs
    created_at: datetime
    expires_at: datetime
    status: LinkStatus = LinkStatus.PENDING
    # who confirmed or declined
    user_id: int | None = None
    # the device the claim created
    device_id: str | None = None

    @classmethod
    def open(
        cls,
        code: str,
        poll_token_hash: str,
        name: str,
        os: DeviceOs,
        now: datetime,
    ) -> "LinkRequest":
        return cls(
            code=code,
            poll_token_hash=poll_token_hash,
            device_name=device_name(name),
            device_os=os,
            created_at=now,
            expires_at=now + LINK_LIFETIME,
        )

    def is_expired(self, now: datetime) -> bool:
        return now >= self.expires_at

    def confirm(self, user_id: int, now: datetime) -> "LinkRequest":
        self._ensure_pending(now)
        return replace(self, status=LinkStatus.CONFIRMED, user_id=user_id)

    def decline(self, user_id: int, now: datetime) -> "LinkRequest":
        self._ensure_pending(now)
        return replace(self, status=LinkStatus.DECLINED, user_id=user_id)

    def claim(self, device_id: str, now: datetime) -> "LinkRequest":
        if self.status is not LinkStatus.CONFIRMED:
            raise LinkNotConfirmedError(self.code)
        if self.is_expired(now):
            raise LinkExpiredError(self.code)
        return replace(self, status=LinkStatus.CLAIMED, device_id=device_id)

    def _ensure_pending(self, now: datetime) -> None:
        if self.status is not LinkStatus.PENDING:
            raise LinkNotPendingError(self.code)
        if self.is_expired(now):
            raise LinkExpiredError(self.code)
