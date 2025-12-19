# app/infrastructure/database/models/user_model.py
"""
Modelo ORM para a tabela Usuarios.
Representa os usuários do sistema com suas configurações e credenciais.
"""

from datetime import datetime
from sqlalchemy import String, Boolean, DateTime, Integer
from sqlalchemy.orm import Mapped, mapped_column, relationship
from typing import List, Optional

from .base import Base, TimestampMixin


class UserModel(Base, TimestampMixin):
    """
    Modelo ORM para tabela Usuarios.

    Representa um usuário do sistema com suas configurações,
    API keys e relacionamentos com outras entidades.

    Relationships:
        - accounts: Lista de contas bancárias do usuário
        - transactions: Lista de transações do usuário
        - schedules: Lista de agendamentos do usuário
        - categories: Categorias customizadas do usuário
    """

    __tablename__ = "Usuarios"

    # Primary Key
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do usuário"
    )

    # Dados pessoais
    nome: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        comment="Nome completo do usuário"
    )

    # Contato
    numero_whatsapp: Mapped[str] = mapped_column(
        String(50),
        unique=True,
        nullable=False,
        index=True,
        comment="Número WhatsApp (único, usado para autenticação)"
    )

    email: Mapped[Optional[str]] = mapped_column(
        String(255),
        unique=True,
        nullable=True,
        index=True,
        comment="Email do usuário (opcional)"
    )

    # API Keys
    api_key_automate: Mapped[Optional[str]] = mapped_column(
        String(100),
        unique=True,
        nullable=True,
        index=True,
        comment="API key para integração com Automate (Android)"
    )

    # Configurações
    conta_padrao_id: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="ID da conta padrão para transações"
    )

    fuso_horario: Mapped[str] = mapped_column(
        String(50),
        default="America/Sao_Paulo",
        nullable=False,
        comment="Timezone do usuário"
    )

    # Status
    ativo: Mapped[bool] = mapped_column(
        Boolean,
        default=True,
        nullable=False,
        comment="Se o usuário está ativo no sistema"
    )

    ultimo_acesso: Mapped[Optional[datetime]] = mapped_column(
        DateTime(timezone=True),
        nullable=True,
        comment="Data/hora do último acesso"
    )

    # Relationships (lazy loading)
    # Nota: Definir back_populates nos modelos relacionados
    # accounts: Mapped[List["AccountModel"]] = relationship(
    #     back_populates="user",
    #     cascade="all, delete-orphan",
    #     lazy="select"
    # )

    def __repr__(self) -> str:
        return f"<UserModel(id={self.id}, nome='{self.nome}', whatsapp='{self.numero_whatsapp}')>"

    def __str__(self) -> str:
        return f"{self.nome} ({self.numero_whatsapp})"
