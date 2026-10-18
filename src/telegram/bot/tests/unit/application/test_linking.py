import asyncio
from datetime import timedelta

import pytest

from stickersnap_bot.application.device_keys import device_for_key
from stickersnap_bot.application.errors import (
    BadRequestError,
    LinkNotFoundError,
    RateLimitedError,
    UnauthorizedError,
)
from stickersnap_bot.application.linking import (
    LINKS_PER_INSTALLATION_PER_HOUR,
    LINKS_PER_IP_PER_HOUR,
    Computer,
    Linking,
    LinkSignals,
    PollStatus,
)
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.links import LINK_LIFETIME, LinkStatus
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import NOW, FakeChatMessages, FakeClock, Tables, fake_unit_of_work, linking

ME, SOMEONE = 42, 7
PC = Computer(
    "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, "inst-1", platform="Windows 11", app_version="0.3.1"
)
LINKED_TITLE = "Компьютер привязан"


class Service:
    """Linking over in-memory tables, with the users the Mini App has signed in."""

    def __init__(self) -> None:
        self.tables = Tables(
            users={
                ME: User.first_seen(ME, TelegramProfile("Дима", username="dima"), NOW),
                SOMEONE: User.first_seen(SOMEONE, TelegramProfile("Аня"), NOW),
            }
        )
        self.messages = FakeChatMessages()
        self.clock = FakeClock()
        self.signals = LinkSignals()
        self.linking: Linking = linking(self.tables, self.messages, self.clock, self.signals)

    async def key_owner(self, key: str) -> Device:
        async with fake_unit_of_work(self.tables)() as uow:
            return await device_for_key(uow, key)


@pytest.fixture
def service() -> Service:
    return Service()


async def test_a_new_code_opens_the_mini_app_and_waits(service: Service) -> None:
    link = await service.linking.create(PC, ip="10.0.0.1")

    assert link.deep_link == f"https://t.me/StickerSnapAppBot?startapp=link_{link.code}"
    assert link.expires_at == NOW + LINK_LIFETIME
    stored = service.tables.links[link.code]
    assert stored.poll_token_hash == hash_secret(link.poll_token)
    assert stored.ip_hash == hash_secret("10.0.0.1")
    assert (stored.installation_id, stored.platform, stored.app_version) == (
        "inst-1",
        "Windows 11",
        "0.3.1",
    )
    poll = await service.linking.poll(link.code, link.poll_token)
    assert (poll.status, poll.expires_at) == (PollStatus.PENDING, link.expires_at)


async def test_codes_and_poll_tokens_are_long_and_never_repeat(service: Service) -> None:
    first = await service.linking.create(PC)
    second = await service.linking.create(PC)

    assert len(first.code) >= 22
    assert len(first.poll_token) >= 43
    assert first.code != second.code
    assert first.poll_token != second.poll_token


async def test_a_blank_name_is_a_bad_request(service: Service) -> None:
    with pytest.raises(BadRequestError):
        await service.linking.create(Computer("  ", DeviceOs.LINUX, "inst-1"))


async def test_confirming_hands_the_key_out_once(service: Service) -> None:
    link = await service.linking.create(PC)

    await service.linking.confirm(ME, link.code, notifications=True)
    poll = await service.linking.poll(link.code, link.poll_token)

    assert poll.status is PollStatus.LINKED
    assert poll.device_key is not None
    assert poll.linked is not None
    assert poll.linked.user.id == ME
    device = await service.key_owner(poll.device_key)
    assert device == poll.linked.device
    assert (device.user_id, device.name, device.os, device.installation_id) == (
        ME,
        "DESKTOP-7F3K2Q",
        DeviceOs.WINDOWS,
        "inst-1",
    )
    assert device.key_hash == hash_secret(poll.device_key)
    again = await service.linking.poll(link.code, link.poll_token)
    assert again.status is PollStatus.EXPIRED
    assert again.device_key is None


async def test_the_bot_says_the_computer_is_linked(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)

    await service.linking.poll(link.code, link.poll_token)

    assert [s.message.title for s in service.messages.sent] == [LINKED_TITLE]


async def test_notifications_switched_off_keep_the_bot_quiet(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=False)

    poll = await service.linking.poll(link.code, link.poll_token)

    assert poll.status is PollStatus.LINKED
    assert not service.tables.users[ME].notifications
    assert service.messages.sent == []


