"""Link details: the computer's installation, platform and version; notifications on or off

Revision ID: 0003
Revises: 0002
Create Date: 2026-10-18 17:03:03.311659
"""

# Migrations never import the app's tables (0001). Times are naive UTC in DateTime columns.

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0003"
down_revision: str | None = "0002"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    with op.batch_alter_table("devices", schema=None) as batch_op:
        batch_op.add_column(sa.Column("installation_id", sa.String(), nullable=True))
        batch_op.create_index(
            batch_op.f("ix_devices_installation_id"), ["installation_id"], unique=False
        )

    with op.batch_alter_table("link_requests", schema=None) as batch_op:
        batch_op.add_column(sa.Column("installation_id", sa.String(), nullable=True))
        batch_op.add_column(sa.Column("platform", sa.String(), nullable=True))
        batch_op.add_column(sa.Column("app_version", sa.String(), nullable=True))
        batch_op.add_column(sa.Column("ip_hash", sa.String(), nullable=True))
        batch_op.create_index(
            "ix_link_requests_installation_id_created_at",
            ["installation_id", "created_at"],
            unique=False,
        )
        batch_op.create_index(
            "ix_link_requests_ip_hash_created_at", ["ip_hash", "created_at"], unique=False
        )

    with op.batch_alter_table("users", schema=None) as batch_op:
        batch_op.add_column(
            sa.Column("notifications", sa.Boolean(), server_default=sa.text("1"), nullable=False)
        )


def downgrade() -> None:
    with op.batch_alter_table("users", schema=None) as batch_op:
        batch_op.drop_column("notifications")

    with op.batch_alter_table("link_requests", schema=None) as batch_op:
        batch_op.drop_index("ix_link_requests_ip_hash_created_at")
        batch_op.drop_index("ix_link_requests_installation_id_created_at")
        batch_op.drop_column("ip_hash")
        batch_op.drop_column("app_version")
        batch_op.drop_column("platform")
        batch_op.drop_column("installation_id")

    with op.batch_alter_table("devices", schema=None) as batch_op:
        batch_op.drop_index(batch_op.f("ix_devices_installation_id"))
        batch_op.drop_column("installation_id")
