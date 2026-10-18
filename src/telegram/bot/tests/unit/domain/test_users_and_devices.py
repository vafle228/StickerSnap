from datetime import UTC, datetime, timedelta

import pytest

from stickersnap_bot.domain.devices import DEVICE_NAME_MAX_LENGTH, device_name
from stickersnap_bot.domain.errors import InvalidValueError
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import TelegramProfile, User

NOW = datetime(2026, 10, 18, 9, 0, tzinfo=UTC)
LATER = NOW + timedelta(hours=1)
ANNA = TelegramProfile("Anna", username="anna")


def test_a_new_user_is_not_blocked() -> None:
    user = User.first_seen(42, ANNA, NOW)

    assert not user.bot_blocked
    assert user.created_at == user.updated_at == NOW


def test_a_changed_profile_moves_updated_at() -> None:
    user = User.first_seen(42, ANNA, NOW).with_profile(TelegramProfile("Anna", "K."), LATER)

    assert user.profile.last_name == "K."
    assert user.updated_at == LATER


def test_the_same_profile_changes_nothing() -> None:
    user = User.first_seen(42, ANNA, NOW)

    assert user.with_profile(TelegramProfile("Anna", username="anna"), LATER) is user


def test_blocking_the_bot_is_remembered() -> None:
    user = User.first_seen(42, ANNA, NOW).with_bot_blocked(True, LATER)

    assert user.bot_blocked
    assert user.updated_at == LATER
    assert user.with_bot_blocked(True, LATER) is user


def test_notifications_are_on_until_turned_off() -> None:
    user = User.first_seen(42, ANNA, NOW)
    quiet = user.with_notifications(False, LATER)

    assert user.notifications
    assert not quiet.notifications
    assert quiet.updated_at == LATER
    assert quiet.with_notifications(False, LATER) is quiet


def test_device_name_is_trimmed_and_limited() -> None:
    assert device_name(" MacBook ") == "MacBook"
    assert device_name("x" * DEVICE_NAME_MAX_LENGTH) == "x" * DEVICE_NAME_MAX_LENGTH
    with pytest.raises(InvalidValueError):
        device_name("x" * (DEVICE_NAME_MAX_LENGTH + 1))


def test_secret_hash_is_stable_and_hides_the_secret() -> None:
    assert hash_secret("key") == hash_secret("key")
    assert hash_secret("key") != hash_secret("other")
    assert "key" not in hash_secret("key")
