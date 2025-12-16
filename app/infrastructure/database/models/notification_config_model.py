"""
ORM Model para Configurações de Notificações.

Este módulo mapeia a tabela 'NotificationConfigs' que armazena as
preferências de notificações de cada usuário.
"""

from datetime import time
from typing import Optional

from sqlalchemy import (
    Integer,
    Boolean,
    Time,
    ForeignKey,
    UniqueConstraint,
    CheckConstraint,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base, TimestampMixin


class NotificationConfigModel(Base, TimestampMixin):
    """
    Modelo ORM para Configurações de Notificações do Usuário.

    Armazena as preferências de notificações para cada usuário,
    incluindo horários e ativação/desativação de cada tipo.

    Tipos de Notificações:
    - Resumo Matinal (Daily Briefing): Agenda do dia + clima
    - Alertas Financeiros: Contas e faturas a vencer hoje e amanhã
    - Check-in Noturno: Confirmação de contas pendentes (próximos 7 dias)

    Attributes:
        id: Identificador único da configuração
        usuario_id: ID do usuário (único)
        resumo_matinal_ativo: Se envia resumo matinal
        resumo_matinal_hora: Horário do resumo matinal
        alertas_financeiros_ativos: Se envia alertas financeiros
        checkin_noturno_ativo: Se envia check-in noturno
        checkin_noturno_hora: Horário do check-in noturno (18h-23h)
        created_at: Data/hora de criação do registro
        updated_at: Data/hora da última atualização

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário dono da configuração

    Constraints:
        - usuario_id deve ser único (um usuário = uma configuração)
        - checkin_noturno_hora deve estar entre 18:00 e 23:00
    """

    __tablename__ = "NotificationConfigs"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da configuração"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        unique=True,
        index=True,
        comment="ID do usuário (único)"
    )

    # Configurações de Resumo Matinal
    resumo_matinal_ativo: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        comment="Se envia resumo matinal com agenda e clima"
    )

    resumo_matinal_hora: Mapped[time] = mapped_column(
        Time,
        nullable=False,
        default=time(7, 0),
        server_default="07:00:00",
        comment="Horário único para TODAS as notificações matinais"
    )

    # Configurações de Alertas Financeiros
    alertas_financeiros_ativos: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        comment="Se inclui alertas de contas/faturas a vencer (hoje e amanhã)"
    )

    # Configurações de Check-in Noturno
    checkin_noturno_ativo: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=True,
        server_default="TRUE",
        comment="Se envia check-in noturno com contas pendentes (D-0 até D-7)"
    )

    checkin_noturno_hora: Mapped[time] = mapped_column(
        Time,
        nullable=False,
        default=time(20, 0),
        server_default="20:00:00",
        comment="Horário para envio do check-in noturno (18:00-23:00)"
    )

    # Constraints
    __table_args__ = (
        CheckConstraint(
            "checkin_noturno_hora >= '18:00:00'::TIME AND "
            "checkin_noturno_hora <= '23:00:00'::TIME",
            name="chk_checkin_hora"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="notification_config"
    # )

    # Propriedades de conveniência
    @property
    def has_morning_notifications(self) -> bool:
        """Retorna True se alguma notificação matinal está ativa."""
        return self.resumo_matinal_ativo or self.alertas_financeiros_ativos

    @property
    def all_notifications_disabled(self) -> bool:
        """Retorna True se todas as notificações estão desativadas."""
        return not (
            self.resumo_matinal_ativo or
            self.alertas_financeiros_ativos or
            self.checkin_noturno_ativo
        )

    def __repr__(self) -> str:
        return (
            f"<NotificationConfigModel(id={self.id}, "
            f"usuario_id={self.usuario_id}, "
            f"matinal={self.resumo_matinal_ativo}, "
            f"financeiro={self.alertas_financeiros_ativos}, "
            f"checkin={self.checkin_noturno_ativo})>"
        )
