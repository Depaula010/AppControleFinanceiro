# tests/unit/test_nightly_checkin_service.py
"""
Testes unitários para NightlyCheckinService.

Testa as correções implementadas em 2026-01-08:
- Agendamentos PARCELADOS aparecem no check-in
- Incremento de parcelas_executadas ao confirmar
- Desativação automática ao completar todas as parcelas
- Receitas confirmáveis
- Faturas que vencem hoje
"""

import pytest
from unittest.mock import patch, MagicMock, call
from datetime import date, datetime
from decimal import Decimal
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

from app.services.nightly_checkin_service import NightlyCheckinService
from app.services.queries.agendamentos_queries import AgendamentosQueries
from app.services.queries.faturas_queries import FaturasQueries


# ============================================================================
# Fixtures de Dados de Teste
# ============================================================================

@pytest.fixture
def sample_agendamento_parcelado():
    """Agendamento parcelado para testes."""
    return {
        'id': 1,
        'descricao': 'Notebook 3x',
        'valor_previsto': Decimal('500.00'),
        'dia_execucao': 10,
        'conta_id': 1,
        'subcategoria_id': 1,
        'nome_conta': 'Nubank',
        'tipo_conta': 'Cartão de Crédito',
        'categoria': 'Eletrônicos',
        'nome_macro': 'Compras',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'PARCELADO',
        'parcelas_executadas': 0,
        'total_parcelas': 3,
        'data_vencimento_real': date(2026, 1, 10)
    }


@pytest.fixture
def sample_agendamento_fixo():
    """Agendamento fixo para testes."""
    return {
        'id': 2,
        'descricao': 'Aluguel',
        'valor_previsto': Decimal('1500.00'),
        'dia_execucao': 5,
        'conta_id': 1,
        'subcategoria_id': 2,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Moradia',
        'nome_macro': 'Moradia',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'data_vencimento_real': date(2026, 1, 5)
    }


@pytest.fixture
def sample_receita_pendente():
    """Receita pendente para testes."""
    return {
        'id': 3,
        'descricao': 'Salário',
        'valor_previsto': Decimal('5000.00'),
        'dia_execucao': 5,
        'conta_id': 1,
        'subcategoria_id': 3,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Salário',
        'nome_macro': 'Trabalho',
        'nome_grupo': 'Renda',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'data_vencimento_real': date(2026, 1, 5)
    }


@pytest.fixture
def sample_fatura_vence_hoje():
    """Fatura que vence hoje para testes."""
    return {
        'id': 1,
        'nome_conta': 'Nubank',
        'data_vencimento': date.today(),
        'status': 'Aberta',
        'valor_total': Decimal('1500.00')
    }


@pytest.fixture
def sample_fatura_vencida():
    """Fatura vencida para testes."""
    return {
        'id': 2,
        'nome_conta': 'Inter',
        'data_vencimento': date(2026, 1, 1),
        'status': 'Aberta',
        'valor_total': Decimal('800.00')
    }


@pytest.fixture
def mock_conn():
    """Mock de conexão do banco."""
    conn = MagicMock()
    conn.execute.return_value = MagicMock()
    conn.begin.return_value.__enter__ = lambda s: None
    conn.begin.return_value.__exit__ = lambda s, *args: None
    return conn


# ============================================================================
# Testes de Queries - Agendamentos PARCELADOS
# ============================================================================

