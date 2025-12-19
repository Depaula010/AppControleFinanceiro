# app/application/use_cases/transactions/__init__.py
"""
Use Cases de Transações.

Módulos:
- CreateTransactionUseCase: Criar transações simples
- CreateTransferUseCase: Criar transferências entre contas
- GetTransactionHistoryUseCase: Consultar histórico
"""

from .create_transaction import (
    CreateTransactionInput,
    CreateTransactionOutput,
    CreateTransactionUseCase,
)

from .create_transfer import (
    CreateTransferInput,
    CreateTransferOutput,
    CreateTransferUseCase,
)

from .get_history import (
    TransactionHistoryFilter,
    TransactionItem,
    GetTransactionHistoryOutput,
    GetTransactionHistoryUseCase,
)


__all__ = [
    # Create Transaction
    'CreateTransactionInput',
    'CreateTransactionOutput',
    'CreateTransactionUseCase',
    # Create Transfer
    'CreateTransferInput',
    'CreateTransferOutput',
    'CreateTransferUseCase',
    # Get History
    'TransactionHistoryFilter',
    'TransactionItem',
    'GetTransactionHistoryOutput',
    'GetTransactionHistoryUseCase',
]
