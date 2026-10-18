"""The bot's chat: answers to what a user writes there, and the notifications the bot sends itself.

Board 17 «Бот: чат»: the bot greets on /start, answers /help and /feedback, answers anything else
with one phrase and a button, and writes when a computer is linked, a pack is created or
stickers are added. Publications that follow one another into one pack edit the last «added»
message instead of sending a new one, while nothing else came into the chat and for 10 minutes.

What the chat remembers for that lives in memory: after a restart the next notification is simply
a new message.
"""

import asyncio
import logging
from dataclasses import dataclass, replace
from datetime import datetime, timedelta
from enum import StrEnum

from stickersnap_bot.application import chat_texts
from stickersnap_bot.application.chat_messages import ChatMessage, ChatMessages
from stickersnap_bot.application.clock import Clock
from stickersnap_bot.application.errors import ApplicationError
from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.packs import Pack
from stickersnap_bot.domain.users import TelegramProfile, User

_log = logging.getLogger(__name__)

# the «added» notice keeps being edited this long after the last publication
EDIT_WINDOW = timedelta(minutes=10)
# after an answer to a random message the next two go unanswered for this long
QUIET_WINDOW = timedelta(minutes=1)
QUIET_MESSAGES = 2
# past this many users in memory, the ones with nothing left to remember are forgotten
_PRUNE_AT = 4096


class Incoming(StrEnum):
    # text, a sticker, a photo, a voice message, an unknown command
    TEXT = "text"
    # a video, a GIF, a file: stickers from them are made on the computer
    MEDIA = "media"


@dataclass(frozen=True)
class _AddedNotice:
    message_id: int
    pack: str
    added: int
    at: datetime


@dataclass(frozen=True)
class _Answered:
    at: datetime
    unanswered: int = 0


class ChatHistory:
    """What the service remembers about each chat: the last «added» notice while it may still be
    edited, and when the bot last answered a random message."""

    def __init__(self) -> None:
        self._added: dict[int, _AddedNotice] = {}
        self._answered: dict[int, _Answered] = {}

    def moved_on(self, user_id: int) -> None:
        """Something else came into the chat: the last notice is no longer the last message."""
        self._added.pop(user_id, None)

    def added_notice(self, user_id: int, pack: str, now: datetime) -> _AddedNotice | None:
        notice = self._added.get(user_id)
        if notice is None or notice.pack != pack or now - notice.at >= EDIT_WINDOW:
            return None
        return notice

    def remember_added(self, user_id: int, notice: _AddedNotice) -> None:
        self._added[user_id] = notice
        self._prune(notice.at)

    def may_answer(self, user_id: int, now: datetime) -> bool:
        """One answer, then quiet for the next two messages within a minute."""
        last = self._answered.get(user_id)
        if last is None or now - last.at >= QUIET_WINDOW or last.unanswered >= QUIET_MESSAGES:
            self._answered[user_id] = _Answered(now)
            self._prune(now)
            return True
        self._answered[user_id] = replace(last, unanswered=last.unanswered + 1)
        return False

    def _prune(self, now: datetime) -> None:
        if len(self._added) > _PRUNE_AT:
            self._added = {k: v for k, v in self._added.items() if now - v.at < EDIT_WINDOW}
        if len(self._answered) > _PRUNE_AT:
            self._answered = {k: v for k, v in self._answered.items() if now - v.at < QUIET_WINDOW}


async def _send(messages: ChatMessages, user_id: int, message: ChatMessage) -> int | None:
    """A chat message never fails the work around it: Telegram's refusal is only logged."""
    try:
        return await messages.send(user_id, message)
    except ApplicationError:
        _log.warning("could not write to %s", user_id, exc_info=True)
        return None


