from pathlib import Path

from alembic import command
from alembic.autogenerate import compare_metadata
from alembic.migration import MigrationContext
from sqlalchemy import create_engine, inspect

from stickersnap_bot.infrastructure.database.migrate import alembic_config, migrate
from stickersnap_bot.infrastructure.database.tables import metadata


def test_migrations_build_exactly_the_tables_of_the_code(database_path: Path) -> None:
    # a change to tables.py without a migration (or the other way round) fails here
    engine = create_engine(f"sqlite:///{database_path}")
    with engine.connect() as connection:
        differences = compare_metadata(MigrationContext.configure(connection), metadata)
    engine.dispose()

    assert differences == []


def test_migrate_twice_changes_nothing(database_path: Path) -> None:
    migrate(database_path)


def test_downgrade_to_nothing_and_back(database_path: Path) -> None:
    config = alembic_config(database_path)

    command.downgrade(config, "base")
    engine = create_engine(f"sqlite:///{database_path}")
    assert inspect(engine).get_table_names() == ["alembic_version"]
    engine.dispose()

    command.upgrade(config, "head")
    engine = create_engine(f"sqlite:///{database_path}")
    assert set(inspect(engine).get_table_names()) >= {"users", "devices", "link_requests", "packs"}
    engine.dispose()
