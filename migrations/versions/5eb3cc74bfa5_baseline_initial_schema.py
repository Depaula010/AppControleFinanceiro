"""baseline_initial_schema

IMPORTANTE: Esta é uma BASELINE MIGRATION para um banco de dados existente.

Esta migração marca o ponto de partida do controle de versão do schema usando Alembic.
O banco de dados JÁ EXISTE em produção com todas as tabelas criadas manualmente.

COMO USAR:
1. NÃO executar 'alembic upgrade' - as tabelas já existem!
2. Apenas marcar como aplicada: alembic stamp 5eb3cc74bfa5
3. Futuras migrações serão incrementais a partir deste ponto

TABELAS EXISTENTES (já criadas manualmente):
- Usuarios
- Contas
- Transacoes
- Faturas
- GrupoCategoria
- MacroCategoria
- SubCategoria
- Agendamentos
- PotesDeGastos
- NotificationConfig
- MonthlyReportConfig
- GoogleCalendarToken
- ApiKeys
- Consent
- baileys_auth

Revision ID: 5eb3cc74bfa5
Revises:
Create Date: 2025-12-16 16:40:31.141988

"""
from typing import Sequence, Union

from alembic import op
import sqlalchemy as sa


# revision identifiers, used by Alembic.
revision: str = '5eb3cc74bfa5'
down_revision: Union[str, None] = None
branch_labels: Union[str, Sequence[str], None] = None
depends_on: Union[str, Sequence[str], None] = None


def upgrade() -> None:
    pass


def downgrade() -> None:
    pass
