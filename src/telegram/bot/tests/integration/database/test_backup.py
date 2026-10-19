"""The daily copy on a real SQLite file: what the backup container of the VPS runs."""

import sqlite3
from collections.abc import Iterator
from datetime import UTC, datetime, timedelta
from pathlib import Path

import pytest

from stickersnap_bot.infrastructure.database.backup import back_up

DAY = datetime(2026, 10, 19, 3, 0, tzinfo=UTC)


@pytest.fixture
def database(database_path: Path) -> Iterator[Path]:
    connection = sqlite3.connect(database_path)
    # WAL, as the service runs it: the row may still sit in the -wal file
    connection.execute("pragma journal_mode=wal")
    connection.execute(
        "insert into users (id, first_name, bot_blocked, created_at, updated_at) "
        "values (42, 'Дима', 0, '2026-10-19', '2026-10-19')"
    )
    connection.commit()
    yield database_path
    connection.close()


def _names(path: Path) -> list[tuple[str]]:
    connection = sqlite3.connect(path)
    try:
        return connection.execute("select first_name from users").fetchall()
    finally:
        connection.close()


def test_the_copy_holds_the_data_and_is_named_by_day(database: Path, tmp_path: Path) -> None:
    copy = back_up(database, tmp_path / "backups", DAY, keep=14)

    assert copy.name == "bot-2026-10-19.sqlite3"
    assert _names(copy) == [("Дима",)]
    assert [p.name for p in copy.parent.iterdir()] == [copy.name]


def test_a_second_run_on_one_day_replaces_that_days_copy(database: Path, tmp_path: Path) -> None:
    folder = tmp_path / "backups"
    back_up(database, folder, DAY, keep=14)

    back_up(database, folder, DAY + timedelta(hours=5), keep=14)

    assert [p.name for p in folder.iterdir()] == ["bot-2026-10-19.sqlite3"]


def test_only_the_last_days_are_kept(database: Path, tmp_path: Path) -> None:
    folder = tmp_path / "backups"
    for day in range(5):
        back_up(database, folder, DAY + timedelta(days=day), keep=3)

    assert sorted(p.name for p in folder.iterdir()) == [
        "bot-2026-10-21.sqlite3",
        "bot-2026-10-22.sqlite3",
        "bot-2026-10-23.sqlite3",
    ]


def test_a_missing_database_is_an_error_not_an_empty_copy(tmp_path: Path) -> None:
    folder = tmp_path / "backups"

    with pytest.raises(sqlite3.OperationalError):
        back_up(tmp_path / "nothing.sqlite3", folder, DAY, keep=14)

    assert not (tmp_path / "nothing.sqlite3").exists()
    assert not list(folder.glob("bot-*.sqlite3"))


def test_keeping_no_copy_is_refused(database: Path, tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="at least one"):
        back_up(database, tmp_path / "backups", DAY, keep=0)
