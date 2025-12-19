"""
ORM Model para Configurações de Relatório Mensal.

Este módulo mapeia a tabela 'MonthlyReportConfigs' que armazena as
preferências de envio do relatório mensal de cada usuário.
"""

from datetime import time
from typing import Optional

from sqlalchemy import (
    Integer,
    Boolean,
    String,
    Time,
    ForeignKey,
    CheckConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base, TimestampMixin


class MonthlyReportConfigModel(Base, TimestampMixin):
    """
    Modelo ORM para Configurações de Relatório Mensal.

    Armazena as preferências de envio do relatório mensal consolidado
    de finanças para cada usuário.

    O relatório pode ser enviado no início do mês (com dados do mês anterior)
    ou no fim do mês (com dados do mês atual).

    Attributes:
        usuario_id: ID do usuário (chave primária)
        ativo: Se o envio do relatório está ativo
        momento_envio: Quando enviar (INICIO_MES ou FIM_MES)
        hora_envio: Horário de envio (padrão: 08:00)
        created_at: Data/hora de criação do registro
        updated_at: Data/hora da última atualização

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário dono da configuração

    Constraints:
        - momento_envio deve ser 'INICIO_MES' ou 'FIM_MES'
    """

    __tablename__ = "MonthlyReportConfigs"

    # Chave primária
    usuario_id: Mapped[int] = mapped_column(
        Integer,
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        primary_key=True,
        comment="ID do usuário (chave primária)"
    )

    # Configurações
    ativo: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        comment="Se o envio do relatório está ativo"
    )

    momento_envio: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        default="INICIO_MES",
        server_default="INICIO_MES",
        comment="Quando enviar: INICIO_MES (dados do mês anterior) ou FIM_MES (dados do mês atual)"
    )

    hora_envio: Mapped[time] = mapped_column(
        Time,
        nullable=False,
        default=time(8, 0),
        server_default="08:00:00",
        comment="Horário de envio do relatório"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "momento_envio IN ('INICIO_MES', 'FIM_MES')",
            name="ck_monthly_report_momento"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="monthly_report_config"
    # )

    # Propriedades de conveniência
    @property
    def sends_at_month_start(self) -> bool:
        """Retorna True se envia no início do mês."""
        return self.momento_envio == "INICIO_MES"

    @property
    def sends_at_month_end(self) -> bool:
        """Retorna True se envia no fim do mês."""
        return self.momento_envio == "FIM_MES"

    def __repr__(self) -> str:
        return (
            f"<MonthlyReportConfigModel(usuario_id={self.usuario_id}, "
            f"ativo={self.ativo}, "
            f"momento='{self.momento_envio}', "
            f"hora={self.hora_envio})>"
        )
