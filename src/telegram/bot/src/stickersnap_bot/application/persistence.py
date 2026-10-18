"""Ports of the service's registry: users, devices, link codes, packs and feedback.

A use case opens a unit of work, reads and saves through its repositories and commits once, so a
claim that turns a link into a device lands together or not at all. Leaving the unit of work
without commit() rolls everything back.
"""

from collections.abc import Callable, Sequence
from datetime import datetime
from types import TracebackType
from typing import Protocol, Self

from stickersnap_bot.domain.devices import Device
from stickersnap_bot.domain.feedback import Feedback
from stickersnap_bot.domain.links import LinkRequest
from stickersnap_bot.domain.packs import Pack
from stickersnap_bot.domain.users import User


class UserRepository(Protocol):
    async def get(self, user_id: int) -> User | None: ...

    async def save(self, user: User) -> None: ...


class DeviceRepository(Protocol):
    async def get(self, device_id: str) -> Device | None: ...

    async def find_by_key_hash(self, key_hash: str) -> Device | None: ...

    async def list_for_installation(self, installation_id: str) -> Sequence[Device]:
        """The devices of one desktop installation, whoever they are linked to."""
        ...

    async def list_for_user(self, user_id: int) -> Sequence[Device]:
        """Newest first (by linked_at), as «Устройства» shows them."""
        ...

    async def save(self, device: Device) -> None: ...

    async def delete(self, device_id: str) -> None: ...


class LinkRepository(Protocol):
    async def get(self, code: str) -> LinkRequest | None: ...

    async def save(self, link: LinkRequest) -> None: ...

    async def delete(self, code: str) -> None: ...

    async def delete_expired(self, now: datetime) -> int:
        """Forgets the codes whose time is up by `now`, answered or not; returns how many."""
        ...

    async def created_since(
        self,
        since: datetime,
        *,
        installation_id: str | None = None,
        ip_hash: str | None = None,
    ) -> Sequence[datetime]:
        """When the codes of one desktop were asked for after `since`, oldest first. The desktop
        is whichever one of the two is given."""
        ...


class PackRepository(Protocol):
    async def get(self, name: str) -> Pack | None: ...

    async def list_for_owner(self, owner_id: int) -> Sequence[Pack]:
        """Freshest first (by updated_at), as «Мои паки» shows them."""
        ...

    async def save(self, pack: Pack) -> None: ...

    async def delete(self, name: str) -> None: ...


class FeedbackRepository(Protocol):
    async def get(self, feedback_id: int) -> Feedback | None: ...

    async def add(self, feedback: Feedback) -> Feedback:
        """Stores new feedback and returns it with the id the database gave it."""
        ...

    async def save(self, feedback: Feedback) -> None: ...

    async def sent_since(
        self,
        since: datetime,
        *,
        user_id: int | None = None,
        installation_id: str | None = None,
        ip_hash: str | None = None,
    ) -> Sequence[datetime]:
        """When the feedback of one author arrived after `since`, oldest first. The author is
        whichever one of the three is given."""
        ...


class UnitOfWork(Protocol):
    # read-only, so an adapter may expose its own repository types
    @property
    def users(self) -> UserRepository: ...

    @property
    def devices(self) -> DeviceRepository: ...

    @property
    def links(self) -> LinkRepository: ...

    @property
    def packs(self) -> PackRepository: ...

    @property
    def feedback(self) -> FeedbackRepository: ...

    async def __aenter__(self) -> Self: ...

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> None: ...

    async def commit(self) -> None: ...


type UnitOfWorkFactory = Callable[[], UnitOfWork]
