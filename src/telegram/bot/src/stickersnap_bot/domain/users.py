from dataclasses import dataclass, replace
from datetime import datetime


@dataclass(frozen=True)
class TelegramProfile:
    """What Telegram tells about a user in a message or in the Mini App's initData."""

    first_name: str
    last_name: str | None = None
    username: str | None = None
    language_code: str | None = None


@dataclass(frozen=True)
class User:
    """A Telegram user who started the bot or opened the Mini App; the id is Telegram's user id."""

    id: int
    profile: TelegramProfile
    created_at: datetime
    updated_at: datetime
    # the user stopped the bot (my_chat_member): desktops get bot_blocked with no Bot API call
    bot_blocked: bool = False
    # «Уведомления в чат» when confirming a link: off, the bot writes nothing by itself
    notifications: bool = True

    @classmethod
    def first_seen(cls, user_id: int, profile: TelegramProfile, now: datetime) -> "User":
        return cls(id=user_id, profile=profile, created_at=now, updated_at=now)

    def with_profile(self, profile: TelegramProfile, now: datetime) -> "User":
        if profile == self.profile:
            return self
        return replace(self, profile=profile, updated_at=now)

    def with_bot_blocked(self, blocked: bool, now: datetime) -> "User":
        if blocked == self.bot_blocked:
            return self
        return replace(self, bot_blocked=blocked, updated_at=now)

    def with_notifications(self, on: bool, now: datetime) -> "User":
        if on == self.notifications:
            return self
        return replace(self, notifications=on, updated_at=now)
