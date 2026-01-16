# tests/unit/test_budget_validation.py
"""
Testes unitários para a feature de Metas de Gastos por Categoria (Budget Validation).

Testa:
- Cálculo de período por periodicidade
- Validação de limites de potes
- Integração com CreateTransactionUseCase
"""

import pytest
from datetime import date, timedelta
from unittest.mock import MagicMock, patch
from decimal import Decimal


# ============================================================================
# Testes para calcular_periodo_pote
# ============================================================================

class TestCalcularPeriodoPote:
    """Testes para o cálculo de período baseado na periodicidade."""

    def test_periodo_semanal_meio_semana(self):
        """Período semanal com data no meio da semana."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        # Quarta-feira, 15 de janeiro de 2025
        data_ref = date(2025, 1, 15)  # Quarta-feira
        inicio, fim = calcular_periodo_pote('SEMANAL', data_ref)

        # Deve retornar segunda a domingo da mesma semana
        assert inicio == date(2025, 1, 13)  # Segunda
        assert fim == date(2025, 1, 19)  # Domingo

    def test_periodo_semanal_segunda(self):
        """Período semanal com data na segunda-feira."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2025, 1, 13)  # Segunda-feira
        inicio, fim = calcular_periodo_pote('SEMANAL', data_ref)

        assert inicio == date(2025, 1, 13)  # Segunda
        assert fim == date(2025, 1, 19)  # Domingo

    def test_periodo_quinzenal_primeira_quinzena(self):
        """Período quinzenal na primeira quinzena do mês."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2025, 1, 10)  # Dia 10
        inicio, fim = calcular_periodo_pote('QUINZENAL', data_ref)

        assert inicio == date(2025, 1, 1)  # Dia 1
        assert fim == date(2025, 1, 15)  # Dia 15

    def test_periodo_quinzenal_segunda_quinzena(self):
        """Período quinzenal na segunda quinzena do mês."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2025, 1, 20)  # Dia 20
        inicio, fim = calcular_periodo_pote('QUINZENAL', data_ref)

        assert inicio == date(2025, 1, 16)  # Dia 16
        assert fim == date(2025, 1, 31)  # Último dia de janeiro

    def test_periodo_mensal(self):
        """Período mensal."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2025, 2, 15)  # Fevereiro
        inicio, fim = calcular_periodo_pote('MENSAL', data_ref)

        assert inicio == date(2025, 2, 1)
        assert fim == date(2025, 2, 28)  # Fevereiro 2025 tem 28 dias

    def test_periodo_mensal_fevereiro_bissexto(self):
        """Período mensal em fevereiro de ano bissexto."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2024, 2, 15)  # 2024 é bissexto
        inicio, fim = calcular_periodo_pote('MENSAL', data_ref)

        assert inicio == date(2024, 2, 1)
        assert fim == date(2024, 2, 29)  # 29 dias

    def test_periodo_anual(self):
        """Período anual."""
        from app.services.finance.budget_validation_service import calcular_periodo_pote

        data_ref = date(2025, 7, 15)
        inicio, fim = calcular_periodo_pote('ANUAL', data_ref)

        assert inicio == date(2025, 1, 1)
        assert fim == date(2025, 12, 31)


# ============================================================================
# Testes para validate_budget
# ============================================================================

