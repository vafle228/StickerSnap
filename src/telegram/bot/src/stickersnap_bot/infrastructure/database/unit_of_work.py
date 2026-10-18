from pathlib import Path
from types import TracebackType
from typing import Any, Self

from sqlalchemy import event
from sqlalchemy.ext.asyncio import (
    AsyncConnection,
    AsyncEngine,
    AsyncTransaction,
    create_async_engine,
)

from stickersnap_bot.infrastructure.database.repositories import (
    SqlDeviceRepository,
    SqlFeedbackRepository,
    SqlLinkRepository,
    SqlPackRepository,
    SqlUserRepository,
)


def create_engine(database_path: Path) -> AsyncEngine:
    """The service's one engine over the SQLite file; the schema comes from migrate()."""
    engine = create_async_engine(f"sqlite+aiosqlite:///{database_path}")

    @event.listens_for(engine.sync_engine, "connect")
    def _on_connect(dbapi_connection: Any, _: Any) -> None:
        cursor = dbapi_connection.cursor()
        # SQLite leaves foreign keys off unless every connection asks
        cursor.execute("PRAGMA foreign_keys = ON")
        # readers do not wait for the writer; the backup is still a copy of the file
        cursor.execute("PRAGMA journal_mode = WAL")
        cursor.execute("PRAGMA busy_timeout = 5000")
        cursor.close()

    return engine


class SqlUnitOfWork:
    """One connection and one transaction; leaving without commit() rolls it back."""

    users: SqlUserRepository
    devices: SqlDeviceRepository
    links: SqlLinkRepository
    packs: SqlPackRepository
    feedback: SqlFeedbackRepository

    def __init__(self, engine: AsyncEngine) -> None:
        self._engine = engine
        self._connection: AsyncConnection | None = None
        self._transaction: AsyncTransaction | None = None

    async def __aenter__(self) -> Self:
        self._connection = await self._engine.connect()
        self._transaction = await self._connection.begin()
        self.users = SqlUserRepository(self._connection)
        self.devices = SqlDeviceRepository(self._connection)
        self.links = SqlLinkRepository(self._connection)
        self.packs = SqlPackRepository(self._connection)
        self.feedback = SqlFeedbackRepository(self._connection)
        return self

    async def __aexit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> None:
        assert self._connection is not None
        assert self._transaction is not None
        try:
            if self._transaction.is_active:
                await self._transaction.rollback()
        finally:
            await self._connection.close()

    async def commit(self) -> None:
        assert self._transaction is not None
        await self._transaction.commit()
