"""main.build_app wires the real database: a signed call reaches SQLite and comes back."""

from collections.abc import AsyncIterator
from datetime import UTC, datetime
from pathlib import Path

import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy import create_engine, text

from stickersnap_bot.infrastructure.database.migrate import migrate
from stickersnap_bot.infrastructure.settings import Settings
from stickersnap_bot.main import build_app
from tests.fakes import TOKEN, signed_launch


@pytest.fixture
async def client(tmp_path: Path) -> AsyncIterator[tuple[AsyncClient, Path]]:
    database = tmp_path / "data" / "bot.sqlite3"
    migrate(database)
    app = build_app(Settings(bot_token=TOKEN, database_path=database))
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as http:
        yield http, database
    # the lifespan does not run here, so close what it would close
    await app.state.close()


async def test_a_signed_call_reaches_the_database(client: tuple[AsyncClient, Path]) -> None:
    http, database = client
    launch = signed_launch(42, now=datetime.now(UTC))

    response = await http.get("/v1/miniapp/devices", headers={"Authorization": f"tma {launch}"})

    assert response.status_code == 200
    assert response.json() == {"devices": []}
    engine = create_engine(f"sqlite:///{database}")
    with engine.connect() as connection:
        names = connection.execute(text("select first_name from users where id = 42")).all()
    engine.dispose()
    assert names == [("Дима",)]
