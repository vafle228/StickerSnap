"""Feedback

Revision ID: 0002
Revises: 0001
Create Date: 2026-10-18 16:23:35.395115
"""

# Migrations never import the app's tables (0001). Times are naive UTC in DateTime columns.

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op

revision: str = "0002"
down_revision: str | None = "0001"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "feedback",
        sa.Column("id", sa.Integer(), autoincrement=True, nullable=False),
        sa.Column("type", sa.String(), nullable=False),
        sa.Column("source", sa.String(), nullable=False),
        sa.Column("text", sa.String(), nullable=False),
        sa.Column("can_reply", sa.Boolean(), nullable=False),
        sa.Column("contact", sa.String(), nullable=True),
        sa.Column("user_id", sa.BigInteger(), nullable=True),
        sa.Column("device_id", sa.String(), nullable=True),
        sa.Column("installation_id", sa.String(), nullable=True),
        sa.Column("ip_hash", sa.String(), nullable=True),
        sa.Column("app_version", sa.String(), nullable=True),
        sa.Column("platform", sa.String(), nullable=True),
        sa.Column("error_code", sa.String(), nullable=True),
        sa.Column("trace_id", sa.String(), nullable=True),
        sa.Column("diagnostics", sa.Text(), nullable=True),
        sa.Column("created_at", sa.DateTime(), nullable=False),
        sa.Column("forwarded_at", sa.DateTime(), nullable=True),
        sa.CheckConstraint("source IN ('desktop', 'miniapp')", name=op.f("ck_feedback_source")),
        sa.CheckConstraint("type IN ('bug', 'idea', 'other')", name=op.f("ck_feedback_type")),
        sa.ForeignKeyConstraint(["user_id"], ["users.id"], name=op.f("fk_feedback_user_id_users")),
        sa.PrimaryKeyConstraint("id", name=op.f("pk_feedback")),
    )
    with op.batch_alter_table("feedback", schema=None) as batch_op:
        batch_op.create_index(
            "ix_feedback_installation_id_created_at",
            ["installation_id", "created_at"],
            unique=False,
        )
        batch_op.create_index(
            "ix_feedback_ip_hash_created_at", ["ip_hash", "created_at"], unique=False
        )
        batch_op.create_index(
            "ix_feedback_user_id_created_at", ["user_id", "created_at"], unique=False
        )


def downgrade() -> None:
    with op.batch_alter_table("feedback", schema=None) as batch_op:
        batch_op.drop_index("ix_feedback_user_id_created_at")
        batch_op.drop_index("ix_feedback_ip_hash_created_at")
        batch_op.drop_index("ix_feedback_installation_id_created_at")

    op.drop_table("feedback")
