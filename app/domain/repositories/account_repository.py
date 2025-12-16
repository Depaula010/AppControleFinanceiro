"""
Interface do repositório de contas financeiras.

Define operações específicas para manipulação de contas.
"""

from typing import Protocol, Optional
from app.infrastructure.database.models import AccountModel


class IAccountRepository(Protocol):
    """
    Interface do repositório de contas financeiras.

    Métodos específicos:
        - get_by_user: Listar todas as contas de um usuário
        - get_active_by_user: Listar apenas contas ativas de um usuário
        - get_credit_cards_by_user: Listar apenas cartões de crédito de um usuário
        - get_default_account: Buscar conta padrão do usuário
        - activate: Ativar conta
        - deactivate: Desativar conta
    """

    def get_by_id(self, id: int) -> Optional[AccountModel]:
        """Busca conta por ID."""
        ...

    def get_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista todas as contas de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de contas do usuário
        """
        ...

    def get_active_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista apenas contas ativas de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de contas ativas (ativa=True)
        """
        ...

    def get_credit_cards_by_user(self, usuario_id: int) -> list[AccountModel]:
        """
        Lista apenas cartões de crédito de um usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Lista de contas com tipo_conta='Cartão de Crédito'
        """
        ...

    def get_by_user_and_name(
        self,
        usuario_id: int,
        nome_conta: str
    ) -> Optional[AccountModel]:
        """
        Busca conta por usuário e nome.

        Útil pois (usuario_id, nome_conta) é UNIQUE na tabela.

        Args:
            usuario_id: ID do usuário
            nome_conta: Nome da conta

        Returns:
            Conta encontrada ou None
        """
        ...

    def create(self, account: AccountModel) -> AccountModel:
        """Cria nova conta."""
        ...

    def update(self, id: int, account: AccountModel) -> Optional[AccountModel]:
        """Atualiza conta existente."""
        ...

    def delete(self, id: int) -> bool:
        """Deleta conta."""
        ...

    def activate(self, id: int) -> bool:
        """
        Ativa uma conta (define ativa=True).

        Args:
            id: ID da conta

        Returns:
            True se ativou, False se não encontrou
        """
        ...

    def deactivate(self, id: int) -> bool:
        """
        Desativa uma conta (define ativa=False).

        Args:
            id: ID da conta

        Returns:
            True se desativou, False se não encontrou
        """
        ...

    def exists(self, id: int) -> bool:
        """Verifica se conta existe."""
        ...

    def exists_by_user_and_name(self, usuario_id: int, nome_conta: str) -> bool:
        """
        Verifica se já existe conta com este nome para este usuário.

        Args:
            usuario_id: ID do usuário
            nome_conta: Nome da conta

        Returns:
            True se já existe
        """
        ...
