"""
ORM Model para Autenticação Baileys (WhatsApp).

Este módulo mapeia a tabela 'baileys_auth' que armazena dados de
autenticação da biblioteca Baileys para conexão com WhatsApp.
"""

from typing import Optional

from sqlalchemy import String, Text
from sqlalchemy.orm import Mapped, mapped_column

from .base import Base


class BaileysAuthModel(Base):
    """
    Modelo ORM para Autenticação Baileys (WhatsApp).

    Armazena dados de sessão e autenticação da biblioteca Baileys,
    que é usada para conectar com a API do WhatsApp Web.

    Esta tabela funciona como um armazenamento chave-valor, onde:
    - session_id: Identifica a sessão do WhatsApp
    - data_key: Chave do dado (ex: "creds", "pre-key-1", "sender-key-1234")
    - data_value: Valor em formato JSON

    A biblioteca Baileys usa esta estrutura para persistir credenciais,
    chaves de criptografia e estado da conexão com WhatsApp.

    Attributes:
        session_id: ID da sessão do WhatsApp
        data_key: Chave do dado armazenado
        data_value: Valor do dado (geralmente JSON)

    Primary Key:
        - Chave composta: (session_id, data_key)

    Security Notes:
        - Esta tabela armazena credenciais sensíveis do WhatsApp
        - data_value contém chaves criptográficas e tokens
        - Acesso deve ser restrito apenas ao serviço de WhatsApp
        - Considerar criptografia adicional em produção
    """

    __tablename__ = "baileys_auth"

    # Chave primária composta
    session_id: Mapped[str] = mapped_column(
        String(100),
        primary_key=True,
        comment="ID da sessão do WhatsApp"
    )

    data_key: Mapped[str] = mapped_column(
        String(100),
        primary_key=True,
        comment="Chave do dado (ex: creds, pre-key-*, sender-key-*)"
    )

    # Valor
    data_value: Mapped[Optional[str]] = mapped_column(
        Text,
        nullable=True,
        comment="Valor do dado em formato JSON"
    )

    def __repr__(self) -> str:
        value_preview = self.data_value[:50] if self.data_value else "None"
        return (
            f"<BaileysAuthModel(session_id='{self.session_id}', "
            f"data_key='{self.data_key}', "
            f"data_value='{value_preview}...')>"
        )
