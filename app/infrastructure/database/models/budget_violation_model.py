# app/infrastructure/database/models/budget_violation_model.py
"""
ORM Model para Violações de Potes de Gastos (Budget Violations).

Este módulo mapeia a tabela 'BudgetViolations' que armazena registros
de transações que ultrapassaram limites de potes, para auditoria.
"""

from datetime import datetime
from decimal import Decimal

from sqlalchemy import (
    Integer,
    String,
    Numeric,
    ForeignKey,
    CheckConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column

from .base import Base, TimestampMixin


class BudgetViolationModel(Base, TimestampMixin):
    """
    Modelo ORM para Violações de Potes de Gastos.

    Registra quando uma transação ultrapassa o limite de um pote,
    armazenando detalhes para auditoria e análise posterior.

    Attributes:
        id: Identificador único da violação
        usuario_id: ID do usuário
        pote_id: ID do pote que foi ultrapassado
        transacao_id: ID da transação que causou a violação (NULL se cancelada)
        valor_transacao: Valor da transação que causou a violação
        valor_limite: Limite do pote no momento da violação
        valor_gasto_antes: Total gasto no pote antes da transação
        valor_gasto_depois: Total que seria gasto após a transação
        percentual_usado: Percentual do limite usado após a transação
        acao: Ação tomada (CONFIRMADO ou CANCELADO)

    Constraints:
        - acao deve ser 'CONFIRMADO' ou 'CANCELADO'
    """

    __tablename__ = "BudgetViolations"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da violação"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário"
    )

    pote_id: Mapped[int] = mapped_column(
        ForeignKey("PotesDeGastos.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do pote que foi ultrapassado"
    )

    transacao_id: Mapped[int] = mapped_column(
        ForeignKey("Transacoes.id", ondelete="SET NULL"),
        nullable=True,
        comment="ID da transação (NULL se cancelada)"
    )

    valor_transacao: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Valor da transação que causou a violação"
    )

    valor_limite: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Limite do pote no momento"
    )

    valor_gasto_antes: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Total gasto antes da transação"
    )

    valor_gasto_depois: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Total que seria gasto após a transação"
    )

    percentual_usado: Mapped[Decimal] = mapped_column(
        Numeric(5, 2),
        nullable=False,
        comment="Percentual do limite usado após a transação"
    )

    acao: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        comment="Ação tomada: CONFIRMADO ou CANCELADO"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "acao IN ('CONFIRMADO', 'CANCELADO')",
            name="ck_budget_violations_acao"
        ),
    )

    def __repr__(self) -> str:
        return (
            f"<BudgetViolationModel(id={self.id}, "
            f"pote_id={self.pote_id}, "
            f"valor={self.valor_transacao}, "
            f"acao='{self.acao}')>"
        )
