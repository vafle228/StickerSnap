from collections.abc import Awaitable, Callable
from datetime import timedelta

import pytest

from stickersnap_bot.application.chat import BotChat, ChatHistory, Incoming, Notifications
from stickersnap_bot.application.errors import TelegramUnavailableError
from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import TelegramProfile, User
from tests.fakes import NOW, FakeChatMessages, FakeClock, Tables, bot_chat, notifications

ME = 42
DIMA = TelegramProfile(first_name="Дима", username="dima")
CATS = Pack.create("cats_by_StickerSnapAppBot", ME, "Котики", PackKind.STICKERS, NOW)
DOGS = Pack.create("dogs_by_StickerSnapAppBot", ME, "Собаки", PackKind.STICKERS, NOW)
NOT_READ = "Я не читаю сообщения в чате."
ON_COMPUTER = "Стикеры из видео делаются в StickerSnap на компьютере."


class Chat:
    """The bot's both halves over one history, as main.py wires them."""

    def __init__(self, tables: Tables | None = None) -> None:
        self.tables = tables or Tables()
        self.messages = FakeChatMessages()
        self.clock = FakeClock()
        history = ChatHistory()
        self.bot: BotChat = bot_chat(self.tables, self.messages, history, self.clock)
        self.notifications: Notifications = notifications(
            self.tables, self.messages, history, self.clock
        )

    def texts(self) -> list[str]:
        return self.messages.texts(ME)


@pytest.fixture
def chat() -> Chat:
    # a user who pressed «Начать» long ago
    tables = Tables(users={ME: User.first_seen(ME, DIMA, NOW - timedelta(days=1))})
    return Chat(tables)


async def test_start_greets_and_remembers_a_new_user() -> None:
    chat = Chat()

    await chat.bot.start(ME, DIMA)

    assert chat.texts()[0].startswith("Привет! Это StickerSnap.")
    assert chat.tables.users[ME].profile == DIMA
    assert chat.tables.users[ME].created_at == NOW


async def test_start_again_greets_again(chat: Chat) -> None:
    await chat.bot.start(ME, DIMA)
    await chat.bot.start(ME, DIMA)

    assert len(chat.texts()) == 2


async def test_stopping_the_bot_stops_the_notifications(chat: Chat) -> None:
    await chat.bot.member_changed(ME, DIMA, stopped=True)
    await chat.notifications.pack_created(CATS, 1)

    assert chat.tables.users[ME].bot_blocked
    assert chat.texts() == []


async def test_starting_the_bot_again_brings_them_back(chat: Chat) -> None:
    await chat.bot.member_changed(ME, DIMA, stopped=True)
    await chat.bot.member_changed(ME, DIMA, stopped=False)
    await chat.notifications.pack_created(CATS, 1)

    assert not chat.tables.users[ME].bot_blocked
    assert len(chat.texts()) == 1


async def test_notifications_switched_off_are_not_sent(chat: Chat) -> None:
    chat.tables.users[ME] = chat.tables.users[ME].with_notifications(False, NOW)

    await chat.notifications.pack_created(CATS, 1)
    await chat.notifications.stickers_added(CATS, 1, 2)

    assert chat.texts() == []


async def test_a_user_the_service_never_saw_gets_no_notifications() -> None:
    chat = Chat()

    await chat.notifications.pack_created(CATS, 1)

    assert chat.texts() == []


async def test_help_and_feedback_answer_every_time(chat: Chat) -> None:
    for _ in range(3):
        await chat.bot.help(ME)
        await chat.bot.feedback(ME)

    assert len(chat.texts()) == 6


async def test_text_gets_one_answer_for_three_messages_in_a_minute(chat: Chat) -> None:
    for _ in range(4):
        await chat.bot.answer(ME, Incoming.TEXT)
        chat.clock.advance(timedelta(seconds=5))

    assert [t[: len(NOT_READ)] for t in chat.texts()] == [NOT_READ, NOT_READ]


async def test_after_a_quiet_minute_the_bot_answers_again(chat: Chat) -> None:
    await chat.bot.answer(ME, Incoming.TEXT)
    chat.clock.advance(timedelta(minutes=1))
    await chat.bot.answer(ME, Incoming.TEXT)

    assert len(chat.texts()) == 2


