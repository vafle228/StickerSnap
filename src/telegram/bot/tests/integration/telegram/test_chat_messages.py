from typing import Any

import pytest
from aiogram import Bot
from aiogram.methods import EditMessageText, SendMessage

from stickersnap_bot.application.chat_messages import ChatMessage, OpenApp, OpenUrl, Screen
from stickersnap_bot.application.errors import RateLimitedError, TelegramUnavailableError
from stickersnap_bot.infrastructure.telegram.chat_messages import BotChatMessages
from tests.integration.telegram.bot_api import FakeBotApi, bad_request, ok

ME = 42
ME_BOT = {"id": 1, "is_bot": True, "first_name": "StickerSnap", "username": "StickerSnapAppBot"}
NOTICE = ChatMessage(
    "В нём <первый> стикер.",
    title="Пак «Коты & диван» создан",
    buttons=(
        OpenUrl("Добавить в Telegram", "https://t.me/addstickers/cats_by_StickerSnapAppBot"),
        OpenApp("Открыть пак", pack="cats_by_StickerSnapAppBot"),
        OpenApp("Устройства", Screen.DEVICES),
        OpenApp("Мои паки"),
    ),
)


def sent_message(message_id: int) -> dict[str, Any]:
    return {"message_id": message_id, "date": 0, "chat": {"id": ME, "type": "private"}}


@pytest.fixture
def api() -> FakeBotApi:
    api = FakeBotApi()
    api.answers["GetMe"] = ok(ME_BOT)
    return api


@pytest.fixture
def messages(api: FakeBotApi) -> BotChatMessages:
    return BotChatMessages(Bot("123:token", session=api))


async def test_sends_html_with_a_button_per_row(api: FakeBotApi, messages: BotChatMessages) -> None:
    api.answers["SendMessage"] = ok(sent_message(7))

    message_id = await messages.send(ME, NOTICE)

    assert message_id == 7
    sent = api.methods[-1]
    assert isinstance(sent, SendMessage)
    assert sent.chat_id == ME
    assert sent.parse_mode == "HTML"
    assert sent.text == "<b>Пак «Коты &amp; диван» создан</b>\nВ нём &lt;первый&gt; стикер."
    assert sent.reply_markup is not None
    rows = sent.reply_markup.inline_keyboard  # type: ignore[union-attr]
    assert [[(b.text, b.url) for b in row] for row in rows] == [
        [("Добавить в Telegram", "https://t.me/addstickers/cats_by_StickerSnapAppBot")],
        [("Открыть пак", "https://t.me/StickerSnapAppBot?startapp=pack_cats_by_StickerSnapAppBot")],
        [("Устройства", "https://t.me/StickerSnapAppBot?startapp=devices")],
        [("Мои паки", "https://t.me/StickerSnapAppBot?startapp")],
    ]


async def test_asks_telegram_for_the_bots_name_once(
    api: FakeBotApi, messages: BotChatMessages
) -> None:
    api.answers["SendMessage"] = ok(sent_message(7))

    await messages.send(ME, NOTICE)
    await messages.send(ME, NOTICE)

    assert api.calls.count("GetMe") == 1


async def test_a_message_without_buttons_has_no_keyboard(
    api: FakeBotApi, messages: BotChatMessages
) -> None:
    api.answers["SendMessage"] = ok(sent_message(7))

    await messages.send(ME, ChatMessage("Привет"))

    sent = api.methods[-1]
    assert isinstance(sent, SendMessage)
    assert sent.reply_markup is None
    assert "GetMe" not in api.calls


async def test_a_stopped_bot_cannot_write(api: FakeBotApi, messages: BotChatMessages) -> None:
    api.answers["SendMessage"] = (
        403,
        {"ok": False, "error_code": 403, "description": "Forbidden: bot was blocked by the user"},
    )

    assert await messages.send(ME, NOTICE) is None


async def test_flood_control_becomes_rate_limited(
    api: FakeBotApi, messages: BotChatMessages
) -> None:
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
        await messages.send(ME, NOTICE)


async def test_a_server_error_becomes_unavailable(
    api: FakeBotApi, messages: BotChatMessages
) -> None:
    api.answers["SendMessage"] = (
        500,
        {"ok": False, "error_code": 500, "description": "Internal Server Error"},
    )

    with pytest.raises(TelegramUnavailableError):
        await messages.send(ME, NOTICE)


async def test_edits_a_message_in_place(api: FakeBotApi, messages: BotChatMessages) -> None:
    api.answers["EditMessageText"] = ok(sent_message(7))

    assert await messages.edit(ME, 7, NOTICE)

    edited = api.methods[-1]
    assert isinstance(edited, EditMessageText)
    assert (edited.chat_id, edited.message_id) == (ME, 7)
    assert edited.reply_markup is not None


async def test_the_same_text_again_is_an_edit(api: FakeBotApi, messages: BotChatMessages) -> None:
    api.answers["EditMessageText"] = bad_request(
        "message is not modified: specified new message content and reply markup are exactly "
        "the same as a current content and reply markup of the message"
    )

    assert await messages.edit(ME, 7, NOTICE)


async def test_a_deleted_message_cannot_be_edited(
    api: FakeBotApi, messages: BotChatMessages
) -> None:
    api.answers["EditMessageText"] = bad_request("message to edit not found")

    assert not await messages.edit(ME, 7, NOTICE)
