"""
Interface do repositório de transações financeiras.

Define operações específicas para manipulação de transações.
"""

from typing import Protocol, Optional
from datetime import date
from decimal import Decimal
from app.infrastructure.database.models import TransactionModel


class ITransactionRepository(Protocol):
    """
    Interface do repositório de transações financeiras.

    Métodos específicos:
        - get_by_user: Listar transações de um usuário
        - get_by_account: Listar transações de uma conta
        - get_by_period: Listar transações em um período
        - get_by_type: Listar transações por tipo (Renda, Despesa, etc)
        - get_income_by_period: Listar receitas em um período
        - get_expenses_by_period: Listar despesas em um período
        - calculate_balance: Calcular saldo de uma conta
        - consolidate: Marcar transação como consolidada
        - unconsolidate: Desmarcar transação como consolidada
    """

    def get_by_id(self, id: int) -> Optional[TransactionModel]:
        """Busca transação por ID."""
        ...

    def get_by_user(
        self,
        usuario_id: int,
        skip: int = 0,
        limit: int = 100
    ) -> list[TransactionModel]:
        """
        Lista transações de um usuário com paginação.

        Args:
            usuario_id: ID do usuário
            skip: Offset para paginação
            limit: Limite de registros

        Returns:
            Lista de transações
        """
        ...

    def get_by_account(
        self,
        conta_id: int,
        skip: int = 0,
        limit: int = 100
    ) -> list[TransactionModel]:
        """
        Lista transações de uma conta específica.

        Args:
            conta_id: ID da conta
            skip: Offset para paginação
            limit: Limite de registros

        Returns:
            Lista de transações
        """
        ...

    def get_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """
        Lista transações de um usuário em um período.

        Args:
            usuario_id: ID do usuário
            data_inicio: Data inicial (inclusive)
            data_fim: Data final (inclusive)

        Returns:
            Lista de transações no período
        """
        ...

    def get_by_type(
        self,
        usuario_id: int,
        tipo_transacao: str
    ) -> list[TransactionModel]:
        """
        Lista transações de um tipo específico.

        Args:
            usuario_id: ID do usuário
            tipo_transacao: Tipo ('Renda', 'Despesa', 'Transferência', 'Pagamento Fatura')

        Returns:
            Lista de transações do tipo especificado
        """
        ...

    def get_income_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """
        Lista apenas receitas (Renda) em um período.

        Args:
            usuario_id: ID do usuário
            data_inicio: Data inicial
            data_fim: Data final

        Returns:
            Lista de transações com tipo_transacao='Renda'
        """
        ...

    def get_expenses_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """
        Lista apenas despesas em um período.

        Args:
            usuario_id: ID do usuário
            data_inicio: Data inicial
            data_fim: Data final

        Returns:
            Lista de transações com tipo_transacao='Despesa'
        """
        ...

    def get_by_invoice(self, fatura_id: int) -> list[TransactionModel]:
        """
        Lista transações vinculadas a uma fatura de cartão.

        Args:
            fatura_id: ID da fatura

        Returns:
            Lista de transações da fatura
        """
        ...

    def calculate_balance(
        self,
        conta_id: int,
        up_to_date: Optional[date] = None
    ) -> Decimal:
        """
        Calcula saldo de uma conta até uma data.

        Args:
            conta_id: ID da conta
            up_to_date: Data limite (None = todas as transações)

        Returns:
            Saldo calculado (saldo_inicial + receitas - despesas)
        """
        ...

    def calculate_total_income(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> Decimal:
        """
        Calcula total de receitas em um período.

        Args:
            usuario_id: ID do usuário
            data_inicio: Data inicial
            data_fim: Data final

        Returns:
            Soma dos valores de receitas
        """
        ...

    def calculate_total_expenses(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> Decimal:
        """
        Calcula total de despesas em um período.

        Args:
            usuario_id: ID do usuário
            data_inicio: Data inicial
            data_fim: Data final

        Returns:
            Soma dos valores de despesas
        """
        ...

    def create(self, transaction: TransactionModel) -> TransactionModel:
        """Cria nova transação."""
        ...

    def update(
        self,
        id: int,
        transaction: TransactionModel
    ) -> Optional[TransactionModel]:
        """Atualiza transação existente."""
        ...

    def delete(self, id: int) -> bool:
        """Deleta transação."""
        ...

    def consolidate(self, id: int) -> bool:
        """
        Marca transação como consolidada (consolidada=True).

        Args:
            id: ID da transação

        Returns:
            True se marcou, False se não encontrou
        """
        ...

    def unconsolidate(self, id: int) -> bool:
        """
        Desmarca transação como consolidada (consolidada=False).

        Args:
            id: ID da transação

        Returns:
            True se desmarcou, False se não encontrou
        """
        ...

    def exists(self, id: int) -> bool:
        """Verifica se transação existe."""
        ...
