from pathlib import Path

from alembic import command
from alembic.config import Config

SCRIPTS = Path(__file__).parent / "migrations"


def alembic_config(database_path: Path) -> Config:
    """Alembic set up in code, so the installed service needs no alembic.ini beside it.
    Migrations run over the stdlib sqlite3 driver: they are synchronous and run before the app."""
    config = Config()
    config.set_main_option("script_location", str(SCRIPTS))
    config.set_main_option("sqlalchemy.url", f"sqlite:///{database_path}")
    return config


def migrate(database_path: Path) -> None:
    """Brings the database file to the latest schema, creating it on the first start."""
    database_path.parent.mkdir(parents=True, exist_ok=True)
    command.upgrade(alembic_config(database_path), "head")
