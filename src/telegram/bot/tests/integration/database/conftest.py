from collections.abc import AsyncIterator
from pathlib import Path

import pytest
from sqlalchemy.ext.asyncio import AsyncEngine

from stickersnap_bot.application.persistence import UnitOfWorkFactory
from stickersnap_bot.infrastructure.database.migrate import migrate
from stickersnap_bot.infrastructure.database.unit_of_work import SqlUnitOfWork, create_engine


@pytest.fixture
def database_path(tmp_path: Path) -> Path:
    # a fresh file per test, made by the real migrations, as on the VPS
    path = tmp_path / "data" / "bot.sqlite3"
    migrate(path)
    return path


@pytest.fixture
async def engine(database_path: Path) -> AsyncIterator[AsyncEngine]:
    engine = create_engine(database_path)
    yield engine
    await engine.dispose()


@pytest.fixture
def unit_of_work(engine: AsyncEngine) -> UnitOfWorkFactory:
    # typed by the port: mypy checks the adapter really is a UnitOfWork
    return lambda: SqlUnitOfWork(engine)
