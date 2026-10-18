from datetime import datetime, timedelta

import pytest

from stickersnap_bot.application.errors import (
    BadRequestError,
    RateLimitedError,
    TelegramUnavailableError,
    UnauthorizedError,
)
from stickersnap_bot.application.feedback import (
    AnonymousCaller,
    DeviceCaller,
    FeedbackForm,
    MiniAppCaller,
    SendFeedback,
)
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.feedback import FeedbackSource, FeedbackType, ReportedError
from stickersnap_bot.domain.secrets import hash_secret
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import NOW, FakeFeedbackInbox, Tables, fake_unit_of_work, send_feedback

ME = 42
KEY = "device-key"
IDEA = FeedbackForm(FeedbackType.IDEA, "Хочу менять порядок стикеров", can_reply=True)
ANONYMOUS_IDEA = FeedbackForm(
    FeedbackType.IDEA, "Идея", can_reply=True, contact="me@example.com", installation_id="i-1"
)


@pytest.fixture
def tables() -> Tables:
    tables = Tables()
    tables.users[ME] = User.first_seen(ME, TelegramProfile("Дима", username="dmitriu"), NOW)
    tables.devices["pc"] = Device(
        "pc", ME, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, hash_secret(KEY), NOW
    )
    return tables


@pytest.fixture
def inbox() -> FakeFeedbackInbox:
    return FakeFeedbackInbox()


class Clock:
    def __init__(self) -> None:
        self.now = NOW

    def __call__(self) -> datetime:
        return self.now


async def test_feedback_from_the_miniapp_is_stored_and_forwarded(
    tables: Tables, inbox: FakeFeedbackInbox
) -> None:
    number = await send_feedback(tables, inbox)(MiniAppCaller(ME), IDEA, "203.0.113.5")

    stored = tables.feedback[number]
    assert stored.author.source is FeedbackSource.MINIAPP
    assert stored.author.user_id == ME
    assert stored.author.ip_hash == hash_secret("203.0.113.5")
    assert stored.forwarded_at == NOW
    [(delivered, user, device)] = inbox.delivered
    assert (delivered.id, delivered.text) == (number, IDEA.text)
    assert user == tables.users[ME]
    assert device is None


async def test_a_linked_computer_sends_as_its_owner(
    tables: Tables, inbox: FakeFeedbackInbox
) -> None:
    bug = FeedbackForm(
        FeedbackType.BUG,
        "",
        can_reply=False,
        app_version="0.3.1",
        platform="Windows 11",
        error=ReportedError("render_failed", "7f3a-c21e"),
        diagnostics="journal",
    )

    number = await send_feedback(tables, inbox)(DeviceCaller(KEY), bug)

    stored = tables.feedback[number]
    assert stored.author.source is FeedbackSource.DESKTOP
    assert (stored.author.user_id, stored.author.device_id) == (ME, "pc")
    assert stored.diagnostics == "journal"
    [(_, user, device)] = inbox.delivered
    assert user == tables.users[ME]
    assert device == tables.devices["pc"]


async def test_an_unknown_device_key_is_unauthorized(tables: Tables) -> None:
    with pytest.raises(UnauthorizedError):
        await send_feedback(tables)(DeviceCaller("revoked"), IDEA)

    assert tables.feedback == {}


async def test_a_desktop_without_a_link_sends_anonymously(
    tables: Tables, inbox: FakeFeedbackInbox
) -> None:
    number = await send_feedback(tables, inbox)(AnonymousCaller(), ANONYMOUS_IDEA)

    stored = tables.feedback[number]
    assert stored.author.user_id is None
    assert (stored.author.installation_id, stored.contact) == ("i-1", "me@example.com")
    assert inbox.delivered[0][1:] == (None, None)


async def test_a_broken_rule_is_a_bad_request(tables: Tables) -> None:
    with pytest.raises(BadRequestError, match="installationId"):
        await send_feedback(tables)(AnonymousCaller(), IDEA)


async def test_a_feedback_chat_that_fails_leaves_it_stored_unforwarded(
    tables: Tables, inbox: FakeFeedbackInbox
) -> None:
    inbox.failure = TelegramUnavailableError("chat not found")

    number = await send_feedback(tables, inbox)(MiniAppCaller(ME), IDEA)

    assert tables.feedback[number].forwarded_at is None


async def test_without_a_feedback_chat_it_is_only_stored(tables: Tables) -> None:
    sender = SendFeedback(fake_unit_of_work(tables), None, clock=lambda: NOW)

    number = await sender(MiniAppCaller(ME), IDEA)

    assert tables.feedback[number].forwarded_at is None


async def test_an_account_sends_five_an_hour_from_anywhere(tables: Tables) -> None:
    clock = Clock()
    sender = SendFeedback(fake_unit_of_work(tables), FakeFeedbackInbox(), clock)
    for minute in range(5):
        clock.now = NOW + timedelta(minutes=minute * 10)
        caller = MiniAppCaller(ME) if minute % 2 else DeviceCaller(KEY)
        await sender(caller, IDEA)

    with pytest.raises(RateLimitedError) as refused:
        await sender(MiniAppCaller(ME), IDEA)

    # the first one, sent at NOW, turns an hour old 20 minutes after the fifth
    assert refused.value.retry_after_sec == 20 * 60
    clock.now = NOW + timedelta(hours=1)
    await sender(MiniAppCaller(ME), IDEA)


async def test_an_installation_without_a_link_sends_five_an_hour(tables: Tables) -> None:
    sender = send_feedback(tables)
    for _ in range(5):
        await sender(AnonymousCaller(), ANONYMOUS_IDEA, "203.0.113.5")

    with pytest.raises(RateLimitedError):
        await sender(AnonymousCaller(), ANONYMOUS_IDEA, "203.0.113.6")
    other = FeedbackForm(FeedbackType.IDEA, "Идея", can_reply=False, installation_id="i-2")
    await sender(AnonymousCaller(), other, "203.0.113.5")


async def test_one_ip_without_a_link_sends_ten_an_hour(tables: Tables) -> None:
    sender = send_feedback(tables)
    for n in range(10):
        form = FeedbackForm(FeedbackType.IDEA, "Идея", can_reply=False, installation_id=f"i-{n}")
        await sender(AnonymousCaller(), form, "203.0.113.5")

    fresh = FeedbackForm(FeedbackType.IDEA, "Идея", can_reply=False, installation_id="i-new")
    with pytest.raises(RateLimitedError):
        await sender(AnonymousCaller(), fresh, "203.0.113.5")
    # a linked account is not counted by IP
    await sender(MiniAppCaller(ME), IDEA, "203.0.113.5")
