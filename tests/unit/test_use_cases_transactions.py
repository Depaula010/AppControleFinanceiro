# tests/unit/test_use_cases_transactions.py
"""
Testes unitários para Use Cases de Transactions.

Testa:
- CreateTransactionUseCase
- CreateTransferUseCase
- GetTransactionHistoryUseCase
"""

import pytest
from unittest.mock import patch, MagicMock
from datetime import date
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestCreateTransactionInput:
    """Testes para CreateTransactionInput DTO."""
    
    def test_create_input_with_required_fields(self):
        """Deve criar input com campos obrigatórios."""
        from app.application.use_cases.transactions import CreateTransactionInput
        
        input_data = CreateTransactionInput(
            usuario_id=1,
            conta_id=1,
            conta_tipo='Conta Corrente',
            subcategoria_id=1,
            descricao='Teste',
            valor=100.0,
            tipo_transacao='Despesa',
            data_transacao=date.today()
        )
        
        assert input_data.usuario_id == 1
        assert input_data.descricao == 'Teste'
        assert input_data.fatura_id is None  # Default
    
    def test_create_input_with_fatura_id(self):
        """Deve aceitar fatura_id opcional."""
        from app.application.use_cases.transactions import CreateTransactionInput
        
        input_data = CreateTransactionInput(
            usuario_id=1,
            conta_id=1,
            conta_tipo='Cartão de Crédito',
            subcategoria_id=1,
            descricao='Teste',
            valor=100.0,
            tipo_transacao='Despesa',
            data_transacao=date.today(),
            fatura_id=5
        )
        
        assert input_data.fatura_id == 5


class TestCreateTransactionOutput:
    """Testes para CreateTransactionOutput DTO."""
    
    def test_success_output(self):
        """Deve criar output de sucesso."""
        from app.application.use_cases.transactions import CreateTransactionOutput
        
        output = CreateTransactionOutput(
            success=True,
            transaction_id=1,
            message="OK"
        )
        
        assert output.success is True
        assert output.transaction_id == 1
    
    def test_failure_output(self):
        """Deve criar output de falha."""
        from app.application.use_cases.transactions import CreateTransactionOutput
        
        output = CreateTransactionOutput(
            success=False,
            message="Erro"
        )
        
        assert output.success is False
        assert output.transaction_id is None


class TestCreateTransferInput:
    """Testes para CreateTransferInput DTO."""
    
    def test_create_transfer_input(self):
        """Deve criar input de transferência."""
        from app.application.use_cases.transactions import CreateTransferInput
        
        input_data = CreateTransferInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_destino=2,
            valor=500.0,
            data_transacao=date.today()
        )
        
        assert input_data.conta_id_origem == 1
        assert input_data.conta_id_destino == 2
        assert input_data.valor == 500.0


class TestCreateTransferUseCase:
    """Testes para CreateTransferUseCase."""
    
    def test_reject_same_origin_destination(self):
        """Deve rejeitar quando origem e destino são iguais."""
        from app.application.use_cases.transactions import (
            CreateTransferInput,
            CreateTransferUseCase
        )
        
        input_data = CreateTransferInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_destino=1,  # Igual à origem
            valor=100.0,
            data_transacao=date.today()
        )
        
        use_case = CreateTransferUseCase()
        result = use_case.execute(input_data)
        
        assert result.success is False
        assert "origem e destino" in result.message.lower()
    
    def test_reject_negative_value(self):
        """Deve rejeitar valor negativo ou zero."""
        from app.application.use_cases.transactions import (
            CreateTransferInput,
            CreateTransferUseCase
        )
        
        input_data = CreateTransferInput(
            usuario_id=1,
            conta_id_origem=1,
            conta_id_destino=2,
            valor=-100.0,  # Negativo
            data_transacao=date.today()
        )
        
        use_case = CreateTransferUseCase()
        result = use_case.execute(input_data)
        
        assert result.success is False
        assert "positivo" in result.message.lower()


class TestTransactionHistoryFilter:
    """Testes para TransactionHistoryFilter DTO."""
    
    def test_create_filter_with_defaults(self):
        """Deve criar filter com valores default."""
        from app.application.use_cases.transactions import TransactionHistoryFilter
        
        filter_data = TransactionHistoryFilter(usuario_id=1)
        
        assert filter_data.usuario_id == 1
        assert filter_data.conta_id is None
        assert filter_data.tipo_transacao is None
        assert filter_data.limit == 50
        assert filter_data.offset == 0
    
    def test_create_filter_with_all_options(self):
        """Deve aceitar todos os filtros opcionais."""
        from app.application.use_cases.transactions import TransactionHistoryFilter
        
        filter_data = TransactionHistoryFilter(
            usuario_id=1,
            conta_id=2,
            tipo_transacao='Despesa',
            data_inicio=date(2025, 1, 1),
            data_fim=date(2025, 12, 31),
            categoria_id=5,
            limit=100,
            offset=50
        )
        
        assert filter_data.conta_id == 2
        assert filter_data.tipo_transacao == 'Despesa'
        assert filter_data.limit == 100


class TestTransactionItem:
    """Testes para TransactionItem DTO."""
    
    def test_create_transaction_item(self):
        """Deve criar item de transação."""
        from app.application.use_cases.transactions import TransactionItem
        
        item = TransactionItem(
            id=1,
            descricao='Supermercado',
            valor=-150.50,
            tipo_transacao='Despesa',
            data_transacao=date.today(),
            conta_nome='Nubank',
            categoria_nome='Alimentação',
            subcategoria_nome='Mercado'
        )
        
        assert item.id == 1
        assert item.descricao == 'Supermercado'
        assert item.valor == -150.50
