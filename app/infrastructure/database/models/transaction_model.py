# app/infrastructure/database/models/transaction_model.py
"""
Modelo ORM para a tabela Transacoes.
Representa todas as transações financeiras do usuário (receitas, despesas, transferências).
"""

from decimal import Decimal
from datetime import date, datetime
from sqlalchemy import String, Numeric, Date, DateTime, Boolean, Integer, CheckConstraint, ForeignKey, Text
from sqlalchemy.orm import Mapped, mapped_column, relationship
from typing import Optional

from .base import Base


class TransactionModel(Base):
    """
    Modelo ORM para tabela Transacoes.

    Representa transações financeiras:
    - Receitas
    - Despesas
    - Transferências entre contas
    - Pagamento de faturas

    Relationships:
        - user: Usuário dono da transação
        - account: Conta onde a transação foi realizada
        - category: Categoria da transação
        - invoice: Fatura associada (se for cartão de crédito)
        - paired_transaction: Transação par (para transferências)
    """

    __tablename__ = "Transacoes"

    # Primary Key
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da transação"
    )

    # Foreign Keys
    usuario_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário"
    )

    conta_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("Contas.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID da conta onde a transação ocorreu"
    )

    subcategoria_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("SubCategoria.id", ondelete="RESTRICT"),
        nullable=False,
        index=True,
        comment="ID da subcategoria"
    )

    fatura_id: Mapped[Optional[int]] = mapped_column(
        Integer,
        ForeignKey("Faturas.id", ondelete="SET NULL"),
        nullable=True,
        index=True,
        comment="ID da fatura (se for cartão de crédito)"
    )

    transferencia_par_id: Mapped[Optional[int]] = mapped_column(
        Integer,
        ForeignKey("Transacoes.id", ondelete="SET NULL"),
        nullable=True,
        comment="ID da transação par (para transferências)"
    )

    # Dados da transação
    descricao: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        comment="Descrição da transação"
    )

    valor: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Valor da transação"
    )

    data_transacao: Mapped[date] = mapped_column(
        Date,
        nullable=False,
        index=True,
        comment="Data em que a transação ocorreu"
    )

    tipo_transacao: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        index=True,
        comment="Tipo: Receita, Despesa, Transferência"
    )

    # Metadados
    observacoes: Mapped[Optional[str]] = mapped_column(
        Text,
        nullable=True,
        comment="Observações adicionais"
    )

    local: Mapped[Optional[str]] = mapped_column(
        String(255),
        nullable=True,
        comment="Local onde a transação ocorreu"
    )

    tags: Mapped[Optional[str]] = mapped_column(
        String(500),
        nullable=True,
        comment="Tags separadas por vírgula"
    )

    # Flags e status
    consolidada: Mapped[bool] = mapped_column(
        Boolean,
        default=True,
        nullable=False,
        comment="Se a transação está confirmada/consolidada"
    )

    recorrente: Mapped[bool] = mapped_column(
        Boolean,
        default=False,
        nullable=False,
        comment="Se é uma transação recorrente"
    )

    agendamento_id: Mapped[Optional[int]] = mapped_column(
        Integer,
        ForeignKey("Agendamentos.id", ondelete="SET NULL"),
        nullable=True,
        comment="ID do agendamento que gerou esta transação"
    )

    # Controle de origem
    origem: Mapped[str] = mapped_column(
        String(50),
        default="manual",
        nullable=False,
        comment="Origem: manual, automate, whatsapp, agendamento"
    )

    # Timestamps
    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default="CURRENT_TIMESTAMP",
        nullable=False,
        comment="Data/hora de criação"
    )

    updated_at: Mapped[Optional[datetime]] = mapped_column(
        DateTime(timezone=True),
        onupdate="CURRENT_TIMESTAMP",
        nullable=True,
        comment="Data/hora da última atualização"
    )

    # Relationships
    # user: Mapped["UserModel"] = relationship(back_populates="transactions")
    # account: Mapped["AccountModel"] = relationship(back_populates="transactions")
    # category: Mapped["SubCategoryModel"] = relationship()
    # invoice: Mapped[Optional["InvoiceModel"]] = relationship()
    # paired_transaction: Mapped[Optional["TransactionModel"]] = relationship(
    #     remote_side=[id]  # Self-referential relationship
    # )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "tipo_transacao IN ('Receita', 'Despesa', 'Transferência')",
            name="check_tipo_transacao"
        ),
        CheckConstraint(
            "valor >= 0",
            name="check_valor_positivo"
        ),
        CheckConstraint(
            "origem IN ('manual', 'automate', 'whatsapp', 'agendamento', 'api')",
            name="check_origem"
        ),
    )

    def __repr__(self) -> str:
        return (
            f"<TransactionModel(id={self.id}, tipo='{self.tipo_transacao}', "
            f"valor={self.valor}, descricao='{self.descricao[:30]}...')>"
        )

    def __str__(self) -> str:
        return f"{self.tipo_transacao}: {self.descricao} - R$ {self.valor}"

    @property
    def is_income(self) -> bool:
        """Verifica se é uma receita."""
        return self.tipo_transacao == "Receita"

    @property
    def is_expense(self) -> bool:
        """Verifica se é uma despesa."""
        return self.tipo_transacao == "Despesa"

    @property
    def is_transfer(self) -> bool:
        """Verifica se é uma transferência."""
        return self.tipo_transacao == "Transferência"

    @property
    def is_credit_card(self) -> bool:
        """Verifica se é uma transação de cartão de crédito (tem fatura)."""
        return self.fatura_id is not None