class BotChat:
    """What the bot answers in the chat."""

    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        messages: ChatMessages,
        history: ChatHistory,
        clock: Clock,
        download_url: str,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._messages = messages
        self._history = history
        self._clock = clock
        self._download_url = download_url

    async def start(self, user_id: int, profile: TelegramProfile) -> None:
        """/start, the first one or again: the same welcome every time."""
        await self._remember(user_id, profile, stopped=False)
        await self._reply(user_id, chat_texts.welcome(self._download_url))

    async def help(self, user_id: int) -> None:
        await self._reply(user_id, chat_texts.help_message(self._download_url))

    async def feedback(self, user_id: int) -> None:
        await self._reply(user_id, chat_texts.feedback())

    async def answer(self, user_id: int, incoming: Incoming) -> None:
        """Anything that is not a command: the bot does not pretend to read it."""
        self._history.moved_on(user_id)
        if not self._history.may_answer(user_id, self._clock()):
            return
        message = (
            chat_texts.made_on_computer(self._download_url)
            if incoming is Incoming.MEDIA
            else chat_texts.not_read()
        )
        await _send(self._messages, user_id, message)

    async def member_changed(self, user_id: int, profile: TelegramProfile, stopped: bool) -> None:
        """my_chat_member: the user stopped the bot, or started it again."""
        await self._remember(user_id, profile, stopped=stopped)

    async def _reply(self, user_id: int, message: ChatMessage) -> None:
        self._history.moved_on(user_id)
        await _send(self._messages, user_id, message)

    async def _remember(self, user_id: int, profile: TelegramProfile, stopped: bool) -> None:
        now = self._clock()
        async with self._unit_of_work() as uow:
            known = await uow.users.get(user_id)
            user = (
                User.first_seen(user_id, profile, now)
                if known is None
                else known.with_profile(profile, now)
            ).with_bot_blocked(stopped, now)
            if user is not known:
                await uow.users.save(user)
                await uow.commit()


class Notifications:
    """What the bot writes by itself: S4 calls device_linked, S5 pack_created and stickers_added.

    Nothing is sent to a user who stopped the bot or is unknown to the service. A notification
    never fails the linking or the publication that caused it.
    """

    def __init__(
        self,
        unit_of_work: UnitOfWorkFactory,
        messages: ChatMessages,
        history: ChatHistory,
        clock: Clock,
    ) -> None:
        self._unit_of_work = unit_of_work
        self._messages = messages
        self._history = history
        self._clock = clock
        # one notice at a time: two publications at once must not both send «added»
        self._lock = asyncio.Lock()

    async def device_linked(self, device: Device) -> None:
        await self._notify(device.user_id, chat_texts.device_linked(device.name, device.os))

    async def pack_created(self, pack: Pack, count: int) -> None:
        """`count`: the stickers the new set holds, the first one at least."""
        message = chat_texts.pack_created(pack.name, pack.title, pack.kind, count)
        await self._notify(pack.owner_id, message)

    async def stickers_added(self, pack: Pack, added: int, count: int) -> None:
        """`added` stickers went into an existing pack, which holds `count` now."""
        user_id = pack.owner_id
        async with self._lock:
            if not await self._may_write(user_id):
                return
            now = self._clock()
            last = self._history.added_notice(user_id, pack.name, now)
            if last is not None:
                total = last.added + added
                edited = chat_texts.stickers_added(pack.name, pack.title, pack.kind, total, count)
                if await self._edit(user_id, last.message_id, edited):
                    self._history.remember_added(user_id, replace(last, added=total, at=now))
                    return
            self._history.moved_on(user_id)
            message = chat_texts.stickers_added(pack.name, pack.title, pack.kind, added, count)
            sent = await _send(self._messages, user_id, message)
            if sent is not None:
                self._history.remember_added(user_id, _AddedNotice(sent, pack.name, added, now))

    async def _notify(self, user_id: int, message: ChatMessage) -> None:
        async with self._lock:
            if not await self._may_write(user_id):
                return
            self._history.moved_on(user_id)
            await _send(self._messages, user_id, message)

    async def _may_write(self, user_id: int) -> bool:
        async with self._unit_of_work() as uow:
            user = await uow.users.get(user_id)
        return user is not None and not user.bot_blocked

    async def _edit(self, user_id: int, message_id: int, message: ChatMessage) -> bool:
        try:
            return await self._messages.edit(user_id, message_id, message)
        except ApplicationError:
            _log.warning("could not edit a notice to %s", user_id, exc_info=True)
            return False
