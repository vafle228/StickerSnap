import json
from datetime import timedelta

import pytest

from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.application.init_data import InitDataVerifier, sign_init_data
from tests.fakes import NOW

TOKEN = "123:secret"
USER = {"id": 42, "first_name": "Дима", "username": "dima", "language_code": "ru"}


def launch(token: str = TOKEN, age: timedelta = timedelta(minutes=1), **extra: str) -> str:
    fields = {
        "query_id": "AAH",
        "user": json.dumps(USER, ensure_ascii=False),
        "auth_date": str(int((NOW - age).timestamp())),
        **extra,
    }
    return sign_init_data(token, fields)


def verifier() -> InitDataVerifier:
    return InitDataVerifier(TOKEN, max_age=timedelta(days=1))


def test_takes_the_user_from_signed_launch_data() -> None:
    user = verifier().verify(launch(), NOW)

    assert user.id == 42
    assert user.profile.first_name == "Дима"
    assert user.profile.username == "dima"
    assert user.profile.language_code == "ru"


def test_refuses_data_signed_with_another_token() -> None:
    with pytest.raises(UnauthorizedError):
        verifier().verify(launch(token="456:other"), NOW)


def test_refuses_data_changed_after_signing() -> None:
    forged = launch().replace("%22id%22%3A+42", "%22id%22%3A+43")
    assert forged != launch()

    with pytest.raises(UnauthorizedError):
        verifier().verify(forged, NOW)


@pytest.mark.parametrize("init_data", ["", "hash=", "query_id=AAH", "garbage"])
def test_refuses_data_without_a_signature(init_data: str) -> None:
    with pytest.raises(UnauthorizedError):
        verifier().verify(init_data, NOW)


def test_refuses_data_older_than_max_age() -> None:
    with pytest.raises(UnauthorizedError, match="too old"):
        verifier().verify(launch(age=timedelta(days=1, minutes=1)), NOW)


def test_refuses_data_from_the_future_beyond_clock_skew() -> None:
    with pytest.raises(UnauthorizedError):
        verifier().verify(launch(age=-timedelta(hours=1)), NOW)


def test_refuses_signed_data_without_a_user() -> None:
    fields = {"auth_date": str(int(NOW.timestamp()))}

    with pytest.raises(UnauthorizedError):
        verifier().verify(sign_init_data(TOKEN, fields), NOW)
