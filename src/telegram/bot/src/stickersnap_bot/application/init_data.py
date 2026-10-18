"""Checks the Mini App's launch data (Telegram.WebApp.initData) the way Telegram describes it:
https://core.telegram.org/bots/webapps#validating-data-received-via-the-mini-app"""

import hashlib
import hmac
import json
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from urllib.parse import parse_qsl, urlencode

from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.domain.users import TelegramProfile

# launch data a little ahead of our clock is the clocks disagreeing, not a forgery
_CLOCK_SKEW = timedelta(minutes=5)


@dataclass(frozen=True)
class LaunchUser:
    id: int
    profile: TelegramProfile


class InitDataVerifier:
    def __init__(self, bot_token: str, max_age: timedelta) -> None:
        self._key = _secret_key(bot_token)
        self._max_age = max_age

    def verify(self, init_data: str, now: datetime) -> LaunchUser:
        fields = dict(parse_qsl(init_data, keep_blank_values=True))
        given = fields.pop("hash", "")
        if not given or not hmac.compare_digest(given, _digest(self._key, fields)):
            raise UnauthorizedError("initData is not signed by the bot")
        try:
            signed_at = datetime.fromtimestamp(int(fields["auth_date"]), UTC)
            user = json.loads(fields["user"])
            launch = LaunchUser(
                id=int(user["id"]),
                profile=TelegramProfile(
                    first_name=str(user.get("first_name", "")),
                    last_name=user.get("last_name"),
                    username=user.get("username"),
                    language_code=user.get("language_code"),
                ),
            )
        except (KeyError, ValueError, TypeError) as error:
            raise UnauthorizedError("initData has no auth_date or user") from error
        if not now - self._max_age <= signed_at <= now + _CLOCK_SKEW:
            raise UnauthorizedError("initData is too old")
        return launch


def sign_init_data(bot_token: str, fields: dict[str, str]) -> str:
    """Launch data as Telegram would sign it: for tests and for trying the Mini App in a plain
    browser against a local service."""
    return urlencode({**fields, "hash": _digest(_secret_key(bot_token), fields)})


def _secret_key(bot_token: str) -> bytes:
    # HMAC-SHA256 of the token with "WebAppData" as the key
    return hmac.new(b"WebAppData", bot_token.encode(), hashlib.sha256).digest()


def _digest(key: bytes, fields: dict[str, str]) -> str:
    # every field but hash, sorted by name, as name=value lines
    check = "\n".join(f"{name}={fields[name]}" for name in sorted(fields))
    return hmac.new(key, check.encode(), hashlib.sha256).hexdigest()