async def test_confirming_without_the_switch_keeps_the_users_choice(service: Service) -> None:
    muted = await service.linking.create(PC)
    await service.linking.confirm(ME, muted.code, notifications=False)
    await service.linking.poll(muted.code, muted.poll_token)

    again = await service.linking.create(PC)
    await service.linking.confirm(ME, again.code)
    poll = await service.linking.poll(again.code, again.poll_token)

    assert poll.status is PollStatus.LINKED
    assert not service.tables.users[ME].notifications
    assert service.messages.sent == []


async def test_a_bot_that_cannot_write_does_not_stop_the_linking(service: Service) -> None:
    service.messages.refused.add(ME)
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)

    assert (await service.linking.poll(link.code, link.poll_token)).status is PollStatus.LINKED


async def test_declining_tells_the_desktop(service: Service) -> None:
    link = await service.linking.create(PC)

    await service.linking.decline(ME, link.code)

    assert (await service.linking.poll(link.code, link.poll_token)).status is PollStatus.DECLINED
    assert service.tables.devices == {}


async def test_a_code_left_alone_expires(service: Service) -> None:
    link = await service.linking.create(PC)
    service.clock.advance(LINK_LIFETIME)

    assert (await service.linking.poll(link.code, link.poll_token)).status is PollStatus.EXPIRED
    with pytest.raises(LinkNotFoundError):
        await service.linking.confirm(ME, link.code, notifications=True)


async def test_a_confirmed_code_not_taken_in_time_expires(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)
    service.clock.advance(LINK_LIFETIME)

    assert (await service.linking.poll(link.code, link.poll_token)).status is PollStatus.EXPIRED
    assert service.tables.devices == {}


@pytest.mark.parametrize("token", ["", "someone-elses-token"])
async def test_a_foreign_poll_token_finds_nothing(service: Service, token: str) -> None:
    link = await service.linking.create(PC)

    with pytest.raises(LinkNotFoundError):
        await service.linking.poll(link.code, token)
    with pytest.raises(LinkNotFoundError):
        await service.linking.cancel(link.code, token)


async def test_an_unknown_code_finds_nothing(service: Service) -> None:
    with pytest.raises(LinkNotFoundError):
        await service.linking.poll("nope", "token")
    with pytest.raises(LinkNotFoundError):
        await service.linking.question(ME, "nope")
    with pytest.raises(LinkNotFoundError):
        await service.linking.confirm(ME, "nope", notifications=True)
    with pytest.raises(LinkNotFoundError):
        await service.linking.decline(ME, "nope")


async def test_a_cancelled_code_is_gone_for_the_mini_app(service: Service) -> None:
    link = await service.linking.create(PC)

    await service.linking.cancel(link.code, link.poll_token)

    with pytest.raises(LinkNotFoundError):
        await service.linking.question(ME, link.code)


async def test_the_mini_app_sees_what_the_desktop_sent(service: Service) -> None:
    link = await service.linking.create(PC)
    service.clock.advance(timedelta(minutes=2))

    question = await service.linking.question(ME, link.code)

    assert question.link.device_name == "DESKTOP-7F3K2Q"
    assert question.link.created_at == NOW
    assert not question.linked_here


