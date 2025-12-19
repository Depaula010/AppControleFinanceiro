"""
Implementações concretas de repositórios usando SQLAlchemy.

Este módulo contém as implementações dos repositórios definidos
na camada de domínio.

Usage:
    from app.infrastructure.database.repositories import (
        SQLAlchemyUserRepository,
        SQLAlchemyAccountRepository,
        SQLAlchemyTransactionRepository
    )

    # Criar sessão
    session = Session(engine)

    # Instanciar repositórios
    user_repo = SQLAlchemyUserRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Usar repositórios
    user = user_repo.get_by_whatsapp("+5511999999999")
    accounts = account_repo.get_by_user(user.id)
"""

from .sqlalchemy_base_repository import SQLAlchemyBaseRepository
from .sqlalchemy_user_repository import SQLAlchemyUserRepository
from .sqlalchemy_account_repository import SQLAlchemyAccountRepository
from .sqlalchemy_transaction_repository import SQLAlchemyTransactionRepository

__all__ = [
    "SQLAlchemyBaseRepository",
    "SQLAlchemyUserRepository",
    "SQLAlchemyAccountRepository",
    "SQLAlchemyTransactionRepository",
]
