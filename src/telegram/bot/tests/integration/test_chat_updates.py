"""Updates from Telegram reach the right answer: the dispatcher over the chat's use cases."""

from collections.abc import AsyncIterator
from typing import Any

import pytest
from aiogram import Bot, Dispatcher
from aiogram.types import Update

from stickersnap_bot.api.telegram import create_dispatcher
from tests.fakes import FakeChatMessages, Tables, bot_chat

ME = 42
FROM = {"id": ME, "is_bot": False, "first_name": "Дима", "username": "dima"}
WELCOME = "Привет! Это StickerSnap."
NOT_READ = "Я не читаю сообщения в чате."
ON_COMPUTER = "Стикеры из видео делаются в StickerSnap на компьютере."
FILE = {"file_id": "f", "file_unique_id": "u"}


BOT = {"id": 123, "is_bot": True, "first_name": "StickerSnap"}


def _member(status: str) -> dict[str, Any]:
    # a kicked member says until when; 0 is forever
    return {"status": status, "user": BOT} | ({"until_date": 0} if status == "kicked" else {})


class Telegram:
    def __init__(self) -> None:
        self.tables = Tables()
        self.messages = FakeChatMessages()
        self.bot = Bot("123:token")
        self.dispatcher: Dispatcher = create_dispatcher(bot_chat(self.tables, self.messages))
        self._next = 0

    async def message(self, chat_type: str = "private", **content: Any) -> None:
        self._next += 1
        message = {
            "message_id": self._next,
            "date": 0,
            "chat": {"id": ME, "type": chat_type},
            "from": FROM,
            **content,
        }
        await self._feed({"update_id": self._next, "message": message})

    async def command(self, text: str) -> None:
        entity = {"type": "bot_command", "offset": 0, "length": len(text.split()[0])}
        await self.message(text=text, entities=[entity])

    async def member(self, old: str, new: str) -> None:
        self._next += 1
        await self._feed(
            {
                "update_id": self._next,
                "my_chat_member": {
                    "chat": {"id": ME, "type": "private"},
                    "from": FROM,
                    "date": 0,
                    "old_chat_member": _member(old),
                    "new_chat_member": _member(new),
                },
            }
        )

    async def _feed(self, update: dict[str, Any]) -> None:
        await self.dispatcher.feed_update(
            self.bot, Update.model_validate(update, context={"bot": self.bot})
        )

    def texts(self) -> list[str]:
        return self.messages.texts(ME)


@pytest.fixture
async def telegram() -> AsyncIterator[Telegram]:
    telegram = Telegram()
    yield telegram
    await telegram.bot.session.close()


async def test_start_greets_and_remembers_the_user(telegram: Telegram) -> None:
    await telegram.command("/start")

    assert telegram.texts()[0].startswith(WELCOME)
    assert telegram.tables.users[ME].profile.username == "dima"


async def test_start_with_a_payload_greets_the_same(telegram: Telegram) -> None:
    await telegram.command("/start abc123")

    assert telegram.texts()[0].startswith(WELCOME)


@pytest.mark.parametrize(
    ("command", "first_line"),
    [
        ("/help", "StickerSnap делает видеостикеры на компьютере"),
        ("/feedback", "Об ошибке или идее напишите в форме."),
        ("/packs", NOT_READ),
    ],
)
async def test_commands_get_their_answers(
    telegram: Telegram, command: str, first_line: str
) -> None:
    await telegram.command(command)

    assert telegram.texts()[0].startswith(first_line)


@pytest.mark.parametrize(
    ("content", "answer"),
    [
        ({"text": "Не могу опубликовать стикер"}, NOT_READ),
        ({"photo": [{**FILE, "width": 1, "height": 1}]}, NOT_READ),
        ({"voice": {**FILE, "duration": 1}}, NOT_READ),
        (
            {
                "sticker": {
                    **FILE,
                    "type": "regular",
                    "width": 512,
                    "height": 512,
                    "is_animated": False,
                    "is_video": False,
                }
            },
            NOT_READ,
        ),
        ({"video": {**FILE, "width": 1, "height": 1, "duration": 7}}, ON_COMPUTER),
        ({"animation": {**FILE, "width": 1, "height": 1, "duration": 2}}, ON_COMPUTER),
        ({"document": FILE}, ON_COMPUTER),
    ],
    ids=["text", "photo", "voice", "sticker", "video", "gif", "file"],
)
async def test_anything_else_gets_one_phrase(
    telegram: Telegram, content: dict[str, Any], answer: str
) -> None:
    await telegram.message(**content)

    assert telegram.texts()[0].startswith(answer)


async def test_a_group_gets_nothing(telegram: Telegram) -> None:
    await telegram.message(chat_type="group", text="/start")

    assert telegram.texts() == []


async def test_stopping_and_starting_the_bot_is_remembered(telegram: Telegram) -> None:
    await telegram.member("member", "kicked")
    assert telegram.tables.users[ME].bot_blocked

    await telegram.member("kicked", "member")
    assert not telegram.tables.users[ME].bot_blocked
