"""SQLite repositories behind the application's ports. Rows become frozen domain objects and back;
nothing here outlives the unit of work's connection."""

from collections.abc import Sequence
from dataclasses import replace
from datetime import datetime
from typing import Any

from sqlalchemy import RowMapping, Table, delete, select, update
from sqlalchemy.dialects.sqlite import insert
from sqlalchemy.ext.asyncio import AsyncConnection

from stickersnap_bot.domain.devices import Device, DeviceOs
from stickersnap_bot.domain.feedback import (
    Author,
    Feedback,
    FeedbackSource,
    FeedbackType,
    ReportedError,
)
from stickersnap_bot.domain.links import LinkRequest, LinkStatus
from stickersnap_bot.domain.packs import Pack, PackKind
from stickersnap_bot.domain.users import TelegramProfile, User
from stickersnap_bot.infrastructure.database.tables import (
    devices,
    feedback,
    link_requests,
    packs,
    users,
)


async def _upsert(connection: AsyncConnection, table: Table, values: dict[str, Any]) -> None:
    keys = [column.name for column in table.primary_key.columns]
    statement = insert(table).values(values)
    await connection.execute(
        statement.on_conflict_do_update(
            index_elements=keys,
            set_={name: statement.excluded[name] for name in values if name not in keys},
        )
    )


class SqlUserRepository:
    def __init__(self, connection: AsyncConnection) -> None:
        self._connection = connection

    async def get(self, user_id: int) -> User | None:
        row = (
            (await self._connection.execute(select(users).where(users.c.id == user_id)))
            .mappings()
            .one_or_none()
        )
        return None if row is None else _user(row)

    async def save(self, user: User) -> None:
        await _upsert(
            self._connection,
            users,
            {
                "id": user.id,
                "first_name": user.profile.first_name,
                "last_name": user.profile.last_name,
                "username": user.profile.username,
                "language_code": user.profile.language_code,
                "bot_blocked": user.bot_blocked,
                "notifications": user.notifications,
                "created_at": user.created_at,
                "updated_at": user.updated_at,
            },
        )


class SqlDeviceRepository:
    def __init__(self, connection: AsyncConnection) -> None:
        self._connection = connection

    async def get(self, device_id: str) -> Device | None:
        return await self._one(devices.c.id == device_id)

    async def find_by_key_hash(self, key_hash: str) -> Device | None:
        return await self._one(devices.c.key_hash == key_hash)

    async def list_for_installation(self, installation_id: str) -> Sequence[Device]:
        result = await self._connection.execute(
            select(devices).where(devices.c.installation_id == installation_id)
        )
        return [_device(row) for row in result.mappings()]

    async def list_for_user(self, user_id: int) -> Sequence[Device]:
        result = await self._connection.execute(
            select(devices)
            .where(devices.c.user_id == user_id)
            .order_by(devices.c.linked_at.desc(), devices.c.id)
        )
        return [_device(row) for row in result.mappings()]

    async def save(self, device: Device) -> None:
        await _upsert(
            self._connection,
            devices,
            {
                "id": device.id,
                "user_id": device.user_id,
                "name": device.name,
                "os": device.os.value,
                "key_hash": device.key_hash,
                "linked_at": device.linked_at,
                "installation_id": device.installation_id,
            },
        )

    async def delete(self, device_id: str) -> None:
        await self._connection.execute(delete(devices).where(devices.c.id == device_id))

    async def _one(self, condition: Any) -> Device | None:
        result = await self._connection.execute(select(devices).where(condition))
        row = result.mappings().one_or_none()
        return None if row is None else _device(row)


class SqlLinkRepository:
    def __init__(self, connection: AsyncConnection) -> None:
        self._connection = connection

    async def get(self, code: str) -> LinkRequest | None:
        result = await self._connection.execute(
            select(link_requests).where(link_requests.c.code == code)
        )
        row = result.mappings().one_or_none()
        return None if row is None else _link(row)

    async def save(self, link: LinkRequest) -> None:
        await _upsert(
            self._connection,
            link_requests,
            {
                "code": link.code,
                "poll_token_hash": link.poll_token_hash,
                "device_name": link.device_name,
                "device_os": link.device_os.value,
                "status": link.status.value,
                "user_id": link.user_id,
                "device_id": link.device_id,
                "created_at": link.created_at,
                "expires_at": link.expires_at,
                "installation_id": link.installation_id,
                "platform": link.platform,
                "app_version": link.app_version,
                "ip_hash": link.ip_hash,
            },
        )

    async def delete(self, code: str) -> None:
        await self._connection.execute(delete(link_requests).where(link_requests.c.code == code))

    async def delete_expired(self, now: datetime) -> int:
        result = await self._connection.execute(
            delete(link_requests).where(link_requests.c.expires_at <= now)
        )
        return result.rowcount

    async def created_since(
        self,
        since: datetime,
        *,
        installation_id: str | None = None,
        ip_hash: str | None = None,
    ) -> Sequence[datetime]:
        given = [
            (link_requests.c[name], value)
            for name, value in (("installation_id", installation_id), ("ip_hash", ip_hash))
            if value is not None
        ]
        assert len(given) == 1, "one desktop at a time"
        column, value = given[0]
        result = await self._connection.execute(
            select(link_requests.c.created_at)
            .where(column == value, link_requests.c.created_at > since)
            .order_by(link_requests.c.created_at)
        )
        return list(result.scalars())


