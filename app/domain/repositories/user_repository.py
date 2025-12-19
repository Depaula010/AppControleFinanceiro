"""
Interface do repositório de usuários.

Define operações específicas para manipulação de usuários.
"""

from typing import Protocol, Optional
from app.infrastructure.database.models import UserModel


class IUserRepository(Protocol):
    """
    Interface do repositório de usuários.

    Estende operações CRUD básicas com métodos específicos de usuário.

    Métodos específicos:
        - get_by_whatsapp: Buscar usuário por número WhatsApp
        - get_by_api_key: Buscar usuário por API key
        - get_active_users: Listar apenas usuários ativos
        - activate: Ativar usuário
        - deactivate: Desativar usuário
        - update_last_access: Atualizar timestamp de último acesso
    """

    def get_by_id(self, id: int) -> Optional[UserModel]:
        """Busca usuário por ID."""
        ...

    def get_by_whatsapp(self, numero_whatsapp: str) -> Optional[UserModel]:
        """
        Busca usuário por número de WhatsApp.

        Args:
            numero_whatsapp: Número do WhatsApp (único)

        Returns:
            Usuário encontrado ou None
        """
        ...

    def get_by_api_key(self, api_key: str) -> Optional[UserModel]:
        """
        Busca usuário por API key do Automate.

        Args:
            api_key: API key do Automate (único)

        Returns:
            Usuário encontrado ou None
        """
        ...

    def get_by_email(self, email: str) -> Optional[UserModel]:
        """
        Busca usuário por email.

        Args:
            email: Email do usuário (único)

        Returns:
            Usuário encontrado ou None
        """
        ...

    def get_active_users(self) -> list[UserModel]:
        """
        Lista apenas usuários ativos.

        Returns:
            Lista de usuários com ativo=True
        """
        ...

    def create(self, user: UserModel) -> UserModel:
        """Cria novo usuário."""
        ...

    def update(self, id: int, user: UserModel) -> Optional[UserModel]:
        """Atualiza usuário existente."""
        ...

    def delete(self, id: int) -> bool:
        """Deleta usuário."""
        ...

    def activate(self, id: int) -> bool:
        """
        Ativa um usuário (define ativo=True).

        Args:
            id: ID do usuário

        Returns:
            True se ativou, False se não encontrou
        """
        ...

    def deactivate(self, id: int) -> bool:
        """
        Desativa um usuário (define ativo=False).

        Args:
            id: ID do usuário

        Returns:
            True se desativou, False se não encontrou
        """
        ...

    def update_last_access(self, id: int) -> bool:
        """
        Atualiza o timestamp de último acesso do usuário.

        Args:
            id: ID do usuário

        Returns:
            True se atualizou, False se não encontrou
        """
        ...

    def exists(self, id: int) -> bool:
        """Verifica se usuário existe."""
        ...

    def exists_by_whatsapp(self, numero_whatsapp: str) -> bool:
        """Verifica se já existe usuário com este WhatsApp."""
        ...

    def exists_by_email(self, email: str) -> bool:
        """Verifica se já existe usuário com este email."""
        ...
