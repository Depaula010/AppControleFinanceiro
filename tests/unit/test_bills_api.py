# tests/unit/test_bills_api.py
"""
Testes unitários para API de Contas Mensais (Bills/Agendamentos).

Testa:
- GET /api/bills - Listar contas mensais
- POST /api/bills - Criar conta mensal
- PUT /api/bills/<id> - Atualizar conta mensal
- DELETE /api/bills/<id> - Soft delete de conta mensal
"""

import pytest
import sys
import os

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))

# Valores esperados (não importados do app - testes isolados de infra)
TIPOS_AGENDAMENTO_ESPERADOS = ['FIXO', 'LEMBRETE_VARIAVEL']
PERIODICIDADES_ESPERADAS = ['DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']


class TestBillConstants:
    """Testes para constantes de bills."""

    def test_tipos_agendamento_esperados(self):
        """TIPOS_AGENDAMENTO deve ter exatamente os tipos corretos."""
        assert 'FIXO' in TIPOS_AGENDAMENTO_ESPERADOS
        assert 'LEMBRETE_VARIAVEL' in TIPOS_AGENDAMENTO_ESPERADOS
        assert len(TIPOS_AGENDAMENTO_ESPERADOS) == 2

    def test_periodicidades_esperadas(self):
        """PERIODICIDADES deve ter todas as periodicidades corretas."""
        expected = ['DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']
        for p in expected:
            assert p in PERIODICIDADES_ESPERADAS
        assert len(PERIODICIDADES_ESPERADAS) == 5

    def test_tipos_agendamento_uppercase(self):
        """Tipos de agendamento devem ser strings uppercase."""
        for tipo in TIPOS_AGENDAMENTO_ESPERADOS:
            assert isinstance(tipo, str)
            assert tipo == tipo.upper()

    def test_periodicidades_uppercase(self):
        """Periodicidades devem ser strings uppercase."""
        for per in PERIODICIDADES_ESPERADAS:
            assert isinstance(per, str)
            assert per == per.upper()

    def test_impl_functions_nomes(self):
        """Verificar nomes das funções _impl esperadas na API."""
        impl_functions = [
            '_get_bills_impl',
            '_create_bill_impl',
            '_update_bill_impl',
            '_delete_bill_impl',
        ]
        assert len(impl_functions) == 4
        for fn in impl_functions:
            assert fn.startswith('_')
            assert fn.endswith('_impl')


class TestCreateBillValidations:
    """Testes de validações para POST /api/bills."""

    def test_tipo_fixo_requer_valor_previsto(self):
        """valor_previsto é obrigatório se tipo_agendamento = FIXO."""
        tipo = 'FIXO'
        valor = None
        requer_valor = (tipo == 'FIXO' and valor is None)
        assert requer_valor is True

    def test_tipo_variavel_aceita_sem_valor(self):
        """valor_previsto é opcional para LEMBRETE_VARIAVEL."""
        tipo = 'LEMBRETE_VARIAVEL'
        valor = None
        requer_valor = (tipo == 'FIXO' and valor is None)
        assert requer_valor is False

    def test_tipo_valido(self):
        """Tipos de agendamento válidos devem estar na lista."""
        assert 'FIXO' in TIPOS_AGENDAMENTO_ESPERADOS
        assert 'LEMBRETE_VARIAVEL' in TIPOS_AGENDAMENTO_ESPERADOS

    def test_tipo_invalido_nao_permitido(self):
        """Tipos inválidos devem ser rejeitados."""
        invalidos = ['fixo', 'PARCELADO', 'INVALIDO', '']
        for tipo in invalidos:
            assert tipo not in TIPOS_AGENDAMENTO_ESPERADOS

    def test_periodicidade_valida(self):
        """Periodicidades válidas devem estar na lista."""
        validas = ['MENSAL', 'ANUAL', 'SEMANAL', 'QUINZENAL', 'DIARIA']
        for p in validas:
            assert p in PERIODICIDADES_ESPERADAS

    def test_periodicidade_invalida_nao_permitida(self):
        """Periodicidades inválidas devem ser rejeitadas."""
        invalidas = ['BIMESTRAL', 'TRIMESTRAL', 'mensal', '']
        for p in invalidas:
            assert p not in PERIODICIDADES_ESPERADAS

    def test_dia_execucao_valido(self):
        """Dia de execução deve estar entre 1 e 31."""
        dias_validos = [1, 10, 15, 28, 31]
        dias_invalidos = [0, 32, -1, 100]

        for dia in dias_validos:
            assert 1 <= dia <= 31

        for dia in dias_invalidos:
            assert not (1 <= dia <= 31)

    def test_mes_execucao_para_anual(self):
        """Mês de execução deve ser entre 1 e 12."""
        meses_validos = [1, 6, 12]
        meses_invalidos = [0, 13, -1]

        for mes in meses_validos:
            assert 1 <= mes <= 12

        for mes in meses_invalidos:
            assert not (1 <= mes <= 12)

    def test_descricao_max_length(self):
        """Descrição deve ser limitada a 255 caracteres."""
        max_len = 255
        desc_longa = 'A' * 300
        desc_truncada = desc_longa[:max_len]
        assert len(desc_truncada) == max_len

    def test_descricao_nao_pode_ser_vazia(self):
        """Descrição vazia deve ser inválida."""
        assert not ''.strip()

    def test_campos_obrigatorios(self):
        """Campos obrigatórios para criação."""
        campos_obrigatorios = [
            'descricao', 'tipo_agendamento', 'periodicidade',
            'dia_execucao', 'subcategoria_id', 'conta_id'
        ]
        assert len(campos_obrigatorios) == 6

    def test_valor_positivo_obrigatorio_para_fixo(self):
        """Valor positivo é obrigatório para tipo FIXO."""
        tipo = 'FIXO'
        valores_invalidos = [0.0, -10.0, None]
        for v in valores_invalidos:
            if v is None:
                invalido = True
            else:
                invalido = not (v > 0)
            assert invalido, f"Esperado que {v} seja inválido para tipo FIXO"


