"""
ORM Model para Consentimentos LGPD.

Este módulo mapeia a tabela 'ConsentimentoUsuario' que armazena
os consentimentos dos usuários para processamento de dados (LGPD).
"""

from datetime import datetime
from typing import Optional

from sqlalchemy import (
    Integer,
    String,
    Boolean,
    Text,
    DateTime,
    ForeignKey,
    UniqueConstraint,
    Index,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


class ConsentModel(Base):
    """
    Modelo ORM para Consentimentos LGPD.

    Armazena consentimentos explícitos dos usuários para processamento
    de dados pessoais, conforme exigido pela LGPD (Lei Geral de Proteção
    de Dados Pessoais).

    Tipos de Consentimento:
    - TERMOS_USO: Termos de uso da plataforma
    - POLITICA_PRIVACIDADE: Política de privacidade
    - PROCESSAMENTO_DADOS: Processamento de dados financeiros
    - NOTIFICACOES: Envio de notificações via WhatsApp
    - INTEGRACAO_GOOGLE: Integração com Google Calendar
    - ANALISE_IA: Análise de dados com IA (Gemini)

    Attributes:
        id: Identificador único do consentimento
        usuario_id: ID do usuário que consentiu
        tipo_consentimento: Tipo de consentimento
        versao_consentimento: Versão do termo (ex: "1.0", "2.0")
        consentimento_dado: Se o consentimento foi dado (True/False)
        data_consentimento: Data/hora em que consentimento foi registrado
        ip_consentimento: IP do usuário no momento do consentimento
        texto_consentimento: Texto completo do termo apresentado
        revogado_em: Data/hora de revogação (NULL se ainda válido)

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário que deu o consentimento

    Constraints:
        - Combinação (usuario_id, tipo_consentimento, versao_consentimento) única
        - Índice especial para consentimentos ativos (não revogados)
    """

    __tablename__ = "ConsentimentoUsuario"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do consentimento"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID do usuário"
    )

    tipo_consentimento: Mapped[str] = mapped_column(
        String(50),
        nullable=False,
        index=True,
        comment="Tipo de consentimento (ex: TERMOS_USO, POLITICA_PRIVACIDADE)"
    )

    versao_consentimento: Mapped[str] = mapped_column(
        String(20),
        nullable=False,
        comment="Versão do termo (ex: 1.0, 2.0)"
    )

    consentimento_dado: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        comment="Se o consentimento foi dado (True) ou negado (False)"
    )

    data_consentimento: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        nullable=False,
        server_default="CURRENT_TIMESTAMP",
        comment="Data/hora do consentimento"
    )

    ip_consentimento: Mapped[Optional[str]] = mapped_column(
        String(45),
        nullable=True,
        comment="IP do usuário (IPv4 ou IPv6) no momento do consentimento"
    )

    texto_consentimento: Mapped[Optional[str]] = mapped_column(
        Text,
        nullable=True,
        comment="Texto completo do termo apresentado ao usuário"
    )

    revogado_em: Mapped[Optional[datetime]] = mapped_column(
        DateTime(timezone=True),
        nullable=True,
        comment="Data/hora de revogação (NULL se ainda válido)"
    )

    # Constraints e Índices
    __table_args__ = (
        UniqueConstraint(
            "usuario_id",
            "tipo_consentimento",
            "versao_consentimento",
            name="uq_consent_user_type_version"
        ),
        Index(
            "idx_consentimento_ativo",
            "usuario_id",
            "tipo_consentimento",
            postgresql_where="consentimento_dado = TRUE AND revogado_em IS NULL"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="consents"
    # )

    # Propriedades de conveniência
    @property
    def is_active(self) -> bool:
        """Retorna True se o consentimento está ativo (dado e não revogado)."""
        return self.consentimento_dado and self.revogado_em is None

    @property
    def is_revoked(self) -> bool:
        """Retorna True se o consentimento foi revogado."""
        return self.revogado_em is not None

    def revoke(self):
        """Revoga o consentimento (define revogado_em para agora)."""
        from datetime import datetime, timezone
        self.revogado_em = datetime.now(timezone.utc)

    def __repr__(self) -> str:
        return (
            f"<ConsentModel(id={self.id}, "
            f"usuario_id={self.usuario_id}, "
            f"tipo='{self.tipo_consentimento}', "
            f"versao='{self.versao_consentimento}', "
            f"ativo={self.is_active})>"
        )
