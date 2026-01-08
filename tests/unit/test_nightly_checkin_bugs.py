# tests/unit/test_nightly_checkin_bugs.py
"""
Testes unitários para correções de bugs do Check-in Noturno.

Correções implementadas em 2026-01-08:
- Bug 1: Receitas não eram confirmáveis via check-in (sessão expirava)
- Bug 2: Agendamentos anuais apareciam como atrasados no mês anterior
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


# ============================================================================
# Fixtures de Dados de Teste
# ============================================================================

@pytest.fixture
def sample_receita_pendente_1():
    """Receita pendente: Salário."""
    return {
        'id': 1,
        'descricao': 'Salário Mensal',
        'valor_previsto': Decimal('5800.80'),
        'dia_execucao': 1,
        'conta_id': 1,
        'subcategoria_id': 1,
        'usuario_id': 1,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Salário',
        'nome_macro': 'Trabalho',
        'nome_grupo': 'Renda',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'data_vencimento_real': date(2026, 1, 1)
    }


@pytest.fixture
def sample_receita_pendente_2():
    """Receita pendente: Swile."""
    return {
        'id': 2,
        'descricao': 'Swile',
        'valor_previsto': Decimal('1200.00'),
        'dia_execucao': 30,
        'conta_id': 1,
        'subcategoria_id': 2,
        'usuario_id': 1,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Benefícios',
        'nome_macro': 'Trabalho',
        'nome_grupo': 'Renda',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'data_vencimento_real': date(2025, 12, 30)
    }


@pytest.fixture
def sample_despesa_pendente():
    """Despesa pendente: Material."""
    return {
        'id': 3,
        'descricao': 'Material',
        'valor_previsto': Decimal('450.00'),
        'dia_execucao': 7,
        'conta_id': 2,
        'subcategoria_id': 3,
        'usuario_id': 1,
        'nome_conta': 'Mercado pago',
        'tipo_conta': 'Conta Digital',
        'categoria': 'Compras',
        'nome_macro': 'Despesas Gerais',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'data_vencimento_real': date(2026, 1, 7)
    }


@pytest.fixture
def sample_agendamento_anual_futuro():
    """Agendamento anual com vencimento futuro no mês."""
    return {
        'id': 4,
        'descricao': 'Material Anual',
        'valor_previsto': Decimal('450.00'),
        'dia_execucao': 30,
        'mes_execucao': 1,  # Janeiro
        'periodicidade': 'ANUAL',
        'conta_id': 2,
        'subcategoria_id': 3,
        'usuario_id': 1,
        'nome_conta': 'Mercado pago',
        'tipo_conta': 'Conta Digital',
        'categoria': 'Compras',
        'nome_macro': 'Despesas Gerais',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'ativo': True
    }


@pytest.fixture
def sample_agendamento_anual_atrasado():
    """Agendamento anual com vencimento passado."""
    return {
        'id': 5,
        'descricao': 'IPTU',
        'valor_previsto': Decimal('1800.00'),
        'dia_execucao': 5,
        'mes_execucao': 1,  # Janeiro
        'periodicidade': 'ANUAL',
        'conta_id': 2,
        'subcategoria_id': 4,
        'usuario_id': 1,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Impostos',
        'nome_macro': 'Obrigações',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'ativo': True
    }


@pytest.fixture
def sample_agendamento_mensal():
    """Agendamento mensal normal."""
    return {
        'id': 6,
        'descricao': 'Internet',
        'valor_previsto': Decimal('100.00'),
        'dia_execucao': 30,
        'periodicidade': 'MENSAL',
        'conta_id': 1,
        'subcategoria_id': 5,
        'usuario_id': 1,
        'nome_conta': 'Banco do Brasil',
        'tipo_conta': 'Conta Corrente',
        'categoria': 'Internet',
        'nome_macro': 'Contas',
        'nome_grupo': 'Despesa',
        'tipo_agendamento': 'FIXO',
        'parcelas_executadas': None,
        'total_parcelas': None,
        'ativo': True
    }


@pytest.fixture
def mock_conn():
    """Mock de conexão do banco."""
    conn = MagicMock()
    conn.execute.return_value = MagicMock()
    conn.begin.return_value.__enter__ = lambda s: None
    conn.begin.return_value.__exit__ = lambda s, *args: None
    return conn


@pytest.fixture
def mock_redis_service():
    """Mock do serviço Redis."""
    with patch('app.services.nightly_checkin_service.redis_service') as mock:
        mock.get.return_value = None
        mock.set_with_ttl.return_value = True
        mock.delete.return_value = True
        yield mock


# ============================================================================
# Bug 1: Receitas não eram confirmáveis via check-in
# ============================================================================

class TestReceitasConfirmaveis:
    """Testa correção do Bug 1: Receitas devem ser confirmáveis via check-in."""

    @patch('app.services.nightly_checkin_service.redis_service')
    def test_receitas_sao_salvas_na_sessao_redis(
        self,
        mock_redis,
        sample_receita_pendente_1,
        sample_receita_pendente_2,
        sample_despesa_pendente
    ):
        """
        Teste 1.1: Receitas devem ser salvas na sessão Redis junto com despesas.

        Cenário:
        - 2 receitas pendentes (Salário, Swile)
        - 1 despesa pendente (Material)

        Resultado Esperado:
        - Sessão Redis contém 3 itens (2 receitas + 1 despesa)
        - Receitas têm índices 1 e 2
        - Despesa tem índice 3
        """
        # Preparar dados
        pending_bills = [
            sample_receita_pendente_1,
            sample_receita_pendente_2,
            sample_despesa_pendente
        ]
        numero_whatsapp = "5511999999999"

        # Configurar mock Redis
        mock_redis.set_with_ttl.return_value = True

        # Executar
        checkin_id = NightlyCheckinService.create_checkin_session(
            numero_whatsapp, pending_bills
        )

        # Verificar que create_checkin_session foi chamado
        assert checkin_id is not None, "Deve retornar checkin_id"

        # Verificar que Redis.set_with_ttl foi chamado
        assert mock_redis.set_with_ttl.called, "Deve salvar sessão no Redis"

        # Obter dados salvos no Redis
        call_args = mock_redis.set_with_ttl.call_args_list
        session_call = [c for c in call_args if 'nightly_checkin:' in str(c)][0]
        session_data = session_call[0][1]

        # Verificar estrutura da sessão
        assert 'items' in session_data, "Sessão deve ter campo 'items'"
        assert 'total_items' in session_data, "Sessão deve ter campo 'total_items'"

        items = session_data['items']
        total_items = session_data['total_items']

        # CORREÇÃO DO BUG: Deve ter 3 itens (2 receitas + 1 despesa)
        assert total_items == 3, f"Esperado 3 itens, obteve {total_items}"
        assert len(items) == 3, f"Esperado 3 itens no dict, obteve {len(items)}"

        # Verificar índices
        assert '1' in items, "Deve ter item com índice '1'"
        assert '2' in items, "Deve ter item com índice '2'"
        assert '3' in items, "Deve ter item com índice '3'"

        # Verificar que receitas estão nos índices 1 e 2
        assert items['1']['nome_grupo'] == 'Renda', "Índice 1 deve ser receita"
        assert items['2']['nome_grupo'] == 'Renda', "Índice 2 deve ser receita"
        assert items['3']['nome_grupo'] == 'Despesa', "Índice 3 deve ser despesa"

    @patch('app.services.nightly_checkin_service.finance_service')
    @patch('app.db_engine')
    def test_confirmar_receitas_via_checkin_cria_transacoes(
        self,
        mock_engine,
        mock_finance_service,
        mock_redis_service,
        mock_conn,
        sample_receita_pendente_1,
        sample_receita_pendente_2
    ):
        """
        Teste 1.2: Ao confirmar receitas via check-in, deve criar transações tipo 'Renda'.

        Cenário:
        - Sessão Redis tem 2 receitas (índices 1, 2)
        - Usuário responde "1, 2"

        Resultado Esperado:
        - 2 transações tipo 'Renda' criadas
        - Valores são POSITIVOS
        - Resposta de sucesso
        """
        # Preparar sessão Redis com receitas
        numero_whatsapp = "5511999999999"
        checkin_id = "test123"

        session_data = {
            'items': {
                '1': sample_receita_pendente_1,
                '2': sample_receita_pendente_2
            },
            'itens_atrasados': [],
            'created_at': str(date.today()),
            'total_items': 2
        }

        # Configurar mocks
        mock_redis_service.get.return_value = session_data
        mock_redis_service.delete.return_value = True

        mock_finance_service.get_or_create_fatura.return_value = None
        mock_finance_service.create_transaction.return_value = None

        mock_engine.connect.return_value.__enter__ = lambda s: mock_conn
        mock_engine.connect.return_value.__exit__ = lambda s, *args: None

        # Executar
        status, resposta = NightlyCheckinService.process_response(
            numero_whatsapp, "1, 2", checkin_id
        )

        # Verificar status
        assert status == 'completed', f"Status deveria ser 'completed', obteve '{status}'"

        # Verificar que create_transaction foi chamado 2 vezes
        assert mock_finance_service.create_transaction.call_count == 2, \
            f"Esperado 2 chamadas, obteve {mock_finance_service.create_transaction.call_count}"

        # Verificar que ambas transações são tipo 'Renda' com valor POSITIVO
        calls = mock_finance_service.create_transaction.call_args_list

        for call in calls:
            args = call[0]
            kwargs = call[1]
            # Argumentos: conn, usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo, data
            descricao = args[5]  # 6º argumento é a descrição
            valor = args[6]      # 7º argumento é o valor
            tipo = args[7]       # 8º argumento é o tipo

            assert tipo == 'Renda', f"Tipo deveria ser 'Renda', obteve '{tipo}' para '{descricao}'"
            assert valor > 0, f"Receita deve ter valor positivo, obteve {valor}"

            # NOVO (2026-01-08): Verificar que agendamento_id está sendo passado
            assert 'agendamento_id' in kwargs, f"agendamento_id deveria estar em kwargs para '{descricao}'"
            assert kwargs['agendamento_id'] is not None, f"agendamento_id não deveria ser None para '{descricao}'"

        # Verificar resposta
        assert "confirmada" in resposta.lower(), "Resposta deve indicar confirmação"


# ============================================================================
# Bug 2: Agendamentos anuais aparecem como atrasados no mês anterior
# ============================================================================

class TestAgendamentosAnuaisDataCorreta:
    """Testa correção do Bug 2: Agendamentos anuais não devem aparecer no mês errado."""

    def test_agendamento_anual_nao_aparece_como_atrasado_mes_anterior(
        self,
        mock_conn,
        sample_agendamento_anual_futuro
    ):
        """
        Teste 2.1: Agendamento anual com vencimento FUTURO não deve aparecer como atrasado.

        Cenário:
        - Agendamento anual: dia=30, mes=1 (30 de janeiro)
        - Data de hoje: 2026-01-08
        - Vencimento é FUTURO (ainda não chegou)

        Resultado Esperado:
        - Agendamento NÃO deve aparecer em contas atrasadas
        - Bug anterior: aparecia como "Venceu em 30/12/2025"
        """
        # Preparar mock da query
        # Simular que query NÃO retorna o agendamento
        mock_result = MagicMock()
        mock_result.fetchall.return_value = []
        mock_conn.execute.return_value = mock_result

        # Executar query de contas atrasadas
        hoje = date(2026, 1, 8)
        sql = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
        params = AgendamentosQueries.get_parametros_padrao(1, hoje)

        result = mock_conn.execute(sql, params).fetchall()
        overdue_bills = [dict(row._mapping) for row in result]

        # Verificar que lista está vazia (agendamento não aparece)
        assert len(overdue_bills) == 0, \
            "Agendamento anual com vencimento futuro NÃO deve aparecer como atrasado"

    def test_agendamento_anual_aparece_quando_realmente_atrasado(
        self,
        mock_conn,
        sample_agendamento_anual_atrasado
    ):
        """
        Teste 2.2: Agendamento anual com vencimento PASSADO deve aparecer como atrasado.

        Cenário:
        - Agendamento anual: dia=5, mes=1 (5 de janeiro)
        - Data de hoje: 2026-01-15
        - Vencimento é PASSADO (atrasado 10 dias)

        Resultado Esperado:
        - Agendamento DEVE aparecer em contas atrasadas
        - data_vencimento_real = 2026-01-05 (não 2025-12-05)
        """
        # Preparar mock da query
        # Simular que query RETORNA o agendamento com data correta
        sample_agendamento_anual_atrasado['data_vencimento_real'] = date(2026, 1, 5)

        mock_row = MagicMock()
        mock_row._mapping = sample_agendamento_anual_atrasado
        mock_result = MagicMock()
        mock_result.fetchall.return_value = [mock_row]
        mock_conn.execute.return_value = mock_result

        # Executar query de contas atrasadas
        hoje = date(2026, 1, 15)
        sql = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
        params = AgendamentosQueries.get_parametros_padrao(1, hoje)

        result = mock_conn.execute(sql, params).fetchall()
        overdue_bills = [dict(row._mapping) for row in result]

        # Verificar que agendamento aparece
        assert len(overdue_bills) == 1, "Agendamento anual atrasado DEVE aparecer"

        # Verificar data de vencimento correta
        bill = overdue_bills[0]
        assert bill['data_vencimento_real'] == date(2026, 1, 5), \
            f"Data deveria ser 2026-01-05, obteve {bill['data_vencimento_real']}"

        # Verificar que NÃO é data do mês anterior
        assert bill['data_vencimento_real'].month == 1, \
            "Mês deveria ser janeiro (1), não dezembro (12)"
        assert bill['data_vencimento_real'].year == 2026, \
            "Ano deveria ser 2026, não 2025"

    def test_agendamento_mensal_mantem_comportamento_original(
        self,
        mock_conn,
        sample_agendamento_mensal
    ):
        """
        Teste 2.3: Agendamentos MENSAIS devem manter comportamento original (não afetar).

        Cenário:
        - Agendamento mensal: dia=30, periodicidade='MENSAL'
        - Data de hoje: 2026-01-08
        - Vencimento do mês anterior: 2025-12-30

        Resultado Esperado:
        - Agendamento DEVE aparecer em contas atrasadas
        - data_vencimento_real = 2025-12-30 (mês anterior)
        - Correção não deve afetar mensais
        """
        # Preparar mock da query
        sample_agendamento_mensal['data_vencimento_real'] = date(2025, 12, 30)

        mock_row = MagicMock()
        mock_row._mapping = sample_agendamento_mensal
        mock_result = MagicMock()
        mock_result.fetchall.return_value = [mock_row]
        mock_conn.execute.return_value = mock_result

        # Executar query de contas atrasadas
        hoje = date(2026, 1, 8)
        sql = AgendamentosQueries.get_contas_atrasadas_checkin_noturno()
        params = AgendamentosQueries.get_parametros_padrao(1, hoje)

        result = mock_conn.execute(sql, params).fetchall()
        overdue_bills = [dict(row._mapping) for row in result]

        # Verificar que agendamento mensal aparece (comportamento original)
        assert len(overdue_bills) == 1, "Agendamento mensal atrasado DEVE aparecer"

        # Verificar data de vencimento do mês anterior
        bill = overdue_bills[0]
        assert bill['data_vencimento_real'] == date(2025, 12, 30), \
            f"Data deveria ser 2025-12-30, obteve {bill['data_vencimento_real']}"


# ============================================================================
# Teste de Integração: Cenário Completo
# ============================================================================

class TestCenarioCompletoCheckinNoturno:
    """Testa cenário completo de check-in noturno com receitas e agendamentos anuais."""

    @patch('app.services.nightly_checkin_service.redis_service')
    def test_mensagem_consolidada_com_receitas_e_despesas(
        self,
        mock_redis,
        sample_receita_pendente_1,
        sample_receita_pendente_2,
        sample_despesa_pendente
    ):
        """
        Teste de integração: Mensagem consolidada deve mostrar receitas e despesas.

        Cenário:
        - 2 receitas pendentes
        - 1 despesa pendente
        - Usuário confirma todas (responde "1, 2, 3")

        Resultado Esperado:
        - Mensagem mostra numeração contínua (1, 2, 3)
        - Receitas e despesas são confirmáveis
        """
        # Preparar dados
        pending_bills = [
            sample_receita_pendente_1,
            sample_receita_pendente_2,
            sample_despesa_pendente
        ]

        # Criar sessão
        numero_whatsapp = "5511999999999"
        checkin_id = NightlyCheckinService.create_checkin_session(
            numero_whatsapp, pending_bills
        )

        # Formatar mensagem
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills, [], [], [], [], checkin_id
        )

        # Verificar que mensagem contém receitas e despesas
        assert mensagem is not None, "Deve gerar mensagem"
        assert "RECEITAS PENDENTES" in mensagem, "Deve ter seção de receitas"
        assert "DESPESAS PENDENTES" in mensagem, "Deve ter seção de despesas"

        # Verificar numeração
        assert "1." in mensagem, "Deve ter item 1"
        assert "2." in mensagem, "Deve ter item 2"
        assert "3." in mensagem, "Deve ter item 3"


if __name__ == "__main__":
    pytest.main([__file__, "-v", "-s"])
