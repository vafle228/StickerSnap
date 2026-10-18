"""The service's own limits: so many requests per caller within a window, counted from the rows
the requests left behind (feedback, link codes)."""

import math
from collections.abc import Sequence
from datetime import datetime, timedelta

from stickersnap_bot.application.errors import RateLimitedError


def keep_to_limit(sent: Sequence[datetime], limit: int, window: timedelta, now: datetime) -> None:
    """RateLimitedError, saying how long to wait, when `sent` (oldest first, all within the
    window) already holds `limit` requests."""
    if len(sent) < limit:
        return
    # one more is allowed once this one leaves the window
    frees_at = sent[len(sent) - limit] + window
    raise RateLimitedError(max(1, math.ceil((frees_at - now).total_seconds())))
