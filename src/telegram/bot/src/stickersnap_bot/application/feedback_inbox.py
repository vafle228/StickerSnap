"""Port to the place the developer reads feedback: the feedback chat in Telegram (FEEDBACK_CHAT_ID).

The adapter raises RateLimitedError and TelegramUnavailableError from application.errors.
"""

from typing import Protocol

from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.feedback import Feedback
from stickersnap_bot.domain.users import User


class FeedbackInbox(Protocol):
    async def deliver(self, feedback: Feedback, user: User | None, device: Device | None) -> None:
        """Hands stored feedback (with its id) over, with its diagnostics as a file. `user` is the
        Telegram account behind it, `device` the linked computer it came from; both None without a
        link."""
        ...
