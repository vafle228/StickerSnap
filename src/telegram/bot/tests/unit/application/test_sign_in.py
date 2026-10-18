from datetime import timedelta

import pytest

from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import NOW, Tables, sign_in, signed_launch

ME = 42


async def test_remembers_a_new_user() -> None:
    tables = Tables()

    assert await sign_in(tables)(signed_launch(ME)) == ME

    assert tables.users[ME].profile == TelegramProfile(first_name="Дима")


async def test_updates_a_changed_profile() -> None:
    earlier = NOW - timedelta(days=3)
    tables = Tables(users={ME: User.first_seen(ME, TelegramProfile(first_name="Old"), earlier)})

    await sign_in(tables)(signed_launch(ME, "New"))

    assert tables.users[ME].profile.first_name == "New"
    assert tables.users[ME].created_at == earlier


async def test_refuses_unsigned_data_and_remembers_no_one() -> None:
    tables = Tables()

    with pytest.raises(UnauthorizedError):
        await sign_in(tables)("user=%7B%22id%22%3A42%7D&hash=forged")

    assert tables.users == {}
