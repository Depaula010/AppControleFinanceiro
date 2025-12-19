"""
ORM Model para Agendamentos de Transações.

Este módulo mapeia a tabela 'Agendamentos' que armazena transações
recorrentes ou parceladas agendadas para execução automática.
"""

from datetime import date
from typing import Optional

from sqlalchemy import (
    Integer,
    String,
    Numeric,
    Date,
    Boolean,
    ForeignKey,
    CheckConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship
from decimal import Decimal

from .base import Base, TimestampMixin


class ScheduleModel(Base, TimestampMixin):
    """
    Modelo ORM para Agendamentos de Transações Recorrentes.

    Representa transações que se repetem periodicamente (ex: salário, aluguel)
    ou parceladas (ex: compra em 12x).

    Tipos de Agendamento:
    - FIXO: Transação recorrente de valor fixo (ex: salário)
    - PARCELADO: Transação parcelada com total definido (ex: compra 12x)
    - LEMBRETE_VARIAVEL: Apenas lembrete para transação manual de valor variável

    Periodicidade:
    - DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL

    Attributes:
        id: Identificador único do agendamento
        usuario_id: ID do usuário dono do agendamento
        conta_id: ID da conta onde será lançada a transação
        subcategoria_id: ID da subcategoria da transação
        descricao: Descrição do agendamento
        valor_previsto: Valor previsto da transação (NULL para LEMBRETE_VARIAVEL)
        tipo_agendamento: Tipo (FIXO, PARCELADO, LEMBRETE_VARIAVEL)
        periodicidade: Frequência de execução
        data_inicio: Data de início do agendamento
        dia_execucao: Dia do mês para execução (1-31)
        total_parcelas: Total de parcelas (apenas para PARCELADO)
        parcelas_executadas: Quantidade de parcelas já executadas
        notificar_antes_dias: Dias de antecedência para notificar (padrão: 3)
        ativo: Se o agendamento está ativo
        created_at: Data/hora de criação do registro
        updated_at: Data/hora da última atualização

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário dono do agendamento
        account: Conta onde será lançada a transação
        sub_category: Subcategoria da transação

    Constraints:
        - tipo_agendamento deve ser 'FIXO', 'PARCELADO' ou 'LEMBRETE_VARIAVEL'
        - periodicidade deve ser 'DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL'
        - dia_execucao entre 1 e 31
        - Se tipo != PARCELADO, então total_parcelas = NULL e parcelas_executadas = 0
    """

    __tablename__ = "Agendamentos"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do agendamento"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário"
    )

    conta_id: Mapped[int] = mapped_column(
        ForeignKey("Contas.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID da conta"
    )

    subcategoria_id: Mapped[int] = mapped_column(
        ForeignKey("SubCategoria.id", ondelete="RESTRICT"),
        nullable=False,
        index=True,
        comment="ID da subcategoria"
    )

    descricao: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        comment="Descrição do agendamento"
    )

    valor_previsto: Mapped[Optional[Decimal]] = mapped_column(
        Numeric(15, 2),
        nullable=True,
        comment="Valor previsto (NULL para LEMBRETE_VARIAVEL)"
    )

    tipo_agendamento: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        index=True,
        comment="Tipo: FIXO, PARCELADO, LEMBRETE_VARIAVEL"
    )

    periodicidade: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        comment="Frequência: DIARIA, SEMANAL, QUINZENAL, MENSAL, ANUAL"
    )

    data_inicio: Mapped[date] = mapped_column(
        Date,
        nullable=False,
        index=True,
        comment="Data de início do agendamento"
    )

    dia_execucao: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        comment="Dia do mês para execução (1-31)"
    )

    total_parcelas: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="Total de parcelas (apenas para PARCELADO)"
    )

    parcelas_executadas: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        default=0,
        server_default="0",
        comment="Quantidade de parcelas executadas"
    )

    notificar_antes_dias: Mapped[int] = mapped_column(
        Integer,
        nullable=False,
        default=3,
        server_default="3",
        comment="Dias de antecedência para notificar"
    )

    ativo: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        index=True,
        comment="Se o agendamento está ativo"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "tipo_agendamento IN ('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')",
            name="ck_agendamentos_tipo"
        ),
        CheckConstraint(
            "periodicidade IN ('DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL')",
            name="ck_agendamentos_periodicidade"
        ),
        CheckConstraint(
            "dia_execucao >= 1 AND dia_execucao <= 31",
            name="ck_agendamentos_dia_execucao"
        ),
        CheckConstraint(
            "(tipo_agendamento = 'PARCELADO') OR "
            "(total_parcelas IS NULL AND parcelas_executadas = 0)",
            name="chk_parcelado"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="schedules"
    # )

    # account: Mapped["AccountModel"] = relationship(
    #     "AccountModel",
    #     back_populates="schedules"
    # )

    # sub_category: Mapped["SubCategoryModel"] = relationship(
    #     "SubCategoryModel",
    #     back_populates="schedules"
    # )

    # Propriedades de conveniência
    @property
    def is_fixed(self) -> bool:
        """Retorna True se é um agendamento fixo."""
        return self.tipo_agendamento == "FIXO"

    @property
    def is_installment(self) -> bool:
        """Retorna True se é um agendamento parcelado."""
        return self.tipo_agendamento == "PARCELADO"

    @property
    def is_reminder(self) -> bool:
        """Retorna True se é apenas um lembrete variável."""
        return self.tipo_agendamento == "LEMBRETE_VARIAVEL"

    @property
    def remaining_installments(self) -> Optional[int]:
        """Retorna quantidade de parcelas restantes (apenas para parcelados)."""
        if not self.is_installment or self.total_parcelas is None:
            return None
        return self.total_parcelas - self.parcelas_executadas

    @property
    def is_completed(self) -> bool:
        """Retorna True se todas as parcelas foram executadas."""
        if not self.is_installment or self.total_parcelas is None:
            return False
        return self.parcelas_executadas >= self.total_parcelas

    @property
    def completion_percentage(self) -> Optional[float]:
        """Retorna percentual de conclusão (apenas para parcelados)."""
        if not self.is_installment or self.total_parcelas is None or self.total_parcelas == 0:
            return None
        return (self.parcelas_executadas / self.total_parcelas) * 100

    def __repr__(self) -> str:
        return (
            f"<ScheduleModel(id={self.id}, "
            f"descricao='{self.descricao}', "
            f"tipo='{self.tipo_agendamento}', "
            f"periodicidade='{self.periodicidade}', "
            f"ativo={self.ativo})>"
        )
