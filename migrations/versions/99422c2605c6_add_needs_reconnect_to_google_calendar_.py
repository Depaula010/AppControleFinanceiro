"""add_needs_reconnect_to_google_calendar_tokens

Adiciona campo needs_reconnect para marcar tokens do Google Calendar que
necessitam reconexão manual após erro de invalid_grant.

Revision ID: 99422c2605c6
Revises: 5eb3cc74bfa5
Create Date: 2026-01-09 18:06:03.150504

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '99422c2605c6'
down_revision: Union[str, None] = '5eb3cc74bfa5'
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    # Adicionar coluna needs_reconnect com default FALSE
    op.add_column(
        'GoogleCalendarTokens',
        sa.Column(
            'needs_reconnect',
            sa.Boolean(),
            nullable=False,
            server_default=sa.false(),
            comment='Indica se o token foi revogado/expirado e necessita reconexão manual'
        )
    )


def downgrade() -> None:
    # Remover coluna needs_reconnect
    op.drop_column('GoogleCalendarTokens', 'needs_reconnect')
