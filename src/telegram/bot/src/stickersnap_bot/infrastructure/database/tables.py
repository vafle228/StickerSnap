"""The schema, as SQLAlchemy Core tables. Alembic migrations must keep it (test_migrations)."""

from datetime import UTC, datetime
from typing import Any

from sqlalchemy import (
    BigInteger,
    Boolean,
    CheckConstraint,
    Column,
    DateTime,
    Dialect,
    ForeignKey,
    Index,
    Integer,
    MetaData,
    String,
    Table,
    Text,
    TypeDecorator,
    true,
)


class UtcDateTime(TypeDecorator[datetime]):
    """SQLite has no time zones: aware UTC in, naive UTC in the column, aware UTC out."""

    impl = DateTime
    cache_ok = True

    def process_bind_param(self, value: datetime | None, dialect: Dialect) -> datetime | None:
        if value is None:
            return None
        if value.tzinfo is None:
            raise ValueError("the service stores only aware datetimes")
        return value.astimezone(UTC).replace(tzinfo=None)

    def process_result_value(self, value: Any, dialect: Dialect) -> datetime | None:
        if value is None:
            return None
        assert isinstance(value, datetime)
        return value.replace(tzinfo=UTC)


# Named constraints, so later migrations (batch mode on SQLite) can find and change them
metadata = MetaData(
    naming_convention={
        "ix": "ix_%(table_name)s_%(column_0_name)s",
        "uq": "uq_%(table_name)s_%(column_0_name)s",
        "ck": "ck_%(table_name)s_%(constraint_name)s",
        "fk": "fk_%(table_name)s_%(column_0_name)s_%(referred_table_name)s",
        "pk": "pk_%(table_name)s",
    }
)

users = Table(
    "users",
    metadata,
    # Telegram's user id; it can outgrow 32 bits
    Column("id", BigInteger, primary_key=True, autoincrement=False),
    Column("first_name", String, nullable=False),
    Column("last_name", String),
    Column("username", String),
    Column("language_code", String),
    Column("bot_blocked", Boolean, nullable=False),
    # the users before migration 0003 keep the default: notifications on
    Column("notifications", Boolean, nullable=False, server_default=true()),
    Column("created_at", UtcDateTime, nullable=False),
    Column("updated_at", UtcDateTime, nullable=False),
)

devices = Table(
    "devices",
    metadata,
    Column("id", String, primary_key=True),
    Column("user_id", BigInteger, ForeignKey("users.id"), nullable=False, index=True),
    Column("name", String, nullable=False),
    Column("os", String, nullable=False),
    Column("key_hash", String, nullable=False, unique=True),
    Column("linked_at", UtcDateTime, nullable=False),
    # the same computer linking again replaces its device
    Column("installation_id", String, index=True),
    CheckConstraint("os IN ('windows', 'macos', 'linux')", name="os"),
)

link_requests = Table(
    "link_requests",
    metadata,
    Column("code", String, primary_key=True),
    Column("poll_token_hash", String, nullable=False),
    Column("device_name", String, nullable=False),
    Column("device_os", String, nullable=False),
    Column("status", String, nullable=False),
    Column("user_id", BigInteger, ForeignKey("users.id")),
    # no foreign key: unlinking deletes the device, the spent code may still be around
    Column("device_id", String),
    Column("created_at", UtcDateTime, nullable=False),
    Column("expires_at", UtcDateTime, nullable=False, index=True),
    Column("installation_id", String),
    Column("platform", String),
    Column("app_version", String),
    Column("ip_hash", String),
    CheckConstraint("device_os IN ('windows', 'macos', 'linux')", name="device_os"),
    CheckConstraint("status IN ('pending', 'confirmed', 'declined', 'claimed')", name="status"),
    # the limit on new codes counts by these over the last hour
    Index("ix_link_requests_installation_id_created_at", "installation_id", "created_at"),
    Index("ix_link_requests_ip_hash_created_at", "ip_hash", "created_at"),
)

packs = Table(
    "packs",
    metadata,
    # Telegram's set name; Telegram compares it without case, and so does the column
    Column("name", String(collation="NOCASE"), primary_key=True),
    Column("owner_id", BigInteger, ForeignKey("users.id"), nullable=False),
    Column("title", String, nullable=False),
    Column("kind", String, nullable=False),
    Column("created_at", UtcDateTime, nullable=False),
    Column("updated_at", UtcDateTime, nullable=False),
    CheckConstraint("kind IN ('stickers', 'emoji')", name="kind"),
    Index("ix_packs_owner_id_updated_at", "owner_id", "updated_at"),
)

feedback = Table(
    "feedback",
    metadata,
    # the number the feedback chat shows («#41»)
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column("type", String, nullable=False),
    Column("source", String, nullable=False),
    Column("text", String, nullable=False),
    Column("can_reply", Boolean, nullable=False),
    Column("contact", String),
    Column("user_id", BigInteger, ForeignKey("users.id")),
    # no foreign key: unlinking deletes the device, its feedback stays
    Column("device_id", String),
    Column("installation_id", String),
    Column("ip_hash", String),
    Column("app_version", String),
    Column("platform", String),
    Column("error_code", String),
    Column("trace_id", String),
    Column("diagnostics", Text),
    Column("created_at", UtcDateTime, nullable=False),
    # empty while Telegram has not taken it to the feedback chat
    Column("forwarded_at", UtcDateTime),
    CheckConstraint("type IN ('bug', 'idea', 'other')", name="type"),
    CheckConstraint("source IN ('desktop', 'miniapp')", name="source"),
    # the limits count by these over the last hour
    Index("ix_feedback_user_id_created_at", "user_id", "created_at"),
    Index("ix_feedback_installation_id_created_at", "installation_id", "created_at"),
    Index("ix_feedback_ip_hash_created_at", "ip_hash", "created_at"),
)
