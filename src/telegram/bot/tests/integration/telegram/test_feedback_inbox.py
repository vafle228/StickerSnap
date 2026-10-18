from dataclasses import replace

import pytest
from aiogram import Bot
from aiogram.methods import SendDocument, SendMessage

from stickersnap_bot.application.errors import RateLimitedError, TelegramUnavailableError
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.feedback import (
    Author,
    Feedback,
    FeedbackSource,
    FeedbackType,
    ReportedError,
)
from stickersnap_bot.domain.users import TelegramProfile, User
from stickersnap_bot.infrastructure.telegram.feedback_inbox import (
    TelegramFeedbackInbox,
    feedback_message,
)
from tests.fakes import NOW
from tests.integration.telegram.bot_api import FakeBotApi, ok

CHAT = -100500
DIMA = User.first_seen(42, TelegramProfile("Дима", username="dmitriu"), NOW)
PC = Device("pc", 42, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, "hash", NOW)
BUG = Feedback(
    type=FeedbackType.BUG,
    text="Экспорт падает на гифке <900> кадров & всё",
    author=Author(FeedbackSource.DESKTOP, user_id=42, device_id="pc"),
    can_reply=True,
    created_at=NOW,
    app_version="0.3.1",
    platform="Windows 11",
    error=ReportedError("render_failed", "7f3a-c21e"),
    diagnostics="журнал",
    id=41,
)
MESSAGE = {"message_id": 7, "date": 0, "chat": {"id": CHAT, "type": "private"}}


@pytest.fixture
def api() -> FakeBotApi:
    return FakeBotApi()


@pytest.fixture
def inbox(api: FakeBotApi) -> TelegramFeedbackInbox:
    return TelegramFeedbackInbox(Bot("123:token", session=api), CHAT)


def test_a_bug_from_a_linked_computer_reads_as_on_the_artboard() -> None:
    assert feedback_message(BUG, DIMA, PC) == (
        "<b>Ошибка</b> · десктоп · StickerSnap 0.3.1 · Windows 11\n"
        "От: @dmitriu · DESKTOP-7F3K2Q · можно писать\n"
        "\n"
        "«Экспорт падает на гифке &lt;900&gt; кадров &amp; всё»\n"
        "\n"
        "Код: render_failed · 7f3a-c21e · #41"
    )


def test_an_idea_from_the_miniapp_names_a_user_without_username() -> None:
    idea = Feedback(
        type=FeedbackType.IDEA,
        text="Хочу менять порядок",
        author=Author(FeedbackSource.MINIAPP, user_id=7),
        can_reply=False,
        created_at=NOW,
        platform="ios",
        id=42,
    )
    anna = User.first_seen(7, TelegramProfile("Анна", "Иванова"), NOW)

    assert feedback_message(idea, anna, None) == (
        "<b>Идея</b> · Mini App · ios\n"
        'От: <a href="tg://user?id=7">Анна Иванова</a> · не писать\n'
        "\n"
        "«Хочу менять порядок»\n"
        "\n"
        "#42"
    )


def test_without_a_link_the_contact_is_the_way_back() -> None:
    other = replace(
        BUG,
        type=FeedbackType.OTHER,
        author=Author(FeedbackSource.DESKTOP, installation_id="i-1"),
        contact="me@example.com",
        error=None,
        app_version=None,
        platform=None,
    )

    lines = feedback_message(other, None, None).split("\n")

    assert lines[:2] == ["<b>Другое</b> · десктоп", "От: без привязки · ответить на me@example.com"]
    lines = feedback_message(replace(other, can_reply=False), None, None).split("\n")
    assert lines[1] == "От: без привязки · не отвечать"


async def test_diagnostics_go_as_a_file_in_reply(
    api: FakeBotApi, inbox: TelegramFeedbackInbox
) -> None:
    api.answers["SendMessage"] = ok(MESSAGE)
    api.answers["SendDocument"] = ok(MESSAGE | {"message_id": 8})

    await inbox.deliver(BUG, DIMA, PC)

    message, document = api.methods
    assert isinstance(message, SendMessage)
    assert (message.chat_id, message.parse_mode) == (CHAT, "HTML")
    assert isinstance(document, SendDocument)
    assert document.reply_parameters is not None
    assert document.reply_parameters.message_id == 7
    assert getattr(document.document, "filename", None) == "diagnostics-41.txt"


async def test_feedback_without_diagnostics_is_one_message(
    api: FakeBotApi, inbox: TelegramFeedbackInbox
) -> None:
    api.answers["SendMessage"] = ok(MESSAGE)

    await inbox.deliver(replace(BUG, diagnostics=None), DIMA, PC)

    assert api.calls == ["SendMessage"]


async def test_a_chat_the_bot_cannot_write_to_is_unavailable(
    api: FakeBotApi, inbox: TelegramFeedbackInbox
) -> None:
    api.answers["SendMessage"] = (
        400,
        {"ok": False, "error_code": 400, "description": "Bad Request: chat not found"},
    )

    with pytest.raises(TelegramUnavailableError):
        await inbox.deliver(BUG, DIMA, PC)


async def test_flood_control_is_rate_limited(api: FakeBotApi, inbox: TelegramFeedbackInbox) -> None:
    api.answers["SendMessage"] = (
        429,
        {
            "ok": False,
            "error_code": 429,
            "description": "Too Many Requests: retry after 5",
            "parameters": {"retry_after": 5},
        },
    )

    with pytest.raises(RateLimitedError):
        await inbox.deliver(BUG, DIMA, PC)
