"""
Implementação do repositório de contas financeiras usando SQLAlchemy.
"""

from typing import Optional
from sqlalchemy.orm import Session
from app.infrastructure.database.models import AccountModel
from app.domain.repositories import IAccountRepository
from .sqlalchemy_base_repository import SQLAlchemyBaseRepository


class SQLAlchemyAccountRepository(SQLAlchemyBaseRepository[AccountModel]):
    """
    Repositório de contas financeiras com SQLAlchemy.

    Implementa IAccountRepository com operações específicas de contas.
    """

    def __init__(self, session: Session):
        """
        Inicializa repositório de contas.

        Args:
            session: Sessão SQLAlchemy
        """
        super().__init__(session, AccountModel)

    def get_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista todas as contas de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de contas
        """
        return self.session.query(AccountModel).filter(
            AccountModel.usuario_id == usuario_id
        ).order_by(
            AccountModel.ordem.nulls_last(),
            AccountModel.nome_conta
        ).all()

    def get_active_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista apenas contas ativas de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de contas ativas
        """
        return self.session.query(AccountModel).filter(
            AccountModel.usuario_id == usuario_id,
            AccountModel.ativa == True
        ).order_by(
            AccountModel.ordem.nulls_last(),
            AccountModel.nome_conta
        ).all()

    def get_credit_cards_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista apenas cartões de crédito de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de cartões de crédito
        """
        return self.session.query(AccountModel).filter(
            AccountModel.usuario_id == usuario_id,
            AccountModel.tipo_conta == 'Cartão de Crédito'
        ).order_by(
            AccountModel.nome_conta
        ).all()

    def get_by_user_and_name(
        self,
        usuario_id: int,
        nome_conta: str
    ) -> Optional[AccountModel]:
        """
        Busca conta por usuário e nome.

        Args:
            usuario_id: ID do usuário
            nome_conta: Nome da conta

        Returns:
            Conta ou None
        """
        return self.session.query(AccountModel).filter(
            AccountModel.usuario_id == usuario_id,
            AccountModel.nome_conta == nome_conta
        ).first()

    def activate(self, id: int) -> bool:
        """
        Ativa conta.

        Args:
            id: ID da conta

        Returns:
            True se ativou
        """
        result = self.update(id, ativa=True)
        return result is not None

    def deactivate(self, id: int) -> bool:
        """
        Desativa conta.

        Args:
            id: ID da conta

        Returns:
            True se desativou
        """
        result = self.update(id, ativa=False)
        return result is not None

    def exists_by_user_and_name(
        self,
        usuario_id: int,
        nome_conta: str
    ) -> bool:
        """
        Verifica se existe conta com este nome para este usuário.

        Args:
            usuario_id: ID do usuário
            nome_conta: Nome da conta

        Returns:
            True se existe
        """
        return self.session.query(
            self.session.query(AccountModel).filter(
                AccountModel.usuario_id == usuario_id,
                AccountModel.nome_conta == nome_conta
            ).exists()
        ).scalar()
