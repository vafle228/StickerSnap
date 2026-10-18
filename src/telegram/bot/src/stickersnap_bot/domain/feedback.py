from dataclasses import dataclass, replace
from datetime import datetime
from enum import StrEnum

from stickersnap_bot.domain.errors import InvalidValueError

FEEDBACK_TEXT_MAX_LENGTH = 2000
CONTACT_MAX_LENGTH = 100
# the desktop's diagnostics package (F4), measured in UTF-8
DIAGNOSTICS_MAX_BYTES = 200 * 1024


class FeedbackType(StrEnum):
    BUG = "bug"
    IDEA = "idea"
    OTHER = "other"


class FeedbackSource(StrEnum):
    DESKTOP = "desktop"
    MINIAPP = "miniapp"


@dataclass(frozen=True)
class ReportedError:
    """The error the desktop's «Сообщить» button was pressed on."""

    code: str
    trace_id: str | None = None


@dataclass(frozen=True)
class Author:
    """Who sent the feedback: a Telegram account (from the Mini App or a linked computer) or an
    installation of the desktop that is not linked."""

    source: FeedbackSource
    user_id: int | None = None
    device_id: str | None = None
    installation_id: str | None = None
    # only a hash of the caller's IP is kept, for the limit on calls without a link
    ip_hash: str | None = None

    @property
    def is_anonymous(self) -> bool:
        return self.user_id is None


@dataclass(frozen=True)
class Feedback:
    """A bug report, an idea or anything else, as «Написать отзыв» sent it.

    The id comes from the database; forwarded_at stays empty while Telegram has not taken it to
    the feedback chat.
    """

    type: FeedbackType
    text: str
    author: Author
    can_reply: bool
    created_at: datetime
    contact: str | None = None
    app_version: str | None = None
    platform: str | None = None
    error: ReportedError | None = None
    diagnostics: str | None = None
    id: int | None = None
    forwarded_at: datetime | None = None

    @classmethod
    def create(
        cls,
        type: FeedbackType,
        text: str,
        author: Author,
        can_reply: bool,
        now: datetime,
        *,
        contact: str | None = None,
        app_version: str | None = None,
        platform: str | None = None,
        error: ReportedError | None = None,
        diagnostics: str | None = None,
    ) -> "Feedback":
        """The rules of the contract («Облачный сервис · Отзывы») the schema cannot say."""
        text = text.strip()
        if len(text) > FEEDBACK_TEXT_MAX_LENGTH:
            raise InvalidValueError(f"text is longer than {FEEDBACK_TEXT_MAX_LENGTH} characters")
        if author.is_anonymous and author.installation_id is None:
            raise InvalidValueError("feedback without a link needs installationId")
        if type is not FeedbackType.BUG and (error is not None or diagnostics is not None):
            raise InvalidValueError("only a bug report carries an error and diagnostics")
        # «Из ошибки текст можно не писать»: the error card says what happened
        if not text and error is None:
            raise InvalidValueError("text is empty")
        if diagnostics is not None:
            if author.source is not FeedbackSource.DESKTOP:
                raise InvalidValueError("only the desktop sends diagnostics")
            if len(diagnostics.encode()) > DIAGNOSTICS_MAX_BYTES:
                raise InvalidValueError(
                    f"diagnostics are larger than {DIAGNOSTICS_MAX_BYTES} bytes"
                )
        if contact is not None:
            contact = contact.strip()
            # a linked caller is answered in Telegram, the contact is for the rest
            if not author.is_anonymous or not can_reply:
                raise InvalidValueError("contact goes only without a link and with canReply")
            if not contact or len(contact) > CONTACT_MAX_LENGTH:
                raise InvalidValueError(f"contact must be 1..{CONTACT_MAX_LENGTH} characters")
        return cls(
            type=type,
            text=text,
            author=author,
            can_reply=can_reply,
            created_at=now,
            contact=contact,
            app_version=app_version,
            platform=platform,
            error=error,
            diagnostics=diagnostics,
        )

    def forwarded(self, now: datetime) -> "Feedback":
        return replace(self, forwarded_at=now)
