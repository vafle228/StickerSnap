from alembic import context
from sqlalchemy import engine_from_config, pool

from stickersnap_bot.infrastructure.database.tables import metadata


def run_migrations() -> None:
    config = context.config
    connectable = engine_from_config(
        config.get_section(config.config_ini_section, {}),
        prefix="sqlalchemy.",
        poolclass=pool.NullPool,
    )
    with connectable.connect() as connection:
        # SQLite cannot ALTER most things: batch mode copies the table instead
        context.configure(connection=connection, target_metadata=metadata, render_as_batch=True)
        with context.begin_transaction():
            context.run_migrations()
    connectable.dispose()


run_migrations()
