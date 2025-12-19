# tests/unit/test_syntax.py
"""
Testes de sintaxe para verificar que todos os módulos podem ser importados.

Este teste é crucial para garantir que não há erros de sintaxe
antes de ir para produção.
"""

import pytest
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestSyntaxFormatters:
    """Testes de sintaxe para formatters."""
    
    def test_import_financial_alert_formatter(self):
        """Deve importar FinancialAlertFormatter sem erros."""
        from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
        assert FinancialAlertFormatter is not None
    
    def test_import_currency_formatter(self):
        """Deve importar formatar_moeda sem erros."""
        from app.shared.formatters.currency_formatter import formatar_moeda
        assert formatar_moeda is not None
    
    def test_import_date_formatter(self):
        """Deve importar formatters de data sem erros."""
        from app.shared.formatters.date_formatter import (
            formatar_mes_pt,
            formatar_mes_ano_pt,
            formatar_dia_semana_pt
        )
        assert formatar_mes_pt is not None


class TestSyntaxUseCasesTransactions:
    """Testes de sintaxe para use cases de transactions."""
    
    def test_import_create_transaction(self):
        """Deve importar CreateTransactionUseCase."""
        from app.application.use_cases.transactions import (
            CreateTransactionInput,
            CreateTransactionOutput,
            CreateTransactionUseCase
        )
        assert CreateTransactionUseCase is not None
    
    def test_import_create_transfer(self):
        """Deve importar CreateTransferUseCase."""
        from app.application.use_cases.transactions import (
            CreateTransferInput,
            CreateTransferOutput,
            CreateTransferUseCase
        )
        assert CreateTransferUseCase is not None
    
    def test_import_get_history(self):
        """Deve importar GetTransactionHistoryUseCase."""
        from app.application.use_cases.transactions import (
            TransactionHistoryFilter,
            TransactionItem,
            GetTransactionHistoryOutput,
            GetTransactionHistoryUseCase
        )
        assert GetTransactionHistoryUseCase is not None


class TestSyntaxUseCasesAccounts:
    """Testes de sintaxe para use cases de accounts."""
    
    def test_import_get_balance(self):
        """Deve importar GetAccountBalanceUseCase."""
        from app.application.use_cases.accounts import (
            GetAccountBalanceInput,
            AccountBalance,
            GetAccountBalanceOutput,
            GetAccountBalanceUseCase
        )
        assert GetAccountBalanceUseCase is not None
    
    def test_import_update_balance(self):
        """Deve importar UpdateAccountBalanceUseCase."""
        from app.application.use_cases.accounts import (
            UpdateAccountBalanceInput,
            UpdateAccountBalanceOutput,
            UpdateAccountBalanceUseCase
        )
        assert UpdateAccountBalanceUseCase is not None


class TestSyntaxUseCasesInvoices:
    """Testes de sintaxe para use cases de invoices."""
    
    def test_import_get_current_invoice(self):
        """Deve importar GetCurrentInvoiceUseCase."""
        from app.application.use_cases.invoices import (
            GetCurrentInvoiceInput,
            InvoiceInfo,
            GetCurrentInvoiceOutput,
            GetCurrentInvoiceUseCase
        )
        assert GetCurrentInvoiceUseCase is not None
    
    def test_import_pay_invoice(self):
        """Deve importar PayInvoiceUseCase."""
        from app.application.use_cases.invoices import (
            PayInvoiceInput,
            PayInvoiceOutput,
            PayInvoiceUseCase
        )
        assert PayInvoiceUseCase is not None


class TestSyntaxUseCasesReports:
    """Testes de sintaxe para use cases de reports."""
    
    def test_import_monthly_report(self):
        """Deve importar GenerateMonthlyReportUseCase."""
        from app.application.use_cases.reports import (
            MonthlyReportInput,
            CategorySummary,
            MonthlyReportOutput,
            GenerateMonthlyReportUseCase
        )
        assert GenerateMonthlyReportUseCase is not None
    
    def test_import_category_report(self):
        """Deve importar GenerateCategoryReportUseCase."""
        from app.application.use_cases.reports import (
            CategoryReportInput,
            SubcategorySummary,
            CategoryDetail,
            GenerateCategoryReportOutput,
            GenerateCategoryReportUseCase
        )
        assert GenerateCategoryReportUseCase is not None


class TestSyntaxMainModule:
    """Testes de sintaxe para módulo principal de use cases."""
    
    def test_import_all_from_use_cases(self):
        """Deve importar tudo do módulo use_cases."""
        from app.application.use_cases import (
            # Transactions
            CreateTransactionUseCase,
            CreateTransferUseCase,
            GetTransactionHistoryUseCase,
            # Accounts
            GetAccountBalanceUseCase,
            UpdateAccountBalanceUseCase,
            # Invoices
            GetCurrentInvoiceUseCase,
            PayInvoiceUseCase,
            # Reports
            GenerateMonthlyReportUseCase,
            GenerateCategoryReportUseCase
        )
        
        assert CreateTransactionUseCase is not None
        assert GetAccountBalanceUseCase is not None
        assert GetCurrentInvoiceUseCase is not None
        assert GenerateMonthlyReportUseCase is not None


class TestSyntaxFinanceServices:
    """Testes de sintaxe para services de finance."""
    
    def test_import_invoice_service(self):
        """Deve importar invoice_service."""
        from app.services.finance.invoice_service import (
            get_or_create_fatura,
            ensure_current_invoice_exists,
            get_fatura_valor,
            get_fatura_id_if_credit_card
        )
        assert get_fatura_id_if_credit_card is not None
    
    def test_import_transaction_service(self):
        """Deve importar transaction_service."""
        from app.services.finance.transaction_service import (
            create_transaction,
            create_transfer_pair,
            create_fatura_payment
        )
        assert create_transaction is not None
    
    def test_import_account_service(self):
        """Deve importar account_service."""
        from app.services.finance.account_service import (
            get_user_accounts,
            get_saldo_contas,
            get_account_by_name
        )
        assert get_user_accounts is not None