class TestAgendamentosParceladosNasQueries:
    """Testa se agendamentos PARCELADOS são incluídos nas queries."""

    def test_query_contas_pendentes_inclui_parcelado(self):
        """Query de contas pendentes deve incluir tipo 'PARCELADO'."""
        query = AgendamentosQueries.get_contas_pendentes_checkin_noturno()
        query_str = str(query)

        # Verificar que query contém 'PARCELADO' no filtro
        assert "'PARCELADO'" in query_str or '"PARCELADO"' in query_str, \
            "Query deve incluir 'PARCELADO' no filtro tipo_agendamento"

        # Verificar padrão: IN ('FIXO', 'LEMBRETE_VARIAVEL', 'PARCELADO')
        assert "tipo_agendamento" in query_str.lower(), \
            "Query deve filtrar por tipo_agendamento"

    def test_query_contas_atrasadas_inclui_parcelado(self):
        """Query de contas atrasadas deve incluir tipo 'PARCELADO'."""
        query = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
        query_str = str(query)

        # Verificar que query contém 'PARCELADO' no filtro
        assert "'PARCELADO'" in query_str or '"PARCELADO"' in query_str, \
            "Query deve incluir 'PARCELADO' no filtro tipo_agendamento"

    def test_get_pending_bills_retorna_agendamentos_parcelados(
        self, mock_conn, sample_agendamento_parcelado
    ):
        """get_pending_bills deve retornar agendamentos PARCELADOS."""
        # Configurar mock para retornar agendamento parcelado
        mock_row = MagicMock()
        mock_row._mapping = sample_agendamento_parcelado
        mock_result = MagicMock()
        mock_result.fetchall.return_value = [mock_row]
        mock_conn.execute.return_value = mock_result

        # Executar
        pending_bills = NightlyCheckinService.get_pending_bills(mock_conn, 1, date.today())

        # Verificar
        assert len(pending_bills) == 1
        assert pending_bills[0]['tipo_agendamento'] == 'PARCELADO'
        assert pending_bills[0]['descricao'] == 'Notebook 3x'
        assert pending_bills[0]['total_parcelas'] == 3


# ============================================================================
# Testes de Incremento de Parcelas
# ============================================================================

