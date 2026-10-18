"""In-memory stand-ins for the application's ports, for fast tests of use cases and routes."""

import json
from collections.abc import Sequence
from dataclasses import dataclass, field, replace
from datetime import UTC, datetime, timedelta
from types import TracebackType
from typing import Self

from stickersnap_bot.application.chat import BotChat, ChatHistory, Notifications
from stickersnap_bot.application.chat_messages import ChatMessage
from stickersnap_bot.application.errors import ApplicationError
from stickersnap_bot.application.init_data import InitDataVerifier, sign_init_data
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.miniapp import MiniApp
from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.application.sign_in import SignIn
from stickersnap_bot.application.sticker_sets import SetSticker, StickerFile, StickerSet
from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.links import LinkRequest
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import User

NOW = datetime(2026, 10, 18, 9, 0, tzinfo=UTC)
TOKEN = "123:secret"
DOWNLOAD_URL = "https://example.com/download"


@dataclass
class Tables:
    users: dict[int, User] = field(default_factory=dict)
    devices: dict[str, Device] = field(default_factory=dict)
    links: dict[str, LinkRequest] = field(default_factory=dict)
    packs: dict[str, Pack] = field(default_factory=dict)


class _Users:
    def __init__(self, tables: Tables) -> None:
        self._t = tables

    async def get(self, user_id: int) -> User | None:
        return self._t.users.get(user_id)

    async def save(self, user: User) -> None:
        self._t.users[user.id] = user


class _Devices:
    def __init__(self, tables: Tables) -> None:
        self._t = tables

    async def get(self, device_id: str) -> Device | None:
        return self._t.devices.get(device_id)

    async def find_by_key_hash(self, key_hash: str) -> Device | None:
        return next((d for d in self._t.devices.values() if d.key_hash == key_hash), None)

    async def list_for_user(self, user_id: int) -> Sequence[Device]:
        found = [d for d in self._t.devices.values() if d.user_id == user_id]
        return sorted(found, key=lambda d: d.linked_at, reverse=True)

    async def save(self, device: Device) -> None:
        self._t.devices[device.id] = device

    async def delete(self, device_id: str) -> None:
        self._t.devices.pop(device_id, None)


class _Links:
    def __init__(self, tables: Tables) -> None:
        self._t = tables

    async def get(self, code: str) -> LinkRequest | None:
        return self._t.links.get(code)

    async def save(self, link: LinkRequest) -> None:
        self._t.links[link.code] = link

    async def delete_expired(self, now: datetime) -> int:
        expired = [code for code, link in self._t.links.items() if link.is_expired(now)]
        for code in expired:
            del self._t.links[code]
        return len(expired)


class _Packs:
    def __init__(self, tables: Tables) -> None:
        self._t = tables

    async def get(self, name: str) -> Pack | None:
        return next((p for p in self._t.packs.values() if p.name.lower() == name.lower()), None)

    async def list_for_owner(self, owner_id: int) -> Sequence[Pack]:
        found = [p for p in self._t.packs.values() if p.owner_id == owner_id]
        return sorted(found, key=lambda p: p.updated_at, reverse=True)

    async def save(self, pack: Pack) -> None:
        self._t.packs[pack.name] = pack

    async def delete(self, name: str) -> None:
        self._t.packs.pop(name, None)


class FakeUnitOfWork:
    """Works on a copy of the tables and puts it back on commit, like a transaction."""

    def __init__(self, committed: Tables) -> None:
        self._committed = committed
        self._work = Tables()

    @property
    def users(self) -> _Users:
        return _Users(self._work)

    @property
    def devices(self) -> _Devices:
        return _Devices(self._work)

    @property
    def links(self) -> _Links:
        return _Links(self._work)

    @property
    def packs(self) -> _Packs:
        return _Packs(self._work)

    async def __aenter__(self) -> Self:
        self._work = _copy(self._committed)
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> None:
        pass

    async def commit(self) -> None:
        for name in ("users", "devices", "links", "packs"):
            setattr(self._committed, name, dict(getattr(self._work, name)))


def _copy(tables: Tables) -> Tables:
    return replace(
        tables,
        users=dict(tables.users),
        devices=dict(tables.devices),
        links=dict(tables.links),
        packs=dict(tables.packs),
    )


def fake_unit_of_work(tables: Tables) -> UnitOfWorkFactory:
    return lambda: FakeUnitOfWork(tables)


