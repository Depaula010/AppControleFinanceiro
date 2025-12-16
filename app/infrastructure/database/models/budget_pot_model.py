"""
ORM Model para Potes de Gastos (Budget Pots).

Este módulo mapeia a tabela 'PotesDeGastos' que armazena limites de gastos
por categoria, permitindo controle orçamentário.
"""

from datetime import date
from decimal import Decimal

from sqlalchemy import (
    Integer,
    String,
    Numeric,
    Date,
    Boolean,
    ForeignKey,
    CheckConstraint,
    UniqueConstraint,
    Table,
    Column,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


# Tabela associativa Pote <-> SubCategorias (Many-to-Many)
pote_subcategorias = Table(
    "PoteSubCategorias",
    Base.metadata,
    Column("id", Integer, primary_key=True, autoincrement=True),
    Column(
        "pote_id",
        Integer,
        ForeignKey("PotesDeGastos.id", ondelete="CASCADE"),
        nullable=False,
        index=True
    ),
    Column(
        "subcategoria_id",
        Integer,
        ForeignKey("SubCategoria.id", ondelete="CASCADE"),
        nullable=False,
        index=True
    ),
    UniqueConstraint("pote_id", "subcategoria_id", name="uq_pote_subcat")
)


class BudgetPotModel(Base):
    """
    Modelo ORM para Potes de Gastos (Budget Pots).

    Representa um limite de gastos para um conjunto de subcategorias,
    com periodicidade definida (semanal, quinzenal, mensal, anual).

    Exemplo: Pote "Lazer" com limite de R$ 500/mês para subcategorias
    "Restaurante", "Cinema", "Viagens de Lazer".

    Attributes:
        id: Identificador único do pote
        usuario_id: ID do usuário dono do pote
        nome_pote: Nome do pote de gastos
        valor_limite: Limite máximo de gastos para o período
        periodicidade: Frequência do limite (SEMANAL, QUINZENAL, MENSAL, ANUAL)
        data_inicio: Data de início do controle
        ativo: Se o pote está ativo

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário dono do pote
        sub_categories: Lista de subcategorias associadas ao pote (Many-to-Many)

    Constraints:
        - periodicidade deve ser 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL'
        - Combinação (usuario_id, nome_pote) deve ser única
    """

    __tablename__ = "PotesDeGastos"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do pote de gastos"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário"
    )

    nome_pote: Mapped[str] = mapped_column(
        String(100),
        nullable=False,
        comment="Nome do pote de gastos"
    )

    valor_limite: Mapped[Decimal] = mapped_column(
        Numeric(15, 2),
        nullable=False,
        comment="Limite máximo de gastos para o período"
    )

    periodicidade: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default="MENSAL",
        server_default="MENSAL",
        comment="Frequência: SEMANAL, QUINZENAL, MENSAL, ANUAL"
    )

    data_inicio: Mapped[date] = mapped_column(
        Date,
        nullable=False,
        server_default="CURRENT_DATE",
        comment="Data de início do controle"
    )

    ativo: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        index=True,
        comment="Se o pote está ativo"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "periodicidade IN ('SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL')",
            name="ck_potes_periodicidade"
        ),
        UniqueConstraint(
            "usuario_id",
            "nome_pote",
            name="uq_potes_usuario_nome"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="budget_pots"
    # )

    # sub_categories: Mapped[list["SubCategoryModel"]] = relationship(
    #     secondary=pote_subcategorias,
    #     back_populates="budget_pots"
    # )

    # Propriedades de conveniência
    @property
    def is_weekly(self) -> bool:
        """Retorna True se o pote é semanal."""
        return self.periodicidade == "SEMANAL"

    @property
    def is_biweekly(self) -> bool:
        """Retorna True se o pote é quinzenal."""
        return self.periodicidade == "QUINZENAL"

    @property
    def is_monthly(self) -> bool:
        """Retorna True se o pote é mensal."""
        return self.periodicidade == "MENSAL"

    @property
    def is_yearly(self) -> bool:
        """Retorna True se o pote é anual."""
        return self.periodicidade == "ANUAL"

    def __repr__(self) -> str:
        return (
            f"<BudgetPotModel(id={self.id}, "
            f"nome='{self.nome_pote}', "
            f"limite={self.valor_limite}, "
            f"periodicidade='{self.periodicidade}', "
            f"ativo={self.ativo})>"
        )