async def test_an_answered_code_is_no_question_any_more(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.decline(ME, link.code)

    with pytest.raises(LinkNotFoundError):
        await service.linking.question(ME, link.code)


async def test_pressing_link_twice_is_fine_but_nobody_else_may(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)

    await service.linking.confirm(ME, link.code, notifications=True)
    with pytest.raises(LinkNotFoundError):
        await service.linking.confirm(SOMEONE, link.code, notifications=True)
    with pytest.raises(LinkNotFoundError):
        await service.linking.decline(ME, link.code)
    assert service.tables.links[link.code].user_id == ME


async def test_linking_the_same_computer_again_replaces_its_device(service: Service) -> None:
    first = await service.linking.create(PC)
    await service.linking.confirm(ME, first.code, notifications=True)
    old = await service.linking.poll(first.code, first.poll_token)
    second = await service.linking.create(PC)

    assert (await service.linking.question(ME, second.code)).linked_here
    assert not (await service.linking.question(SOMEONE, second.code)).linked_here
    await service.linking.confirm(ME, second.code, notifications=True)
    new = await service.linking.poll(second.code, second.poll_token)

    assert old.device_key is not None
    assert new.device_key is not None
    assert [d.user_id for d in service.tables.devices.values()] == [ME]
    with pytest.raises(UnauthorizedError):
        await service.key_owner(old.device_key)
    assert (await service.key_owner(new.device_key)).user_id == ME


async def test_a_computer_linked_by_someone_else_moves_to_you(service: Service) -> None:
    theirs = await service.linking.create(PC)
    await service.linking.confirm(SOMEONE, theirs.code, notifications=True)
    await service.linking.poll(theirs.code, theirs.poll_token)
    mine = await service.linking.create(PC)

    await service.linking.confirm(ME, mine.code, notifications=True)
    await service.linking.poll(mine.code, mine.poll_token)

    assert [d.user_id for d in service.tables.devices.values()] == [ME]


async def test_other_computers_stay_linked(service: Service) -> None:
    laptop = Computer("Laptop", DeviceOs.LINUX, "inst-2")
    for computer in (PC, laptop):
        link = await service.linking.create(computer)
        await service.linking.confirm(ME, link.code, notifications=True)
        await service.linking.poll(link.code, link.poll_token)

    assert sorted(d.name for d in service.tables.devices.values()) == ["DESKTOP-7F3K2Q", "Laptop"]


async def test_the_key_tells_the_desktop_who_it_is_and_unlinks_it(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)
    key = (await service.linking.poll(link.code, link.poll_token)).device_key
    assert key is not None

    linked = await service.linking.linked(key)
    await service.linking.unlink(key)

    assert (linked.user.profile.username, linked.device.name) == ("dima", "DESKTOP-7F3K2Q")
    assert service.tables.devices == {}
    with pytest.raises(UnauthorizedError):
        await service.linking.linked(key)
    with pytest.raises(UnauthorizedError):
        await service.linking.unlink(key)


async def test_a_waiting_desktop_wakes_up_on_the_answer(service: Service) -> None:
    link = await service.linking.create(PC)

    async def answer() -> None:
        await asyncio.sleep(0.05)
        await service.linking.confirm(ME, link.code, notifications=True)

    polled, _ = await asyncio.wait_for(
        asyncio.gather(
            service.linking.poll(link.code, link.poll_token, timedelta(seconds=20)), answer()
        ),
        timeout=5,
    )

    assert polled.status is PollStatus.LINKED


async def test_a_wait_without_an_answer_ends_pending(service: Service) -> None:
    link = await service.linking.create(PC)

    poll = await service.linking.poll(link.code, link.poll_token, timedelta(milliseconds=50))

    assert poll.status is PollStatus.PENDING


async def test_two_polls_at_once_take_one_key(service: Service) -> None:
    link = await service.linking.create(PC)
    await service.linking.confirm(ME, link.code, notifications=True)

    polls = await asyncio.gather(
        *(service.linking.poll(link.code, link.poll_token) for _ in range(2))
    )

    assert sorted(p.status for p in polls) == [PollStatus.EXPIRED, PollStatus.LINKED]
    assert len(service.tables.devices) == 1


async def test_an_installation_gets_ten_codes_an_hour(service: Service) -> None:
    for n in range(LINKS_PER_INSTALLATION_PER_HOUR):
        await service.linking.create(PC, ip=f"10.0.0.{n}")
        service.clock.advance(timedelta(minutes=1))

    with pytest.raises(RateLimitedError) as refused:
        await service.linking.create(PC, ip="10.0.1.1")

    # the first code leaves the hour 50 minutes from now
    assert refused.value.retry_after_sec == 50 * 60
    await service.linking.create(Computer("Other", DeviceOs.LINUX, "inst-2"), ip="10.0.1.1")


async def test_one_address_gets_thirty_codes_an_hour(service: Service) -> None:
    for n in range(LINKS_PER_IP_PER_HOUR):
        await service.linking.create(Computer("PC", DeviceOs.LINUX, f"inst-{n}"), ip="10.0.0.1")

    with pytest.raises(RateLimitedError):
        await service.linking.create(Computer("PC", DeviceOs.LINUX, "inst-new"), ip="10.0.0.1")
    service.clock.advance(timedelta(hours=1))
    await service.linking.create(Computer("PC", DeviceOs.LINUX, "inst-new"), ip="10.0.0.1")


async def test_codes_older_than_the_limit_are_forgotten(service: Service) -> None:
    old = await service.linking.create(PC)
    service.clock.advance(LINK_LIFETIME + timedelta(hours=1))

    await service.linking.create(PC)

    assert old.code not in service.tables.links
    assert [link.status for link in service.tables.links.values()] == [LinkStatus.PENDING]


async def test_signals_wake_only_the_answered_code() -> None:
    signals = LinkSignals()
    waiting = asyncio.create_task(signals.wait("a", timedelta(seconds=5)))
    other = asyncio.create_task(signals.wait("b", timedelta(milliseconds=100)))
    await asyncio.sleep(0)

    signals.answered("a")
    await asyncio.wait_for(waiting, timeout=1)

    assert not other.done()
    await other
