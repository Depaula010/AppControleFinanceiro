"""
Camada de serviços da aplicação.

Contém lógica de negócio que orquestra repositórios.
"""

from .user_service import UserService

__all__ = [
    "UserService",
]
