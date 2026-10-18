from pathlib import Path

import pytest

from stickersnap_bot.infrastructure.database.migrate import migrate


@pytest.fixture
def database_path(tmp_path: Path) -> Path:
    # a fresh file per test, made by the real migrations, as on the VPS
    path = tmp_path / "data" / "bot.sqlite3"
    migrate(path)
    return path