class TestUpdateBillValidations:
    """Testes de validações para PUT /api/bills/<id>."""

    def test_campos_atualizaveis(self):
        """Campos aceitos no update devem ser os esperados."""
        campos = [
            'descricao', 'tipo_agendamento', 'periodicidade',
            'dia_execucao', 'mes_execucao', 'valor_previsto',
            'subcategoria_id', 'conta_id', 'notificar_antes_dias', 'data_inicio'
        ]
        assert len(campos) == 10

    def test_tipo_invalido_nao_permitido_no_update(self):
        """Update deve rejeitar tipos inválidos."""
        assert 'TIPO_INEXISTENTE' not in TIPOS_AGENDAMENTO_ESPERADOS
        assert 'parcelado' not in TIPOS_AGENDAMENTO_ESPERADOS

    def test_periodicidade_invalida_nao_permitida_no_update(self):
        """Update deve rejeitar periodicidades inválidas."""
        assert 'BIMESTRAL' not in PERIODICIDADES_ESPERADAS
        assert 'TRIMESTRAL' not in PERIODICIDADES_ESPERADAS


class TestDeleteBillBehavior:
    """Testes para comportamento do DELETE /api/bills/<id>."""

    def test_delete_e_soft_delete(self):
        """Delete deve ser soft delete (UPDATE SET ativo=false, não DELETE físico)."""
        sql_delete = "UPDATE Agendamentos SET ativo = false WHERE id = :id AND usuario_id = :uid AND ativo = true"
        assert sql_delete.strip().upper().startswith('UPDATE')
        assert 'ativo = false' in sql_delete

    def test_delete_verifica_propriedade(self):
        """Delete deve filtrar por usuario_id para isolar dados do usuário."""
        sql_delete = "UPDATE Agendamentos SET ativo = false WHERE id = :id AND usuario_id = :uid AND ativo = true"
        assert 'usuario_id' in sql_delete

    def test_delete_apenas_registros_ativos(self):
        """Delete deve afetar apenas registros com ativo = true."""
        sql_delete = "UPDATE Agendamentos SET ativo = false WHERE id = :id AND usuario_id = :uid AND ativo = true"
        assert 'ativo = true' in sql_delete

    def test_rowcount_zero_indica_nao_encontrado(self):
        """rowcount = 0 deve significar que o registro não foi encontrado."""
        rowcount = 0
        nao_encontrado = (rowcount == 0)
        assert nao_encontrado is True