class FakeStickerSets:
    """Telegram's sets by name; `failure` makes every call raise it."""

    def __init__(self) -> None:
        self.sets: dict[str, StickerSet] = {}
        self.files: dict[str, StickerFile] = {}
        self.deleted: list[str] = []
        self.failure: ApplicationError | None = None

    async def get(self, name: str) -> StickerSet | None:
        self._fail()
        return self.sets.get(name)

    async def delete_sticker(self, sticker_id: str) -> None:
        self._fail()
        self.deleted.append(sticker_id)
        for name, found in self.sets.items():
            left = tuple(s for s in found.stickers if s.id != sticker_id)
            self.sets[name] = replace(found, stickers=left)
        self.sets = {name: s for name, s in self.sets.items() if s.stickers}

    async def download(self, file_id: str) -> StickerFile | None:
        self._fail()
        return self.files.get(file_id)

    def _fail(self) -> None:
        if self.failure is not None:
            raise self.failure


def sticker(n: int) -> SetSticker:
    return SetSticker(
        id=f"sticker{n}", emoji=("😴",), thumb_file_id=f"thumb{n}", video_file_id=f"video{n}"
    )


def sticker_set(name: str, count: int, kind: PackKind = PackKind.STICKERS) -> StickerSet:
    return StickerSet(
        name=name,
        title=f"Title of {name}",
        kind=kind,
        stickers=tuple(sticker(n) for n in range(1, count + 1)),
    )


def miniapp(tables: Tables, sets: FakeStickerSets | None = None, now: datetime = NOW) -> MiniApp:
    return MiniApp(
        fake_unit_of_work(tables), sets or FakeStickerSets(), MediaLinks(TOKEN), clock=lambda: now
    )


def sign_in(tables: Tables, now: datetime = NOW) -> SignIn:
    verifier = InitDataVerifier(TOKEN, max_age=timedelta(days=1))
    return SignIn(fake_unit_of_work(tables), verifier, clock=lambda: now)


def signed_launch(user_id: int, first_name: str = "Дима", now: datetime = NOW) -> str:
    """initData as Telegram signs it for the bot with TOKEN."""
    user = json.dumps({"id": user_id, "first_name": first_name}, ensure_ascii=False)
    return sign_init_data(TOKEN, {"user": user, "auth_date": str(int(now.timestamp()))})


class FakeClock:
    def __init__(self, now: datetime = NOW) -> None:
        self.now = now

    def __call__(self) -> datetime:
        return self.now

    def advance(self, by: timedelta) -> None:
        self.now += by


@dataclass(frozen=True)
class Sent:
    chat_id: int
    message: ChatMessage
    message_id: int


class FakeChatMessages:
    """The chats as Telegram would keep them: `chats[chat_id]` is what the user sees now.

    `refused` chats get None as a stopped bot does; `uneditable` messages refuse edits; `failure`
    makes every call raise it.
    """

    def __init__(self) -> None:
        self.sent: list[Sent] = []
        self.edited: list[Sent] = []
        self.refused: set[int] = set()
        self.uneditable: set[int] = set()
        self.failure: ApplicationError | None = None

    async def send(self, chat_id: int, message: ChatMessage) -> int | None:
        if self.failure is not None:
            raise self.failure
        if chat_id in self.refused:
            return None
        sent = Sent(chat_id, message, message_id=len(self.sent) + 1)
        self.sent.append(sent)
        return sent.message_id

    async def edit(self, chat_id: int, message_id: int, message: ChatMessage) -> bool:
        if self.failure is not None:
            raise self.failure
        if message_id in self.uneditable:
            return False
        self.edited.append(Sent(chat_id, message, message_id))
        return True

    def texts(self, chat_id: int) -> list[str]:
        """The chat's messages from the bot, oldest first, each as last edited."""
        latest = {s.message_id: s.message for s in [*self.sent, *self.edited]}
        return [latest[s.message_id].text for s in self.sent if s.chat_id == chat_id]


def bot_chat(
    tables: Tables,
    messages: FakeChatMessages | None = None,
    history: ChatHistory | None = None,
    clock: FakeClock | None = None,
) -> BotChat:
    return BotChat(
        fake_unit_of_work(tables),
        messages or FakeChatMessages(),
        history or ChatHistory(),
        clock=clock or FakeClock(),
        download_url=DOWNLOAD_URL,
    )


def notifications(
    tables: Tables,
    messages: FakeChatMessages | None = None,
    history: ChatHistory | None = None,
    clock: FakeClock | None = None,
) -> Notifications:
    return Notifications(
        fake_unit_of_work(tables),
        messages or FakeChatMessages(),
        history or ChatHistory(),
        clock=clock or FakeClock(),
    )
