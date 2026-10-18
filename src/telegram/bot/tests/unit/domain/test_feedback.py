from typing import Any

import pytest

from stickersnap_bot.domain.errors import InvalidValueError
from stickersnap_bot.domain.feedback import (
    DIAGNOSTICS_MAX_BYTES,
    Author,
    Feedback,
    FeedbackSource,
    FeedbackType,
    ReportedError,
)
from tests.fakes import NOW

LINKED = Author(FeedbackSource.DESKTOP, user_id=42, device_id="pc")
MINIAPP = Author(FeedbackSource.MINIAPP, user_id=42)
ANONYMOUS = Author(FeedbackSource.DESKTOP, installation_id="install-1")
RENDER = ReportedError("render_failed", "7f3a-c21e")


def create(
    type: FeedbackType = FeedbackType.IDEA,
    text: str = "Хочу менять порядок стикеров",
    author: Author = LINKED,
    can_reply: bool = True,
    **rest: Any,
) -> Feedback:
    return Feedback.create(type, text, author, can_reply, NOW, **rest)


def test_new_feedback_has_no_id_and_is_not_forwarded() -> None:
    feedback = create(text="  Идея  ")

    assert feedback.text == "Идея"
    assert feedback.id is None
    assert feedback.forwarded_at is None
    assert feedback.forwarded(NOW).forwarded_at == NOW


def test_a_bug_from_an_error_may_have_no_text() -> None:
    feedback = create(FeedbackType.BUG, "", error=RENDER, diagnostics="log")

    assert feedback.text == ""
    assert feedback.error == RENDER


def test_the_diagnostics_limit_counts_utf8_bytes() -> None:
    fits = "я" * (DIAGNOSTICS_MAX_BYTES // 2)

    assert create(FeedbackType.BUG, diagnostics=fits).diagnostics == fits
    with pytest.raises(InvalidValueError):
        create(FeedbackType.BUG, diagnostics=fits + "я")


def test_without_a_link_the_contact_is_kept() -> None:
    feedback = create(author=ANONYMOUS, contact=" me@example.com ")

    assert feedback.contact == "me@example.com"


@pytest.mark.parametrize(
    "rules_broken",
    [
        {"text": "   "},
        {"text": "x" * 2001},
        {"author": Author(FeedbackSource.DESKTOP)},
        {"type": FeedbackType.IDEA, "error": RENDER},
        {"type": FeedbackType.OTHER, "diagnostics": "log"},
        {"type": FeedbackType.BUG, "author": MINIAPP, "diagnostics": "log"},
        {"contact": "me@example.com"},
        {"author": ANONYMOUS, "can_reply": False, "contact": "me@example.com"},
        {"author": ANONYMOUS, "contact": "  "},
        {"author": ANONYMOUS, "contact": "x" * 101},
    ],
)
def test_breaking_a_rule_is_refused(rules_broken: dict[str, Any]) -> None:
    with pytest.raises(InvalidValueError):
        create(**rules_broken)