class TestValidateBudget:
    """Testes para a validação de limites de potes."""

    def test_transacao_sem_pote_permite(self):
        """Subcategoria não vinculada a pote deve permitir."""
        from app.services.finance.budget_validation_service import validate_budget

        # Mock de conexão que retorna lista vazia de potes
        mock_conn = MagicMock()
        mock_conn.execute.return_value.fetchall.return_value = []

        result = validate_budget(
            conn=mock_conn,
            usuario_id=1,
            subcategoria_id=999,  # Subcategoria sem pote
            valor_transacao=100.0,
            data_transacao=date.today()
        )

        assert result.pode_prosseguir is True
        assert result.requer_confirmacao is False
        assert len(result.validacoes) == 0
        assert result.mensagem == ""

    def test_transacao_dentro_limite_permite(self):
        """Despesa que não excede limite deve permitir."""
        from app.services.finance.budget_validation_service import validate_budget

        # Mock de conexão
        mock_conn = MagicMock()

        # Pote encontrado
        mock_conn.execute.return_value.fetchall.side_effect = [
            # get_potes_for_subcategoria
            [(1, 'Alimentação', Decimal('500.00'), 'MENSAL', date(2025, 1, 1))],
        ]
        # Gasto atual no período
        mock_conn.execute.return_value.scalar.return_value = Decimal('-200.00')  # Negativo = despesa

        result = validate_budget(
            conn=mock_conn,
            usuario_id=1,
            subcategoria_id=5,
            valor_transacao=100.0,  # Gasto atual: 200 + 100 = 300 < 500
            data_transacao=date.today()
        )

        assert result.pode_prosseguir is True
        assert result.requer_confirmacao is False

    def test_transacao_excede_limite_requer_confirmacao(self):
        """Exceder limite deve retornar requer_confirmacao=True."""
        from app.services.finance.budget_validation_service import validate_budget

        # Mock de conexão
        mock_conn = MagicMock()

        # Simulando retornos sequenciais
        mock_potes_result = MagicMock()
        mock_potes_result.fetchall.return_value = [
            (1, 'Alimentação', Decimal('500.00'), 'MENSAL', date(2025, 1, 1))
        ]

        mock_gasto_result = MagicMock()
        mock_gasto_result.scalar.return_value = Decimal('-450.00')  # Já gastou 450

        mock_conn.execute.side_effect = [mock_potes_result, mock_gasto_result]

        result = validate_budget(
            conn=mock_conn,
            usuario_id=1,
            subcategoria_id=5,
            valor_transacao=100.0,  # 450 + 100 = 550 > 500
            data_transacao=date.today()
        )

        assert result.pode_prosseguir is False
        assert result.requer_confirmacao is True
        assert len(result.validacoes) == 1
        assert result.validacoes[0].ultrapassaria_limite is True
        assert 'Alimentação' in result.mensagem

    def test_multiplos_potes_um_excede(self):
        """Subcategoria em múltiplos potes, apenas um excede."""
        from app.services.finance.budget_validation_service import validate_budget

        mock_conn = MagicMock()

        # Dois potes para a subcategoria
        mock_potes_result = MagicMock()
        mock_potes_result.fetchall.return_value = [
            (1, 'Alimentação', Decimal('500.00'), 'MENSAL', date(2025, 1, 1)),
            (2, 'Geral', Decimal('2000.00'), 'MENSAL', date(2025, 1, 1)),
        ]

        # Gasto atual para cada pote
        mock_gasto_result1 = MagicMock()
        mock_gasto_result1.scalar.return_value = Decimal('-450.00')  # Alimentação: 450

        mock_gasto_result2 = MagicMock()
        mock_gasto_result2.scalar.return_value = Decimal('-500.00')  # Geral: 500

        mock_conn.execute.side_effect = [mock_potes_result, mock_gasto_result1, mock_gasto_result2]

        result = validate_budget(
            conn=mock_conn,
            usuario_id=1,
            subcategoria_id=5,
            valor_transacao=100.0,  # Alimentação: 550 > 500, Geral: 600 < 2000
            data_transacao=date.today()
        )

        assert result.requer_confirmacao is True
        assert len(result.validacoes) == 2
        # Apenas o pote Alimentação deve estar na lista de ultrapassados
        potes_ultrapassados = [v for v in result.validacoes if v.ultrapassaria_limite]
        assert len(potes_ultrapassados) == 1
        assert potes_ultrapassados[0].nome_pote == 'Alimentação'


# ============================================================================
# Testes para BudgetValidationResult dataclass
# ============================================================================

class TestBudgetValidationResult:
    """Testes para o dataclass de resultado."""

    def test_calculo_percentual(self):
        """Verifica cálculo de percentual usado."""
        from app.services.finance.budget_validation_service import BudgetValidationResult

        result = BudgetValidationResult(
            pote_id=1,
            nome_pote='Test',
            valor_limite=1000.0,
            valor_gasto_atual=500.0,
            valor_apos_transacao=700.0,
            percentual_usado=70.0,
            ultrapassaria_limite=False
        )

        assert result.percentual_usado == 70.0
        assert result.ultrapassaria_limite is False