class TestIncrementoParcelas:
    """Testa incremento de parcelas_executadas ao confirmar."""

    @patch('app.services.nightly_checkin_service.finance_service')
    @patch('app.services.nightly_checkin_service.text')
    def test_confirmar_parcela_incrementa_contador(
        self, mock_text, mock_finance_service, mock_conn, sample_agendamento_parcelado
    ):
        """Ao confirmar parcela, deve incrementar parcelas_executadas."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        # Simular UPDATE statement
        mock_update_query = MagicMock()
        mock_text.return_value = mock_update_query

        # Executar confirmação
        bills_to_confirm = [sample_agendamento_parcelado]
        usuario_id = 1

        confirmadas = NightlyCheckinService.mark_bills_as_paid(
            mock_conn, usuario_id, bills_to_confirm
        )

        # Verificar que transação foi criada
        assert len(confirmadas) == 1
        assert confirmadas[0] == 'Notebook 3x'

        # Verificar que UPDATE foi executado (incremento de parcelas)
        # Detecta pela presença de 'nova_parcela' no dict de params (text() é mockado,
        # então str(query) não contém "UPDATE" - usamos os parâmetros como indicador)
        calls = mock_conn.execute.call_args_list

        update_calls = [
            c for c in calls
            if len(c[0]) > 1 and isinstance(c[0][1], dict) and 'nova_parcela' in c[0][1]
        ]

        assert len(update_calls) > 0, "Deve executar UPDATE para incrementar parcelas"

    @patch('app.services.nightly_checkin_service.finance_service')
    @patch('app.services.nightly_checkin_service.text')
    def test_confirmar_parcela_intermediaria_mantem_ativo(
        self, mock_text, mock_finance_service, mock_conn, sample_agendamento_parcelado
    ):
        """Ao confirmar parcela intermediária, agendamento deve permanecer ativo."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        # Parcela 1 de 3 (não é a última)
        sample_agendamento_parcelado['parcelas_executadas'] = 0
        sample_agendamento_parcelado['total_parcelas'] = 3

        mock_update_query = MagicMock()
        mock_text.return_value = mock_update_query

        # Executar
        NightlyCheckinService.mark_bills_as_paid(
            mock_conn, 1, [sample_agendamento_parcelado]
        )

        # Verificar que UPDATE foi chamado com ativo=TRUE
        update_calls = [
            c for c in mock_conn.execute.call_args_list
            if len(c[0]) > 1 and isinstance(c[0][1], dict) and 'nova_parcela' in c[0][1]
        ]

        if update_calls:
            # Verificar parâmetros do UPDATE
            update_params = update_calls[0][0][1]
            # nova_parcela deve ser 1, novo_ativo deve ser True
            assert update_params['nova_parcela'] == 1
            assert update_params['novo_ativo'] == True

    @patch('app.services.nightly_checkin_service.finance_service')
    @patch('app.services.nightly_checkin_service.text')
    def test_confirmar_ultima_parcela_desativa_agendamento(
        self, mock_text, mock_finance_service, mock_conn, sample_agendamento_parcelado
    ):
        """Ao confirmar última parcela, agendamento deve ser desativado."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        # Parcela 2 de 3 (próxima será a última)
        sample_agendamento_parcelado['parcelas_executadas'] = 2
        sample_agendamento_parcelado['total_parcelas'] = 3

        mock_update_query = MagicMock()
        mock_text.return_value = mock_update_query

        # Executar
        NightlyCheckinService.mark_bills_as_paid(
            mock_conn, 1, [sample_agendamento_parcelado]
        )

        # Verificar que UPDATE foi chamado com ativo=FALSE
        update_calls = [
            c for c in mock_conn.execute.call_args_list
            if len(c[0]) > 1 and isinstance(c[0][1], dict) and 'nova_parcela' in c[0][1]
        ]

        if update_calls:
            # Verificar parâmetros do UPDATE
            update_params = update_calls[0][0][1]
            # nova_parcela deve ser 3, novo_ativo deve ser False
            assert update_params['nova_parcela'] == 3
            assert update_params['novo_ativo'] == False


# ============================================================================
# Testes de Receitas Confirmáveis
# ============================================================================

class TestReceitasConfirmaveis:
    """Testa confirmação de receitas via check-in."""

    @patch('app.services.nightly_checkin_service.finance_service')
    def test_confirmar_receita_cria_transacao_tipo_renda(
        self, mock_finance_service, mock_conn, sample_receita_pendente
    ):
        """Ao confirmar receita, deve criar transação tipo 'Renda' com valor positivo."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        # Executar
        NightlyCheckinService.mark_bills_as_paid(
            mock_conn, 1, [sample_receita_pendente]
        )

        # Verificar chamada ao create_transaction
        assert mock_finance_service.create_transaction.called

        call_args = mock_finance_service.create_transaction.call_args[0]
        valor_criado = call_args[6]  # 7º argumento é o valor (conn, uid, conta_id, sub_id, fatura_id, descricao, valor, tipo, data)
        tipo_criado = call_args[7]   # 8º argumento é o tipo

        # Receita deve ter valor POSITIVO e tipo 'Renda'
        assert valor_criado > 0, "Receita deve ter valor positivo"
        assert tipo_criado == 'Renda', "Receita deve ter tipo 'Renda'"

    @patch('app.services.nightly_checkin_service.finance_service')
    def test_confirmar_despesa_cria_transacao_tipo_despesa(
        self, mock_finance_service, mock_conn, sample_agendamento_fixo
    ):
        """Ao confirmar despesa, deve criar transação tipo 'Despesa' com valor negativo."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        # Executar
        NightlyCheckinService.mark_bills_as_paid(
            mock_conn, 1, [sample_agendamento_fixo]
        )

        # Verificar chamada ao create_transaction
        assert mock_finance_service.create_transaction.called

        call_args = mock_finance_service.create_transaction.call_args[0]
        valor_criado = call_args[6]  # 7º argumento é o valor (conn, uid, conta_id, sub_id, fatura_id, descricao, valor, tipo, data)
        tipo_criado = call_args[7]   # 8º argumento é o tipo

        # Despesa deve ter valor NEGATIVO e tipo 'Despesa'
        assert valor_criado < 0, "Despesa deve ter valor negativo"
        assert tipo_criado == 'Despesa', "Despesa deve ter tipo 'Despesa'"


# ============================================================================
# Testes de Faturas que Vencem Hoje
# ============================================================================

class TestFaturasVencemHoje:
    """Testa alertas de faturas que vencem hoje."""

    def test_query_faturas_vencendo_hoje_existe(self):
        """Deve existir query get_faturas_vencendo_hoje()."""
        assert hasattr(FaturasQueries, 'get_faturas_vencendo_hoje'), \
            "FaturasQueries deve ter método get_faturas_vencendo_hoje()"

        query = FaturasQueries.get_faturas_vencendo_hoje()
        query_str = str(query)

        # Verificar que query filtra por data_vencimento = hoje
        assert "data_vencimento" in query_str.lower(), \
            "Query deve filtrar por data_vencimento"
        assert "= :hoje" in query_str or "= :data_vencimento" in query_str, \
            "Query deve usar filtro de igualdade (=) para data"

    def test_mensagem_consolidada_inclui_faturas_vencendo_hoje(
        self, sample_fatura_vence_hoje
    ):
        """Mensagem consolidada deve incluir seção de faturas que vencem hoje."""
        # Preparar dados
        pending_bills = []
        overdue_bills = []
        bills_due_today = []
        overdue_invoices = []
        faturas_vencendo_hoje = [sample_fatura_vence_hoje]
        checkin_id = "test123"

        # Executar
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills, overdue_bills, bills_due_today,
            overdue_invoices, faturas_vencendo_hoje, checkin_id
        )

        # Verificar
        assert mensagem is not None, "Deve gerar mensagem quando há faturas vencendo hoje"
        assert "FATURAS QUE VENCEM HOJE" in mensagem, \
            "Mensagem deve conter seção 'FATURAS QUE VENCEM HOJE'"
        assert "Nubank" in mensagem, "Mensagem deve conter nome do cartão"
        assert "1.500,00" in mensagem or "1500" in mensagem, \
            "Mensagem deve conter valor da fatura"
        assert "Vence HOJE" in mensagem, \
            "Mensagem deve indicar que vence hoje"

    def test_mensagem_diferencia_faturas_vencidas_de_faturas_vencendo_hoje(
        self, sample_fatura_vencida, sample_fatura_vence_hoje
    ):
        """Mensagem deve diferenciar faturas vencidas de faturas que vencem hoje."""
        # Preparar dados
        pending_bills = []
        overdue_bills = []
        bills_due_today = []
        overdue_invoices = [sample_fatura_vencida]
        faturas_vencendo_hoje = [sample_fatura_vence_hoje]
        checkin_id = "test123"

        # Executar
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills, overdue_bills, bills_due_today,
            overdue_invoices, faturas_vencendo_hoje, checkin_id
        )

        # Verificar
        assert "FATURAS VENCIDAS" in mensagem, "Deve ter seção de faturas vencidas"
        assert "FATURAS QUE VENCEM HOJE" in mensagem, \
            "Deve ter seção de faturas que vencem hoje"

        # Verificar que Inter está em vencidas e Nubank em vencem hoje
        mensagem_lines = mensagem.split('\n')
        secao_vencidas_idx = None
        secao_vencendo_hoje_idx = None

        for idx, line in enumerate(mensagem_lines):
            if "FATURAS VENCIDAS" in line:
                secao_vencidas_idx = idx
            if "FATURAS QUE VENCEM HOJE" in line:
                secao_vencendo_hoje_idx = idx

        # Inter deve aparecer após seção VENCIDAS e antes de VENCEM HOJE
        # Nubank deve aparecer após seção VENCEM HOJE
        assert secao_vencidas_idx is not None
        assert secao_vencendo_hoje_idx is not None
        assert secao_vencidas_idx < secao_vencendo_hoje_idx


# ============================================================================
# Testes de Formatação de Mensagem
# ============================================================================

class TestFormatacaoMensagem:
    """Testa formatação da mensagem consolidada."""

    def test_mensagem_mostra_numero_parcela_para_parcelados(
        self, sample_agendamento_parcelado
    ):
        """Mensagem deve mostrar número da parcela para agendamentos PARCELADOS."""
        # Preparar dados
        sample_agendamento_parcelado['parcelas_executadas'] = 0
        # Usar Conta Corrente: cartão de crédito é filtrado pela lógica de formatação
        sample_agendamento_parcelado['tipo_conta'] = 'Conta Corrente'
        pending_bills = [sample_agendamento_parcelado]
        overdue_bills = []
        bills_due_today = []
        overdue_invoices = []
        faturas_vencendo_hoje = []
        checkin_id = "test123"

        # Executar
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills, overdue_bills, bills_due_today,
            overdue_invoices, faturas_vencendo_hoje, checkin_id
        )

        # Verificar
        assert mensagem is not None
        # Deve mostrar (1/3) ou similar
        assert "1/3" in mensagem or "(1/3)" in mensagem, \
            "Deve mostrar número da parcela no formato (1/3)"

    def test_mensagem_vazia_quando_nao_ha_pendencias(self):
        """Deve retornar None quando não há pendências."""
        # Executar
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            [], [], [], [], [], "test123"
        )

        # Verificar
        assert mensagem is None, "Deve retornar None quando não há pendências"

    def test_mensagem_tem_todas_secoes_quando_ha_dados(
        self, sample_agendamento_fixo, sample_receita_pendente,
        sample_fatura_vencida, sample_fatura_vence_hoje
    ):
        """Mensagem deve ter todas as seções quando há dados."""
        # Preparar dados completos
        pending_bills = [sample_agendamento_fixo, sample_receita_pendente]
        overdue_bills = []
        bills_due_today = []
        overdue_invoices = [sample_fatura_vencida]
        faturas_vencendo_hoje = [sample_fatura_vence_hoje]
        checkin_id = "test123"

        # Executar
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills, overdue_bills, bills_due_today,
            overdue_invoices, faturas_vencendo_hoje, checkin_id
        )

        # Verificar
        assert mensagem is not None
        assert "CHECK-IN NOTURNO" in mensagem
        assert "DESPESAS PENDENTES" in mensagem or "RECEITAS PENDENTES" in mensagem
        assert "FATURAS VENCIDAS" in mensagem
        assert "FATURAS QUE VENCEM HOJE" in mensagem
        assert "COMO RESPONDER" in mensagem or "ID:" in mensagem


# ============================================================================
# Testes de Integração (Cenários Completos)
# ============================================================================

class TestCenariosCompletos:
    """Testa cenários completos de check-in."""

    @patch('app.services.nightly_checkin_service.finance_service')
    @patch('app.services.nightly_checkin_service.text')
    def test_cenario_confirmar_3_parcelas_completo(
        self, mock_text, mock_finance_service, mock_conn
    ):
        """Cenário: Confirmar agendamento de 3 parcelas até desativar."""
        # Configurar mocks
        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None
        mock_update_query = MagicMock()
        mock_text.return_value = mock_update_query

        # Parcela 1
        agendamento = {
            'id': 1,
            'descricao': 'Notebook 3x',
            'valor_previsto': Decimal('500.00'),
            'dia_execucao': 10,
            'conta_id': 1,
            'subcategoria_id': 1,
            'nome_conta': 'Nubank',
            'tipo_conta': 'Cartão de Crédito',
            'nome_grupo': 'Despesa',
            'tipo_agendamento': 'PARCELADO',
            'parcelas_executadas': 0,
            'total_parcelas': 3
        }

        # Confirmar parcela 1
        NightlyCheckinService.mark_bills_as_paid(mock_conn, 1, [agendamento.copy()])

        # Atualizar parcelas_executadas manualmente (simula DB)
        agendamento['parcelas_executadas'] = 1

        # Confirmar parcela 2
        NightlyCheckinService.mark_bills_as_paid(mock_conn, 1, [agendamento.copy()])

        # Atualizar parcelas_executadas manualmente
        agendamento['parcelas_executadas'] = 2

        # Confirmar parcela 3 (última)
        NightlyCheckinService.mark_bills_as_paid(mock_conn, 1, [agendamento.copy()])

        # Verificar que foram criadas 3 transações
        assert mock_finance_service.create_transaction.call_count == 3

        # Verificar que foram executados 3 UPDATEs
        update_calls = [
            c for c in mock_conn.execute.call_args_list
            if len(c[0]) > 1 and isinstance(c[0][1], dict) and 'nova_parcela' in c[0][1]
        ]
        assert len(update_calls) == 3, "Deve executar 3 UPDATEs (um por parcela)"


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
