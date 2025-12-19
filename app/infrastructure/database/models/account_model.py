# app/infrastructure/database/models/account_model.py
"""
Modelo ORM para a tabela Contas.
Representa contas bancárias, cartões de crédito e outros tipos de contas financeiras.
"""

from decimal import Decimal
from sqlalchemy import String, Numeric, Integer, Boolean, CheckConstraint, ForeignKey
from sqlalchemy.orm import Mapped, mapped_column, relationship
from typing import Optional

from .base import Base, TimestampMixin


class AccountModel(Base, TimestampMixin):
    """
    Modelo ORM para tabela Contas.

    Representa contas financeiras do usuário como:
    - Conta Corrente
    - Conta Poupança
    - Cartão de Crédito
    - Investimentos
    - Dinheiro físico

    Para cartões de crédito, armazena informações de fechamento/vencimento.
    """

    __tablename__ = "Contas"

    # Primary Key
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da conta"
    )

    # Foreign Keys
    usuario_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário dono da conta"
    )

    # Informações da conta
    nome_conta: Mapped[str] = mapped_column(
        String(100),
        nullable=False,
        comment="Nome/apelido da conta (ex: 'Nubank', 'Bradesco CC')"
    )

    tipo_conta: Mapped[str] = mapped_column(
        String(50),
        nullable=False,
        comment="Tipo da conta (Conta Corrente, Cartão de Crédito, etc)"
    )

    # Saldo
    saldo_inicial: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        default=Decimal('0.00'),
        nullable=False,
        comment="Saldo inicial da conta (base para cálculos)"
    )

    # Configurações de Cartão de Crédito
    dia_vencimento: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="Dia do vencimento da fatura (apenas cartão de crédito)"
    )

    dia_fechamento: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="Dia de fechamento da fatura (apenas cartão de crédito)"
    )

    limite_credito: Mapped[Optional[Decimal]] = mapped_column(
        Numeric(15, 2),
        nullable=True,
        comment="Limite do cartão de crédito"
    )

    # Configurações
    inclui_saldo_total: Mapped[bool] = mapped_column(
        Boolean,
        default=True,
        nullable=False,
        comment="Se deve incluir no cálculo do saldo total"
    )

    cor_hex: Mapped[Optional[str]] = mapped_column(
        String(7),
        nullable=True,
        comment="Cor da conta em hex (ex: '#FF5733') para UI"
    )

    icone: Mapped[Optional[str]] = mapped_column(
        String(50),
        nullable=True,
        comment="Nome do ícone para UI"
    )

    # Status
    ativa: Mapped[bool] = mapped_column(
        Boolean,
        default=True,
        nullable=False,
        comment="Se a conta está ativa"
    )

    ordem: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="Ordem de exibição na UI"
    )

    # Relationships
    # user: Mapped["UserModel"] = relationship(back_populates="accounts")
    # transactions: Mapped[List["TransactionModel"]] = relationship(
    #     back_populates="account",
    #     cascade="all, delete-orphan"
    # )
    # invoices: Mapped[List["InvoiceModel"]] = relationship(
    #     back_populates="account",
    #     cascade="all, delete-orphan"
    # )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "tipo_conta IN ('Conta Corrente', 'Conta Poupança', 'Investimento', "
            "'Cartão de Crédito', 'Dinheiro', 'Outro')",
            name="check_tipo_conta"
        ),
        CheckConstraint(
            "dia_vencimento IS NULL OR (dia_vencimento >= 1 AND dia_vencimento <= 31)",
            name="check_dia_vencimento"
        ),
        CheckConstraint(
            "dia_fechamento IS NULL OR (dia_fechamento >= 1 AND dia_fechamento <= 31)",
            name="check_dia_fechamento"
        ),
    )

    def __repr__(self) -> str:
        return f"<AccountModel(id={self.id}, nome='{self.nome_conta}', tipo='{self.tipo_conta}')>"

    def __str__(self) -> str:
        return f"{self.nome_conta} ({self.tipo_conta})"

    @property
    def is_credit_card(self) -> bool:
        """Verifica se a conta é um cartão de crédito."""
        return self.tipo_conta == "Cartão de Crédito"

    @property
    def has_invoice_config(self) -> bool:
        """Verifica se tem configuração de fatura (cartão de crédito)."""
        return self.dia_vencimento is not None and self.dia_fechamento is not None
