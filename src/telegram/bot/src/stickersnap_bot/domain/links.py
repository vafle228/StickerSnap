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
    # the desktop waits, the user has not answered in the Mini App yet
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
    Mini App (startapp=link_<code>), then the desktop claims the link once and gets a device key.
    A code that is neither declined nor claimed expires after LINK_LIFETIME.

    The installation id tells the same computer when it links again; the platform and the app
    version are only shown on the confirmation screen.
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
    installation_id: str | None = None
    platform: str | None = None
    app_version: str | None = None
    # the address the code was asked from, hashed: the service's limit counts by it
    ip_hash: str | None = None

    @classmethod
    def open(
        cls,
        code: str,
        poll_token_hash: str,
        name: str,
        os: DeviceOs,
        now: datetime,
        *,
        installation_id: str | None = None,
        platform: str | None = None,
        app_version: str | None = None,
        ip_hash: str | None = None,
    ) -> "LinkRequest":
        return cls(
            code=code,
            poll_token_hash=poll_token_hash,
            device_name=device_name(name),
            device_os=os,
            created_at=now,
            expires_at=now + LINK_LIFETIME,
            installation_id=installation_id,
            platform=platform,
            app_version=app_version,
            ip_hash=ip_hash,
        )

    def is_expired(self, now: datetime) -> bool:
        return now >= self.expires_at

    def is_answered_by(self, user_id: int) -> bool:
        """The user already pressed «Привязать» on this code: pressing again changes nothing."""
        confirmed = self.status in (LinkStatus.CONFIRMED, LinkStatus.CLAIMED)
        return confirmed and self.user_id == user_id

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