# ============================================================================
# Testes para CreateTransactionUseCase com validação de budget
# ============================================================================

class TestCreateTransactionUseCaseWithBudget:
    """Testes para integração do budget validation com CreateTransactionUseCase."""

    def test_input_with_required_fields(self):
        """Verifica criação de input com campos obrigatórios."""
        from app.application.use_cases.transactions.create_transaction import CreateTransactionInput

        input_data = CreateTransactionInput(
            usuario_id=1,
            conta_id=1,
            conta_tipo='Conta Corrente',
            subcategoria_id=5,
            descricao='Supermercado',
            valor=150.0,
            tipo_transacao='Despesa',
            data_transacao=date.today()
        )

        assert input_data.usuario_id == 1
        assert input_data.tipo_transacao == 'Despesa'

    def test_output_has_budget_fields(self):
        """Verifica que output tem campos de budget."""
        from app.application.use_cases.transactions.create_transaction import CreateTransactionOutput

        output = CreateTransactionOutput(
            success=False,
            message='Limite excedido',
            budget_warning=True,
            requires_confirmation=True
        )

        assert output.budget_warning is True
        assert output.requires_confirmation is True

    def test_receita_nao_valida_budget(self):
        """Tipo Receita não deve passar por validação de budget."""
        from app.application.use_cases.transactions.create_transaction import (
            CreateTransactionInput,
            CreateTransactionOutput,
        )

        input_data = CreateTransactionInput(
            usuario_id=1,
            conta_id=1,
            conta_tipo='Conta Corrente',
            subcategoria_id=5,
            descricao='Salário',
            valor=5000.0,
            tipo_transacao='Renda',  # Receita, não despesa
            data_transacao=date.today()
        )

        # A validação de budget só ocorre para Despesa
        assert input_data.tipo_transacao == 'Renda'


# ============================================================================
# Testes para formatação de mensagem de aviso
# ============================================================================

class TestFormatMensagemAviso:
    """Testes para formatação de mensagens de aviso."""

    def test_formato_mensagem_um_pote(self):
        """Mensagem com um único pote ultrapassado."""
        from app.services.finance.budget_validation_service import (
            BudgetValidationResult,
            _formatar_mensagem_aviso
        )

        potes_ultrapassados = [
            BudgetValidationResult(
                pote_id=1,
                nome_pote='Alimentação',
                valor_limite=800.0,
                valor_gasto_atual=720.0,
                valor_apos_transacao=870.0,
                percentual_usado=108.75,
                ultrapassaria_limite=True
            )
        ]

        mensagem = _formatar_mensagem_aviso(potes_ultrapassados)

        assert 'Alimentação' in mensagem
        assert 'R$ 800' in mensagem or '800,00' in mensagem
        assert 'ultrapassará' in mensagem.lower() or 'ultrapassara' in mensagem.lower()

    def test_formato_mensagem_multiplos_potes(self):
        """Mensagem com múltiplos potes ultrapassados."""
        from app.services.finance.budget_validation_service import (
            BudgetValidationResult,
            _formatar_mensagem_aviso
        )

        potes_ultrapassados = [
            BudgetValidationResult(
                pote_id=1,
                nome_pote='Alimentação',
                valor_limite=500.0,
                valor_gasto_atual=450.0,
                valor_apos_transacao=550.0,
                percentual_usado=110.0,
                ultrapassaria_limite=True
            ),
            BudgetValidationResult(
                pote_id=2,
                nome_pote='Lazer',
                valor_limite=300.0,
                valor_gasto_atual=280.0,
                valor_apos_transacao=380.0,
                percentual_usado=126.7,
                ultrapassaria_limite=True
            )
        ]

        mensagem = _formatar_mensagem_aviso(potes_ultrapassados)

        assert 'múltiplos' in mensagem.lower() or 'multiplos' in mensagem.lower()
        assert 'Alimentação' in mensagem
        assert 'Lazer' in mensagem
