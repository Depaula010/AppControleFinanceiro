"""
ORM Model para Faturas de Cartão de Crédito.

Este módulo mapeia a tabela 'Faturas' que armazena as faturas mensais
de cartões de crédito, incluindo datas de vencimento e fechamento.
"""

from datetime import date
from typing import Optional

from sqlalchemy import (
    Integer,
    ForeignKey,
    Date,
    String,
    CheckConstraint,
    UniqueConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base, TimestampMixin


class InvoiceModel(Base, TimestampMixin):
    """
    Modelo ORM para Faturas de Cartão de Crédito.

    Representa uma fatura mensal de um cartão de crédito, com datas de
    fechamento e vencimento. Cada fatura está associada a uma conta
    (que deve ser do tipo "Cartão de Crédito").

    Status possíveis:
    - Aberta: Fatura ainda em período de lançamentos (antes do fechamento)
    - Fechada: Fatura fechada, aguardando pagamento
    - Paga: Fatura já paga

    Attributes:
        id: Identificador único da fatura
        conta_id: ID da conta (cartão de crédito) associada
        data_vencimento: Data de vencimento da fatura
        data_fechamento: Data de fechamento da fatura (último dia para lançamentos)
        status: Status da fatura (Aberta, Fechada, Paga)
        created_at: Data/hora de criação do registro
        updated_at: Data/hora da última atualização

    Relationships (comentadas até todos os modelos estarem criados):
        account: Conta (cartão de crédito) associada à fatura
        transactions: Transações vinculadas a esta fatura

    Constraints:
        - Cada combinação conta_id + data_vencimento deve ser única
        - Status deve ser 'Aberta', 'Fechada' ou 'Paga'
    """

    __tablename__ = "Faturas"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da fatura"
    )

    conta_id: Mapped[int] = mapped_column(
        ForeignKey("Contas.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID da conta (cartão de crédito)"
    )

    data_vencimento: Mapped[date] = mapped_column(
        Date,
        nullable=False,
        index=True,
        comment="Data de vencimento da fatura"
    )

    data_fechamento: Mapped[date] = mapped_column(
        Date,
        nullable=False,
        index=True,
        comment="Data de fechamento da fatura (último dia para lançamentos)"
    )

    status: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default="Aberta",
        server_default="Aberta",
        index=True,
        comment="Status da fatura: Aberta, Fechada ou Paga"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "status IN ('Aberta', 'Fechada', 'Paga')",
            name="ck_faturas_status"
        ),
        UniqueConstraint(
            "conta_id",
            "data_vencimento",
            name="uq_faturas_conta_vencimento"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # account: Mapped["AccountModel"] = relationship(
    #     "AccountModel",
    #     back_populates="invoices"
    # )

    # transactions: Mapped[list["TransactionModel"]] = relationship(
    #     "TransactionModel",
    #     back_populates="invoice",
    #     cascade="all, delete-orphan"
    # )

    # Propriedades de conveniência
    @property
    def is_open(self) -> bool:
        """Retorna True se a fatura está aberta para lançamentos."""
        return self.status == "Aberta"

    @property
    def is_closed(self) -> bool:
        """Retorna True se a fatura está fechada."""
        return self.status == "Fechada"

    @property
    def is_paid(self) -> bool:
        """Retorna True se a fatura está paga."""
        return self.status == "Paga"

    @property
    def days_until_due(self) -> int:
        """Retorna quantidade de dias até o vencimento (negativo se vencida)."""
        from datetime import date as date_class
        today = date_class.today()
        delta = self.data_vencimento - today
        return delta.days

    @property
    def is_overdue(self) -> bool:
        """Retorna True se a fatura está vencida e não paga."""
        return self.days_until_due < 0 and not self.is_paid

    def __repr__(self) -> str:
        return (
            f"<InvoiceModel(id={self.id}, "
            f"conta_id={self.conta_id}, "
            f"vencimento={self.data_vencimento}, "
            f"status='{self.status}')>"
        )
