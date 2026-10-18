"""Initial schema: users, devices, link requests, packs

Revision ID: 0001
Revises:
Create Date: 2026-10-18 11:17:53.137917
"""

# Migrations never import the app's tables: a later change there must not rewrite history.
# Times are naive UTC in DateTime columns (tables.UtcDateTime).

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0001"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "users",
        sa.Column("id", sa.BigInteger(), autoincrement=False, nullable=False),
        sa.Column("first_name", sa.String(), nullable=False),
        sa.Column("last_name", sa.String(), nullable=True),
        sa.Column("username", sa.String(), nullable=True),
        sa.Column("language_code", sa.String(), nullable=True),
        sa.Column("bot_blocked", sa.Boolean(), nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("updated_at", sa.DateTime(), nullable=False),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_users")),
    )
    op.create_table(
        "devices",
        sa.Column("id", sa.String(), nullable=False),
        sa.Column("user_id", sa.BigInteger(), nullable=False),
        sa.Column("name", sa.String(), nullable=False),
        sa.Column("os", sa.String(), nullable=False),
        sa.Column("key_hash", sa.String(), nullable=False),
        sa.Column("linked_at", sa.DateTime(), nullable=False),
        sa.CheckConstraint("os IN ('windows', 'macos', 'linux')", name=op.f("ck_devices_os")),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], name=op.f("fk_devices_user_id_users")),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_devices")),
        sa.UniqueConstraint("key_hash", name=op.f("uq_devices_key_hash")),
    )
    with op.batch_alter_table("devices", schema=None) as batch_op:
        batch_op.create_index(batch_op.f("ix_devices_user_id"), ["user_id"], unique=False)

    op.create_table(
        "link_requests",
        sa.Column("code", sa.String(), nullable=False),
        sa.Column("poll_token_hash", sa.String(), nullable=False),
        sa.Column("device_name", sa.String(), nullable=False),
        sa.Column("device_os", sa.String(), nullable=False),
        sa.Column("status", sa.String(), nullable=False),
        sa.Column("user_id", sa.BigInteger(), nullable=True),
        sa.Column("device_id", sa.String(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("expires_at", sa.DateTime(), nullable=False),
        sa.CheckConstraint(
            "device_os IN ('windows', 'macos', 'linux')", name=op.f("ck_link_requests_device_os")
        ),
        sa.CheckConstraint(
            "status IN ('pending', 'confirmed', 'declined', 'claimed')",
            name=op.f("ck_link_requests_status"),
        ),
        sa.ForeignKeyConstraint(
            ["user_id"], ["users.id"], name=op.f("fk_link_requests_user_id_users")
        ),
        sa.PrimaryKeyConstraint("code", name=op.f("pk_link_requests")),
    )
    with op.batch_alter_table("link_requests", schema=None) as batch_op:
        batch_op.create_index(
            batch_op.f("ix_link_requests_expires_at"), ["expires_at"], unique=False
        )

    op.create_table(
        "packs",
        sa.Column("name", sa.String(collation="NOCASE"), nullable=False),
        sa.Column("owner_id", sa.BigInteger(), nullable=False),
        sa.Column("title", sa.String(), nullable=False),
        sa.Column("kind", sa.String(), nullable=False),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("updated_at", sa.DateTime(), nullable=False),
        sa.CheckConstraint("kind IN ('stickers', 'emoji')", name=op.f("ck_packs_kind")),
        sa.ForeignKeyConstraint(["owner_id"], ["users.id"], name=op.f("fk_packs_owner_id_users")),
        sa.PrimaryKeyConstraint("name", name=op.f("pk_packs")),
    )
    with op.batch_alter_table("packs", schema=None) as batch_op:
        batch_op.create_index(
            "ix_packs_owner_id_updated_at", ["owner_id", "updated_at"], unique=False
        )


def downgrade() -> None:
    with op.batch_alter_table("packs", schema=None) as batch_op:
        batch_op.drop_index("ix_packs_owner_id_updated_at")

    op.drop_table("packs")
    with op.batch_alter_table("link_requests", schema=None) as batch_op:
        batch_op.drop_index(batch_op.f("ix_link_requests_expires_at"))

    op.drop_table("link_requests")
    with op.batch_alter_table("devices", schema=None) as batch_op:
        batch_op.drop_index(batch_op.f("ix_devices_user_id"))

    op.drop_table("devices")
    op.drop_table("users")
