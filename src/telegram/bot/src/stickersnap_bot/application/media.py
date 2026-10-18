"""Short-lived signed links to sticker files, so <img> and <video> load them without a header.

The browser never sees a Telegram file URL: that one carries the bot's token. A link stays the
same for a whole period, so the browser's cache keeps working while the grid refetches.
"""

import hashlib
import hmac
from datetime import UTC, datetime, timedelta

_SIGNATURE_LENGTH = 32


class MediaLinks:
    def __init__(self, secret: str, period: timedelta = timedelta(hours=1)) -> None:
        self._key = hmac.new(secret.encode(), b"media-links", hashlib.sha256).digest()
        self._period = int(period.total_seconds())

    def token(self, file_id: str, now: datetime) -> str:
        """`<file_id>.<expires>.<signature>`; it lives one to two periods."""
        expires = (int(now.timestamp()) // self._period + 2) * self._period
        return f"{file_id}.{expires}.{self._sign(file_id, expires)}"

    def open(self, token: str, now: datetime) -> tuple[str, datetime] | None:
        """The file id and when the link expires, or None for a forged or expired link."""
        file_id, _, rest = token.partition(".")
        expires_text, _, signature = rest.partition(".")
        if not file_id or not expires_text.isdigit():
            return None
        expires = int(expires_text)
        if not hmac.compare_digest(signature, self._sign(file_id, expires)):
            return None
        expires_at = datetime.fromtimestamp(expires, UTC)
        return (file_id, expires_at) if now < expires_at else None

    def _sign(self, file_id: str, expires: int) -> str:
        message = f"{file_id}.{expires}".encode()
        return hmac.new(self._key, message, hashlib.sha256).hexdigest()[:_SIGNATURE_LENGTH]
