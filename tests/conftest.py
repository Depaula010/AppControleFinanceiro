# tests/conftest.py
"""
Configuração global de testes para o projeto AppControleFinanceiro.

Este arquivo configura fixtures e mocks para testes unitários e de integração.
"""

import pytest
import sys
import os
from unittest.mock import MagicMock, patch
from datetime import date
from dataclasses import dataclass
from typing import Any, List, Dict

# ============================================================================
# MOCK DE VARIÁVEIS DE AMBIENTE (ANTES DE QUALQUER IMPORT DO APP)
# ============================================================================
# Isso permite que os testes rodem sem precisar de .env configurado

os.environ.setdefault('GEMINI_API_KEY', 'test_key_for_testing_purposes_only')
os.environ.setdefault('DATABASE_URL', 'postgresql://test:test@localhost:5432/test')
os.environ.setdefault('API_SECRET_KEY', 'test_secret_key_for_testing_minimum_32_chars_required')
os.environ.setdefault('WEBHOOK_SIGNATURE_KEY', 'test_webhook_key_for_testing_minimum_32_chars')
os.environ.setdefault('REDIS_URL', 'redis://localhost:6379')
os.environ.setdefault('BOT_WHATSAPP_URL', 'https://test.whatsapp.bot')
os.environ.setdefault('ENCRYPTION_KEY', 'wL7XCLwCYlQpZz-_xQkzXZ8nH3CpKLzQ5d-8Z9K8W0Q=')

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


# ============================================================================
# Fixtures de Mock para Database
# ============================================================================

class MockConnection:
    """Mock de conexão do banco de dados."""
    
    def __init__(self):
        self.executed_queries = []
        self.mock_results = {}
        self._committed = False
        self._rolled_back = False
    
    def execute(self, query, params=None):
        """Mock de execute que retorna resultados configurados."""
        self.executed_queries.append((str(query), params))
        
        # Retorna mock result
        mock_result = MagicMock()
        mock_result.fetchall.return_value = []
        mock_result.fetchone.return_value = None
        mock_result.scalar.return_value = None
        mock_result.scalar_one_or_none.return_value = None
        
        return mock_result
    
    def commit(self):
        self._committed = True
    
    def rollback(self):
        self._rolled_back = True
    
    def __enter__(self):
        return self
    
    def __exit__(self, exc_type, exc_val, exc_tb):
        if exc_type:
            self.rollback()
        return False


@pytest.fixture
def mock_connection():
    """Fixture para mock de conexão."""
    return MockConnection()


@pytest.fixture
def mock_db_connection(mock_connection):
    """Fixture que patcha get_db_connection."""
    with patch('app.infrastructure.database.connection.get_db_connection') as mock:
        mock.return_value.__enter__ = lambda s: mock_connection
        mock.return_value.__exit__ = lambda s, *args: None
        yield mock_connection


# ============================================================================
# Fixtures de Dados de Teste
# ============================================================================

@pytest.fixture
def sample_user_id():
    """ID de usuário para testes."""
    return 1


@pytest.fixture
def sample_conta_corrente():
    """Dados de conta corrente para testes."""
    return {
        'id': 1,
        'nome_conta': 'Nubank',
        'tipo_conta': 'Conta Corrente',
        'saldo_inicial': 1000.0
    }


@pytest.fixture
def sample_cartao_credito():
    """Dados de cartão de crédito para testes."""
    return {
        'id': 2,
        'nome_conta': 'Nubank Crédito',
        'tipo_conta': 'Cartão de Crédito',
        'dia_fechamento': 20,
        'dia_vencimento': 27
    }


@pytest.fixture
def sample_transaction_data(sample_user_id, sample_conta_corrente):
    """Dados para criação de transação."""
    return {
        'usuario_id': sample_user_id,
        'conta_id': sample_conta_corrente['id'],
        'conta_tipo': sample_conta_corrente['tipo_conta'],
        'subcategoria_id': 1,
        'descricao': 'Supermercado',
        'valor': 150.50,
        'tipo_transacao': 'Despesa',
        'data_transacao': date.today()
    }


@pytest.fixture
def sample_transfer_data(sample_user_id):
    """Dados para transferência."""
    return {
        'usuario_id': sample_user_id,
        'conta_id_origem': 1,
        'conta_id_destino': 2,
        'valor': 500.0,
        'data_transacao': date.today()
    }


@pytest.fixture
def sample_alertas():
    """Dados de alertas financeiros para testes."""
    return {
        'contas_hoje': [
            {'descricao': 'Conta de Luz', 'valor': 150.50, 'tipo': 'Despesa'},
            {'descricao': 'Salário', 'valor': 5000.00, 'tipo': 'Receita'}
        ],
        'contas_amanha': [
            {'descricao': 'Internet', 'valor': 99.90, 'tipo': 'Despesa'}
        ],
        'faturas_hoje': [
            {'cartao': 'Nubank', 'valor': 1500.00}
        ],
        'faturas_amanha': []
    }


@pytest.fixture
def sample_alertas_vazio():
    """Dados de alertas vazios."""
    return {
        'contas_hoje': [],
        'contas_amanha': [],
        'faturas_hoje': [],
        'faturas_amanha': []
    }


# ============================================================================
# Helpers para Verificação
# ============================================================================

def assert_output_success(output, expected_success=True):
    """Verifica se output de use case tem success esperado."""
    assert hasattr(output, 'success'), "Output deve ter atributo 'success'"
    assert output.success == expected_success, f"Expected success={expected_success}, got {output.success}"


def assert_output_has_message(output):
    """Verifica se output tem mensagem."""
    assert hasattr(output, 'message'), "Output deve ter atributo 'message'"
    assert output.message, "Message não deve estar vazia"