async def test_a_video_is_made_on_the_computer(chat: Chat) -> None:
    await chat.bot.answer(ME, Incoming.MEDIA)

    assert chat.texts()[0].startswith(ON_COMPUTER)


async def test_a_linked_computer_is_announced(chat: Chat) -> None:
    device = Device("d1", ME, "DESKTOP-7F3K2Q", DeviceOs.WINDOWS, "hash", NOW)

    await chat.notifications.device_linked(device)

    assert chat.messages.sent[0].message.title == "Компьютер привязан"


async def test_publications_into_one_pack_edit_the_last_notice(chat: Chat) -> None:
    await chat.notifications.stickers_added(CATS, 1, 13)
    chat.clock.advance(timedelta(minutes=9))
    await chat.notifications.stickers_added(CATS, 2, 15)

    assert chat.texts() == ["В пак «Котики» добавлено 3 стикера. Теперь в нём 15 из 120."]


async def test_the_window_follows_the_last_publication(chat: Chat) -> None:
    for count in (11, 12, 13):
        await chat.notifications.stickers_added(CATS, 1, count)
        chat.clock.advance(timedelta(minutes=9))

    assert len(chat.texts()) == 1


async def test_ten_quiet_minutes_start_a_new_notice(chat: Chat) -> None:
    await chat.notifications.stickers_added(CATS, 1, 13)
    chat.clock.advance(timedelta(minutes=10))
    await chat.notifications.stickers_added(CATS, 2, 15)

    assert chat.texts() == [
        "В пак «Котики» добавлен 1 стикер. Теперь в нём 13 из 120.",
        "В пак «Котики» добавлено 2 стикера. Теперь в нём 15 из 120.",
    ]


async def test_another_pack_gets_a_notice_of_its_own(chat: Chat) -> None:
    await chat.notifications.stickers_added(CATS, 1, 13)
    await chat.notifications.stickers_added(DOGS, 1, 5)
    await chat.notifications.stickers_added(CATS, 1, 14)

    assert len(chat.texts()) == 3


@pytest.mark.parametrize(
    "between",
    [
        lambda chat: chat.bot.answer(ME, Incoming.TEXT),
        lambda chat: chat.bot.help(ME),
        lambda chat: chat.notifications.pack_created(DOGS, 1),
    ],
    ids=["the user wrote", "the bot answered a command", "another notification"],
)
async def test_anything_new_in_the_chat_starts_a_new_notice(
    chat: Chat, between: Callable[[Chat], Awaitable[None]]
) -> None:
    await chat.notifications.stickers_added(CATS, 1, 13)
    await between(chat)
    await chat.notifications.stickers_added(CATS, 1, 14)

    assert chat.texts()[-1] == "В пак «Котики» добавлен 1 стикер. Теперь в нём 14 из 120."
    assert chat.messages.edited == []


async def test_a_notice_that_cannot_be_edited_is_sent_anew(chat: Chat) -> None:
    await chat.notifications.stickers_added(CATS, 1, 13)
    chat.messages.uneditable.add(chat.messages.sent[0].message_id)
    await chat.notifications.stickers_added(CATS, 2, 15)

    # the new message counts only what it is about
    assert chat.texts()[-1] == "В пак «Котики» добавлено 2 стикера. Теперь в нём 15 из 120."


async def test_a_refused_notice_is_not_edited_later(chat: Chat) -> None:
    chat.messages.refused.add(ME)
    await chat.notifications.stickers_added(CATS, 1, 13)
    chat.messages.refused.clear()
    await chat.notifications.stickers_added(CATS, 1, 14)

    assert chat.texts() == ["В пак «Котики» добавлен 1 стикер. Теперь в нём 14 из 120."]


async def test_telegram_failing_does_not_fail_the_caller(chat: Chat) -> None:
    chat.messages.failure = TelegramUnavailableError("down")

    await chat.notifications.stickers_added(CATS, 1, 13)
    await chat.notifications.pack_created(CATS, 1)
    await chat.bot.help(ME)

    assert chat.texts() == []
