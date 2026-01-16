# app/application/use_cases/__init__.py
"""
Camada de Use Cases (Application Layer).

Este módulo exporta todos os use cases disponíveis, organizados por domínio:

- **Transactions**: Criação de transações e transferências
- **Accounts**: Consulta e atualização de saldos
- **Invoices**: Consulta e pagamento de faturas
- **Reports**: Relatórios mensais e por categoria
"""

# Transactions
from .transactions import (
    CreateTransactionInput,
    CreateTransactionOutput,
    CreateTransactionUseCase,
    CreateTransferInput,
    CreateTransferOutput,
    CreateTransferUseCase,
    TransactionHistoryFilter,
    TransactionItem,
    GetTransactionHistoryOutput,
    GetTransactionHistoryUseCase,
)

# Accounts
from .accounts import (
    GetAccountBalanceInput,
    AccountBalance,
    GetAccountBalanceOutput,
    GetAccountBalanceUseCase,
    UpdateAccountBalanceInput,
    UpdateAccountBalanceOutput,
    UpdateAccountBalanceUseCase,
)

# Invoices
from .invoices import (
    GetCurrentInvoiceInput,
    InvoiceInfo,
    GetCurrentInvoiceOutput,
    GetCurrentInvoiceUseCase,
    PayInvoiceInput,
    PayInvoiceOutput,
    PayInvoiceUseCase,
)

# Reports
from .reports import (
    MonthlyReportInput,
    CategorySummary,
    MonthlyReportOutput,
    GenerateMonthlyReportUseCase,
    CategoryReportInput,
    SubcategorySummary,
    CategoryDetail,
    GenerateCategoryReportOutput,
    GenerateCategoryReportUseCase,
)

# Budget
from .budget import (
    ValidateBudgetInput,
    BudgetValidationDetail,
    ValidateBudgetOutput,
    ValidateBudgetUseCase,
)


__all__ = [
    # Transactions
    'CreateTransactionInput',
    'CreateTransactionOutput',
    'CreateTransactionUseCase',
    'CreateTransferInput',
    'CreateTransferOutput',
    'CreateTransferUseCase',
    'TransactionHistoryFilter',
    'TransactionItem',
    'GetTransactionHistoryOutput',
    'GetTransactionHistoryUseCase',
    # Accounts
    'GetAccountBalanceInput',
    'AccountBalance',
    'GetAccountBalanceOutput',
    'GetAccountBalanceUseCase',
    'UpdateAccountBalanceInput',
    'UpdateAccountBalanceOutput',
    'UpdateAccountBalanceUseCase',
    # Invoices
    'GetCurrentInvoiceInput',
    'InvoiceInfo',
    'GetCurrentInvoiceOutput',
    'GetCurrentInvoiceUseCase',
    'PayInvoiceInput',
    'PayInvoiceOutput',
    'PayInvoiceUseCase',
    # Reports
    'MonthlyReportInput',
    'CategorySummary',
    'MonthlyReportOutput',
    'GenerateMonthlyReportUseCase',
    'CategoryReportInput',
    'SubcategorySummary',
    'CategoryDetail',
    'GenerateCategoryReportOutput',
    'GenerateCategoryReportUseCase',
    # Budget
    'ValidateBudgetInput',
    'BudgetValidationDetail',
    'ValidateBudgetOutput',
    'ValidateBudgetUseCase',
]
