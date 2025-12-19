# tests/unit/test_use_cases_accounts.py
"""
Testes unitários para Use Cases de Accounts.

Testa:
- GetAccountBalanceUseCase
- UpdateAccountBalanceUseCase
"""

import pytest
from unittest.mock import patch, MagicMock
from datetime import date
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


class TestGetAccountBalanceInput:
    """Testes para GetAccountBalanceInput DTO."""
    
    def test_create_input_all_accounts(self):
        """Deve criar input para todas as contas."""
        from app.application.use_cases.accounts import GetAccountBalanceInput
        
        input_data = GetAccountBalanceInput(usuario_id=1)
        
        assert input_data.usuario_id == 1
        assert input_data.conta_id is None
    
    def test_create_input_specific_account(self):
        """Deve criar input para conta específica."""
        from app.application.use_cases.accounts import GetAccountBalanceInput
        
        input_data = GetAccountBalanceInput(usuario_id=1, conta_id=5)
        
        assert input_data.conta_id == 5


class TestAccountBalance:
    """Testes para AccountBalance DTO."""
    
    def test_create_account_balance(self):
        """Deve criar objeto de saldo."""
        from app.application.use_cases.accounts import AccountBalance
        
        balance = AccountBalance(
            conta_id=1,
            nome_conta='Nubank',
            tipo_conta='Conta Corrente',
            saldo=1500.50
        )
        
        assert balance.conta_id == 1
        assert balance.nome_conta == 'Nubank'
        assert balance.saldo == 1500.50


class TestGetAccountBalanceOutput:
    """Testes para GetAccountBalanceOutput DTO."""
    
    def test_create_success_output(self):
        """Deve criar output de sucesso."""
        from app.application.use_cases.accounts import (
            GetAccountBalanceOutput,
            AccountBalance
        )
        
        balances = [
            AccountBalance(conta_id=1, nome_conta='A', tipo_conta='Conta Corrente', saldo=1000),
            AccountBalance(conta_id=2, nome_conta='B', tipo_conta='Conta Corrente', saldo=500)
        ]
        
        output = GetAccountBalanceOutput(
            success=True,
            balances=balances,
            total=1500,
            message="OK"
        )
        
        assert output.success is True
        assert len(output.balances) == 2
        assert output.total == 1500


class TestUpdateAccountBalanceInput:
    """Testes para UpdateAccountBalanceInput DTO."""
    
    def test_create_update_input(self):
        """Deve criar input de atualização."""
        from app.application.use_cases.accounts import UpdateAccountBalanceInput
        
        input_data = UpdateAccountBalanceInput(
            usuario_id=1,
            conta_id=1,
            novo_saldo_inicial=2000.0
        )
        
        assert input_data.novo_saldo_inicial == 2000.0


class TestUpdateAccountBalanceOutput:
    """Testes para UpdateAccountBalanceOutput DTO."""
    
    def test_create_update_output(self):
        """Deve criar output de atualização."""
        from app.application.use_cases.accounts import UpdateAccountBalanceOutput
        
        output = UpdateAccountBalanceOutput(
            success=True,
            saldo_anterior=1000.0,
            saldo_novo=2000.0,
            message="Atualizado"
        )
        
        assert output.saldo_anterior == 1000.0
        assert output.saldo_novo == 2000.0
