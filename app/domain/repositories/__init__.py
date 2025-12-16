"""
Interfaces de repositórios (Domain Layer).

Este módulo contém as interfaces (Protocols) que definem os contratos
dos repositórios. As implementações concretas ficam na camada Infrastructure.

Usage:
    from app.domain.repositories import IUserRepository, IAccountRepository

    # Em um serviço:
    def __init__(self, user_repo: IUserRepository):
        self.user_repo = user_repo
"""

from .base_repository import IBaseRepository
from .user_repository import IUserRepository
from .account_repository import IAccountRepository
from .transaction_repository import ITransactionRepository

__all__ = [
    "IBaseRepository",
    "IUserRepository",
    "IAccountRepository",
    "ITransactionRepository",
]
