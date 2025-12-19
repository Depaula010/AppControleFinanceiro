"""
Implementação do repositório de usuários usando SQLAlchemy.
"""

from typing import Optional
from datetime import datetime, timezone
from sqlalchemy.orm import Session
from app.infrastructure.database.models import UserModel
from app.domain.repositories import IUserRepository
from .sqlalchemy_base_repository import SQLAlchemyBaseRepository


class SQLAlchemyUserRepository(SQLAlchemyBaseRepository[UserModel]):
    """
    Repositório de usuários com SQLAlchemy.

    Implementa IUserRepository com operações específicas de usuário.
    """

    def __init__(self, session: Session):
        """
        Inicializa repositório de usuários.

        Args:
            session: Sessão SQLAlchemy
        """
        super().__init__(session, UserModel)

    def get_by_whatsapp(self, numero_whatsapp: str) -> Optional[UserModel]:
        """
        Busca usuário por número WhatsApp.

        Args:
            numero_whatsapp: Número do WhatsApp

        Returns:
            Usuário ou None
        """
        return self.session.query(UserModel).filter(
            UserModel.numero_whatsapp == numero_whatsapp
        ).first()

    def get_by_api_key(self, api_key: str) -> Optional[UserModel]:
        """
        Busca usuário por API key.

        Args:
            api_key: API key do Automate

        Returns:
            Usuário ou None
        """
        return self.session.query(UserModel).filter(
            UserModel.api_key_automate == api_key
        ).first()

    def get_by_email(self, email: str) -> Optional[UserModel]:
        """
        Busca usuário por email.

        Args:
            email: Email do usuário

        Returns:
            Usuário ou None
        """
        return self.session.query(UserModel).filter(
            UserModel.email == email
        ).first()

    def get_active_users(self) -> list[UserModel]:
        """
        Lista apenas usuários ativos.

        Returns:
            Lista de usuários com ativo=True
        """
        return self.session.query(UserModel).filter(
            UserModel.ativo == True
        ).all()

    def activate(self, id: int) -> bool:
        """
        Ativa usuário.

        Args:
            id: ID do usuário

        Returns:
            True se ativou
        """
        result = self.update(id, ativo=True)
        return result is not None

    def deactivate(self, id: int) -> bool:
        """
        Desativa usuário.

        Args:
            id: ID do usuário

        Returns:
            True se desativou
        """
        result = self.update(id, ativo=False)
        return result is not None

    def update_last_access(self, id: int) -> bool:
        """
        Atualiza timestamp de último acesso.

        Args:
            id: ID do usuário

        Returns:
            True se atualizou
        """
        now = datetime.now(timezone.utc)
        result = self.update(id, ultimo_acesso=now)
        return result is not None

    def exists_by_whatsapp(self, numero_whatsapp: str) -> bool:
        """
        Verifica se existe usuário com este WhatsApp.

        Args:
            numero_whatsapp: Número do WhatsApp

        Returns:
            True se existe
        """
        return self.session.query(
            self.session.query(UserModel).filter(
                UserModel.numero_whatsapp == numero_whatsapp
            ).exists()
        ).scalar()

    def exists_by_email(self, email: str) -> bool:
        """
        Verifica se existe usuário com este email.

        Args:
            email: Email do usuário

        Returns:
            True se existe
        """
        return self.session.query(
            self.session.query(UserModel).filter(
                UserModel.email == email
            ).exists()
        ).scalar()
