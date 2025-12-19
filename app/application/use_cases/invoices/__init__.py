# app/application/use_cases/invoices/__init__.py
"""
Use Cases de Faturas.

Módulos:
- GetCurrentInvoiceUseCase: Consultar faturas abertas
- PayInvoiceUseCase: Pagar fatura
"""

from .get_current_invoice import (
    GetCurrentInvoiceInput,
    InvoiceInfo,
    GetCurrentInvoiceOutput,
    GetCurrentInvoiceUseCase,
)

from .pay_invoice import (
    PayInvoiceInput,
    PayInvoiceOutput,
    PayInvoiceUseCase,
)


__all__ = [
    # Get Current Invoice
    'GetCurrentInvoiceInput',
    'InvoiceInfo',
    'GetCurrentInvoiceOutput',
    'GetCurrentInvoiceUseCase',
    # Pay Invoice
    'PayInvoiceInput',
    'PayInvoiceOutput',
    'PayInvoiceUseCase',
]
