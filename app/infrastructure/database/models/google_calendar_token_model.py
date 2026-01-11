"""
ORM Model para Tokens do Google Calendar.

Este módulo mapeia a tabela 'GoogleCalendarTokens' que armazena
os tokens OAuth2 para integração com Google Calendar.
"""

from datetime import datetime
from typing import Optional

from sqlalchemy import (
    Integer,
    String,
    Text,
    DateTime,
    Boolean,
    ForeignKey,
)
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


class GoogleCalendarTokenModel(Base):
    """
    Modelo ORM para Tokens do Google Calendar.

    Armazena credenciais OAuth2 para integração com Google Calendar,
    permitindo que o sistema crie eventos automaticamente.

    Attributes:
        id: Identificador único do token
        usuario_id: ID do usuário dono do token
        access_token: Token de acesso OAuth2 (criptografado)
        refresh_token: Token de atualização OAuth2 (criptografado)
        token_expiry: Data/hora de expiração do access_token
        needs_reconnect: Indica se o token foi revogado/expirado e necessita reconexão manual
        created_at: Data/hora de criação do registro
        updated_at: Data/hora da última atualização

    Relationships (comentadas até todos os modelos estarem criados):
        user: Usuário dono do token

    Security Notes:
        - access_token e refresh_token devem ser criptografados antes de armazenar
        - Use ENCRYPTION_KEY do .env para criptografia/descriptografia
        - Nunca logar ou expor os tokens em texto puro
    """

    __tablename__ = "GoogleCalendarTokens"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do token"
    )

    usuario_id: Mapped[int] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=False,
        unique=True,
        index=True,
        comment="ID do usuário (único - um token por usuário)"
    )

    access_token: Mapped[str] = mapped_column(
        Text,
        nullable=False,
        comment="Token de acesso OAuth2 do Google (criptografado)"
    )

    refresh_token: Mapped[Optional[str]] = mapped_column(
        Text,
        nullable=True,
        comment="Token de atualização OAuth2 (criptografado)"
    )

    token_expiry: Mapped[Optional[datetime]] = mapped_column(
        DateTime(timezone=True),
        nullable=True,
        comment="Data/hora de expiração do access_token"
    )

    needs_reconnect: Mapped[bool] = mapped_column(
        Boolean,
        nullable=False,
        default=False,
        server_default="FALSE",
        comment="Indica se o token foi revogado/expirado e necessita reconexão manual"
    )

    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        nullable=False,
        server_default="CURRENT_TIMESTAMP",
        comment="Data/hora de criação"
    )

    updated_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        nullable=False,
        server_default="CURRENT_TIMESTAMP",
        comment="Data/hora da última atualização"
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # user: Mapped["UserModel"] = relationship(
    #     "UserModel",
    #     back_populates="google_calendar_token"
    # )

    # Propriedades de conveniência
    @property
    def is_expired(self) -> bool:
        """Retorna True se o access_token está expirado."""
        if self.token_expiry is None:
            return False
        from datetime import datetime, timezone
        return datetime.now(timezone.utc) >= self.token_expiry

    @property
    def has_refresh_token(self) -> bool:
        """Retorna True se possui refresh_token."""
        return self.refresh_token is not None

    @property
    def is_valid(self) -> bool:
        """Retorna True se o token está válido e não necessita reconexão."""
        return not self.needs_reconnect and self.has_refresh_token

    def __repr__(self) -> str:
        return (
            f"<GoogleCalendarTokenModel(id={self.id}, "
            f"usuario_id={self.usuario_id}, "
            f"expired={self.is_expired})>"
        )