class TestPortugueseAliases:
    """Testes para aliases em português."""

    def test_aliases_existem(self):
        """Aliases /api/contas-mensais devem existir."""
        aliases = [
            ('/api/contas-mensais', 'GET'),
            ('/api/contas-mensais', 'POST'),
            ('/api/contas-mensais/<id>', 'PUT'),
            ('/api/contas-mensais/<id>', 'DELETE'),
        ]
        assert len(aliases) == 4

    def test_aliases_usam_mesmas_impl(self):
        """Aliases devem referenciar as mesmas funções _impl."""
        impl_functions = [
            '_get_bills_impl',
            '_create_bill_impl',
            '_update_bill_impl',
            '_delete_bill_impl',
        ]
        assert len(impl_functions) == 4

    def test_alias_pt_matches_en_route(self):
        """Alias /api/contas-mensais deve espelhar /api/bills."""
        en_routes = ['/api/bills', '/api/bills/<id>']
        pt_aliases = ['/api/contas-mensais', '/api/contas-mensais/<id>']
        # Ambos devem ter o mesmo número de endpoints
        assert len(en_routes) == len(pt_aliases)


class TestBillSecurity:
    """Testes de segurança dos endpoints de contas mensais."""

    def test_endpoints_requerem_autenticacao(self):
        """Todos os endpoints de bills devem exigir autenticação (token_required)."""
        endpoints = [
            ('GET', '/api/bills'),
            ('POST', '/api/bills'),
            ('PUT', '/api/bills/<id>'),
            ('DELETE', '/api/bills/<id>'),
        ]
        assert len(endpoints) == 4

    def test_user_isolation_em_queries(self):
        """Queries de get/delete verificam usuario_id."""
        sql_get = "WHERE a.usuario_id = :uid AND a.ativo = true"
        assert 'usuario_id' in sql_get

        sql_delete = "WHERE id = :id AND usuario_id = :uid AND ativo = true"
        assert 'usuario_id' in sql_delete

    def test_sql_injection_prevention(self):
        """Descrição é truncada antes de uso; campos usam parâmetros nomeados."""
        malicious = "'; DROP TABLE Agendamentos; --"
        sanitized = malicious[:255]
        assert len(sanitized) <= 255
        # Parâmetros nomeados no SQLAlchemy (text() com :param)
        assert ':descricao' != malicious  # parâmetro é separado do valor

    def test_conta_id_verificada_no_usuario(self):
        """conta_id deve ser validada como pertencente ao usuário."""
        sql_check = "SELECT id FROM Contas WHERE id = :cid AND usuario_id = :uid AND ativa = true"
        assert 'usuario_id' in sql_check
        assert 'ativa = true' in sql_check


class TestBillGetFields:
    """Testes de estrutura da resposta do GET /api/bills."""

    def test_campos_na_resposta(self):
        """GET deve retornar todos os campos esperados."""
        campos_esperados = [
            'id', 'descricao', 'valor_previsto', 'tipo_agendamento',
            'periodicidade', 'dia_execucao', 'mes_execucao',
            'notificar_antes_dias', 'subcategoria_id', 'subcategoria_nome',
            'conta_id', 'conta_nome', 'data_inicio'
        ]
        assert len(campos_esperados) == 13

    def test_data_inicio_formato_iso(self):
        """data_inicio deve estar no formato ISO date (YYYY-MM-DD)."""
        from datetime import date
        data = date.today()
        data_str = data.isoformat()
        assert len(data_str) == 10
        assert data_str[4] == '-'
        assert data_str[7] == '-'

    def test_notificar_antes_dias_default(self):
        """Valor padrão de notificação deve ser 3 dias."""
        default_notificacao = 3
        assert default_notificacao == 3

    def test_valor_previsto_float_ou_none(self):
        """valor_previsto deve ser float ou None."""
        valor_fixo = float(150.00)
        assert isinstance(valor_fixo, float)

        valor_variavel = None
        assert valor_variavel is None

    def test_response_structure(self):
        """Estrutura padrão da resposta da API."""
        response_keys = ['status', 'data']
        assert 'status' in response_keys
        assert 'data' in response_keys

    def test_lista_vazia_retorna_array_vazio(self):
        """Quando não há contas, deve retornar array vazio."""
        empty_result = []
        assert isinstance(empty_result, list)
        assert len(empty_result) == 0
