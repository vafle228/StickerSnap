from datetime import UTC, datetime, timedelta

import pytest

from stickersnap_bot.domain.devices import DeviceOs
from stickersnap_bot.domain.errors import (
    InvalidValueError,
    LinkExpiredError,
    LinkNotConfirmedError,
    LinkNotPendingError,
)
from stickersnap_bot.domain.links import LINK_LIFETIME, LinkRequest, LinkStatus

NOW = datetime(2026, 10, 18, 9, 0, tzinfo=UTC)
LATER = NOW + timedelta(minutes=1)
TOO_LATE = NOW + LINK_LIFETIME


def open_link() -> LinkRequest:
    return LinkRequest.open("code", "poll-hash", "  DESKTOP-ABC ", DeviceOs.WINDOWS, NOW)


def test_opens_pending_for_ten_minutes_with_the_name_trimmed() -> None:
    link = open_link()

    assert link.status is LinkStatus.PENDING
    assert link.expires_at == NOW + timedelta(minutes=10)
    assert link.device_name == "DESKTOP-ABC"


def test_refuses_an_empty_device_name() -> None:
    with pytest.raises(InvalidValueError):
        LinkRequest.open("code", "poll-hash", "   ", DeviceOs.LINUX, NOW)


def test_confirm_then_claim_gives_the_device() -> None:
    link = open_link().confirm(42, LATER).claim("device-1", LATER)

    assert link.status is LinkStatus.CLAIMED
    assert link.user_id == 42
    assert link.device_id == "device-1"


def test_decline_remembers_who_declined() -> None:
    link = open_link().decline(7, LATER)

    assert link.status is LinkStatus.DECLINED
    assert link.user_id == 7


def test_expires_at_ten_minutes() -> None:
    link = open_link()

    assert not link.is_expired(TOO_LATE - timedelta(microseconds=1))
    assert link.is_expired(TOO_LATE)


def test_an_expired_code_cannot_be_confirmed() -> None:
    with pytest.raises(LinkExpiredError):
        open_link().confirm(42, TOO_LATE)


def test_a_confirmed_code_cannot_be_confirmed_again() -> None:
    # someone else pressing the same link must not take the computer over
    with pytest.raises(LinkNotPendingError):
        open_link().confirm(42, LATER).confirm(13, LATER)


def test_a_declined_code_cannot_be_confirmed() -> None:
    with pytest.raises(LinkNotPendingError):
        open_link().decline(42, LATER).confirm(42, LATER)


def test_a_pending_code_cannot_be_claimed() -> None:
    with pytest.raises(LinkNotConfirmedError):
        open_link().claim("device-1", LATER)


def test_a_code_is_claimed_only_once() -> None:
    claimed = open_link().confirm(42, LATER).claim("device-1", LATER)

    with pytest.raises(LinkNotConfirmedError):
        claimed.claim("device-2", LATER)


def test_a_confirmed_code_left_unclaimed_expires() -> None:
    with pytest.raises(LinkExpiredError):
        open_link().confirm(42, LATER).claim("device-1", TOO_LATE)