class SqlPackRepository:
    def __init__(self, connection: AsyncConnection) -> None:
        self._connection = connection

    async def get(self, name: str) -> Pack | None:
        result = await self._connection.execute(select(packs).where(packs.c.name == name))
        row = result.mappings().one_or_none()
        return None if row is None else _pack(row)

    async def list_for_owner(self, owner_id: int) -> Sequence[Pack]:
        result = await self._connection.execute(
            select(packs)
            .where(packs.c.owner_id == owner_id)
            .order_by(packs.c.updated_at.desc(), packs.c.name)
        )
        return [_pack(row) for row in result.mappings()]

    async def save(self, pack: Pack) -> None:
        await _upsert(
            self._connection,
            packs,
            {
                "name": pack.name,
                "owner_id": pack.owner_id,
                "title": pack.title,
                "kind": pack.kind.value,
                "created_at": pack.created_at,
                "updated_at": pack.updated_at,
            },
        )

    async def delete(self, name: str) -> None:
        await self._connection.execute(delete(packs).where(packs.c.name == name))


class SqlFeedbackRepository:
    def __init__(self, connection: AsyncConnection) -> None:
        self._connection = connection

    async def get(self, feedback_id: int) -> Feedback | None:
        result = await self._connection.execute(
            select(feedback).where(feedback.c.id == feedback_id)
        )
        row = result.mappings().one_or_none()
        return None if row is None else _feedback(row)

    async def add(self, item: Feedback) -> Feedback:
        values = _feedback_values(item)
        del values["id"]
        result = await self._connection.execute(insert(feedback).values(values))
        assert result.inserted_primary_key is not None
        return replace(item, id=result.inserted_primary_key[0])

    async def save(self, item: Feedback) -> None:
        assert item.id is not None, "add() gives feedback its id"
        await self._connection.execute(
            update(feedback).where(feedback.c.id == item.id).values(_feedback_values(item))
        )

    async def sent_since(
        self,
        since: datetime,
        *,
        user_id: int | None = None,
        installation_id: str | None = None,
        ip_hash: str | None = None,
    ) -> Sequence[datetime]:
        given = [
            (feedback.c[name], value)
            for name, value in (
                ("user_id", user_id),
                ("installation_id", installation_id),
                ("ip_hash", ip_hash),
            )
            if value is not None
        ]
        assert len(given) == 1, "one author at a time"
        column, value = given[0]
        result = await self._connection.execute(
            select(feedback.c.created_at)
            .where(column == value, feedback.c.created_at > since)
            .order_by(feedback.c.created_at)
        )
        return list(result.scalars())


def _feedback_values(item: Feedback) -> dict[str, Any]:
    return {
        "id": item.id,
        "type": item.type.value,
        "source": item.author.source.value,
        "text": item.text,
        "can_reply": item.can_reply,
        "contact": item.contact,
        "user_id": item.author.user_id,
        "device_id": item.author.device_id,
        "installation_id": item.author.installation_id,
        "ip_hash": item.author.ip_hash,
        "app_version": item.app_version,
        "platform": item.platform,
        "error_code": None if item.error is None else item.error.code,
        "trace_id": None if item.error is None else item.error.trace_id,
        "diagnostics": item.diagnostics,
        "created_at": item.created_at,
        "forwarded_at": item.forwarded_at,
    }


def _user(row: RowMapping) -> User:
    return User(
        id=row["id"],
        profile=TelegramProfile(
            first_name=row["first_name"],
            last_name=row["last_name"],
            username=row["username"],
            language_code=row["language_code"],
        ),
        bot_blocked=row["bot_blocked"],
        notifications=row["notifications"],
        created_at=row["created_at"],
        updated_at=row["updated_at"],
    )


def _device(row: RowMapping) -> Device:
    return Device(
        id=row["id"],
        user_id=row["user_id"],
        name=row["name"],
        os=DeviceOs(row["os"]),
        key_hash=row["key_hash"],
        linked_at=row["linked_at"],
        installation_id=row["installation_id"],
    )


def _link(row: RowMapping) -> LinkRequest:
    return LinkRequest(
        code=row["code"],
        poll_token_hash=row["poll_token_hash"],
        device_name=row["device_name"],
        device_os=DeviceOs(row["device_os"]),
        status=LinkStatus(row["status"]),
        user_id=row["user_id"],
        device_id=row["device_id"],
        created_at=row["created_at"],
        expires_at=row["expires_at"],
        installation_id=row["installation_id"],
        platform=row["platform"],
        app_version=row["app_version"],
        ip_hash=row["ip_hash"],
    )


def _pack(row: RowMapping) -> Pack:
    return Pack(
        name=row["name"],
        owner_id=row["owner_id"],
        title=row["title"],
        kind=PackKind(row["kind"]),
        created_at=row["created_at"],
        updated_at=row["updated_at"],
    )


def _feedback(row: RowMapping) -> Feedback:
    return Feedback(
        id=row["id"],
        type=FeedbackType(row["type"]),
        text=row["text"],
        author=Author(
            source=FeedbackSource(row["source"]),
            user_id=row["user_id"],
            device_id=row["device_id"],
            installation_id=row["installation_id"],
            ip_hash=row["ip_hash"],
        ),
        can_reply=row["can_reply"],
        contact=row["contact"],
        app_version=row["app_version"],
        platform=row["platform"],
        error=None
        if row["error_code"] is None
        else ReportedError(row["error_code"], row["trace_id"]),
        diagnostics=row["diagnostics"],
        created_at=row["created_at"],
        forwarded_at=row["forwarded_at"],
    )
