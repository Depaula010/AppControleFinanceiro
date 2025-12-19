# tests/unit/test_use_cases_invoices.py
"""
Testes unitários para Use Cases de Invoices.

Testa:
- GetCurrentInvoiceUseCase
- PayInvoiceUseCase
"""

import pytest
from unittest.mock import patch, MagicMock
from datetime import date
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestGetCurrentInvoiceInput:
    """Testes para GetCurrentInvoiceInput DTO."""
    
    def test_create_input_all_cards(self):
        """Deve criar input para todos os cartões."""
        from app.application.use_cases.invoices import GetCurrentInvoiceInput
        
        input_data = GetCurrentInvoiceInput(usuario_id=1)
        
        assert input_data.usuario_id == 1
        assert input_data.conta_id_cartao is None
    
    def test_create_input_specific_card(self):
        """Deve criar input para cartão específico."""
        from app.application.use_cases.invoices import GetCurrentInvoiceInput
        
        input_data = GetCurrentInvoiceInput(usuario_id=1, conta_id_cartao=5)
        
        assert input_data.conta_id_cartao == 5


class TestInvoiceInfo:
    """Testes para InvoiceInfo DTO."""
    
    def test_create_invoice_info(self):
        """Deve criar info de fatura."""
        from app.application.use_cases.invoices import InvoiceInfo
        
        info = InvoiceInfo(
            nome_cartao='Nubank',
            valor_fatura=1500.50,
            data_vencimento=date(2025, 12, 27),
            status='Aberta'
        )
        
        assert info.nome_cartao == 'Nubank'
        assert info.valor_fatura == 1500.50
        assert info.status == 'Aberta'


class TestGetCurrentInvoiceOutput:
    """Testes para GetCurrentInvoiceOutput DTO."""
    
    def test_create_success_output(self):
        """Deve criar output de sucesso."""
        from app.application.use_cases.invoices import (
            GetCurrentInvoiceOutput,
            InvoiceInfo
        )
        
        invoices = [
            InvoiceInfo(nome_cartao='A', valor_fatura=1000, 
                       data_vencimento=date.today(), status='Aberta'),
            InvoiceInfo(nome_cartao='B', valor_fatura=500, 
                       data_vencimento=date.today(), status='Aberta')
        ]
        
        output = GetCurrentInvoiceOutput(
            success=True,
            invoices=invoices,
            total=1500,
            message="OK"
        )
        
        assert output.success is True
        assert len(output.invoices) == 2
        assert output.total == 1500


class TestPayInvoiceInput:
    """Testes para PayInvoiceInput DTO."""
    
    def test_create_pay_input(self):
        """Deve criar input de pagamento."""
        from app.application.use_cases.invoices import PayInvoiceInput
        
        input_data = PayInvoiceInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_cartao=2,
            valor=1500.0,
            data_pagamento=date.today()
        )
        
        assert input_data.conta_id_origem == 1
        assert input_data.conta_id_cartao == 2
        assert input_data.valor == 1500.0


class TestPayInvoiceUseCase:
    """Testes para PayInvoiceUseCase."""
    
    def test_reject_same_account(self):
        """Deve rejeitar quando origem é o próprio cartão."""
        from app.application.use_cases.invoices import (
            PayInvoiceInput,
            PayInvoiceUseCase
        )
        
        input_data = PayInvoiceInput(
            usuario_id=1,
            conta_id_origem=2,  # Mesmo que cartão
            conta_id_cartao=2,
            valor=100.0,
            data_pagamento=date.today()
        )
        
        use_case = PayInvoiceUseCase()
        result = use_case.execute(input_data)
        
        assert result.success is False
        assert "próprio cartão" in result.message.lower()
    
    def test_reject_zero_value(self):
        """Deve rejeitar valor zero."""
        from app.application.use_cases.invoices import (
            PayInvoiceInput,
            PayInvoiceUseCase
        )
        
        input_data = PayInvoiceInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_cartao=2,
            valor=0.0,  # Zero
            data_pagamento=date.today()
        )
        
        use_case = PayInvoiceUseCase()
        result = use_case.execute(input_data)
        
        assert result.success is False
        assert "positivo" in result.message.lower()
    
    def test_reject_negative_value(self):
        """Deve rejeitar valor negativo."""
        from app.application.use_cases.invoices import (
            PayInvoiceInput,
            PayInvoiceUseCase
        )
        
        input_data = PayInvoiceInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_cartao=2,
            valor=-100.0,  # Negativo
            data_pagamento=date.today()
        )
        
        use_case = PayInvoiceUseCase()
        result = use_case.execute(input_data)
        
        assert result.success is False
