"""
Queries SQL centralizadas para Agendamentos.

Este módulo contém todas as queries SQL relacionadas a agendamentos (contas a pagar/receber)
para evitar duplicação de código e facilitar manutenção.

IMPORTANTE: Ao modificar uma query aqui, a mudança afeta TODOS os lugares que a utilizam.
"""

from sqlalchemy import text
from typing import Dict, Any


class AgendamentosQueries:
    """
    Queries SQL reutilizáveis para operações com Agendamentos.

    Convenções:
    - Todos os métodos são estáticos
    - Retornam um objeto `text()` do SQLAlchemy
    - Parâmetros necessários estão documentados em cada método
    """

    @staticmethod
    def get_contas_pendentes_ultimos_7_dias() -> text:
        """
        Busca contas pendentes que vencem hoje ou estão atrasadas nos últimos 7 dias.

        CORREÇÃO DE BUG (2026-01-06):
        Agora usa CTE para calcular data real de vencimento, em vez de filtrar
        apenas por dia do mês. Isso corrige problemas de:
        - Virada de mês (conta de 28/12 aparece em 06/01)
        - Contas futuras sendo incluídas incorretamente
        - Cálculo incorreto de atraso

        Usado em: Check-in noturno

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :target_date (date) - Data de referência (normalmente hoje)
            :data_limite_transacao (date) - Data limite para verificar transações (hoje - 60 dias)

        Retorna: Agendamentos não pagos que vencem hoje ou estão atrasados (últimos 7 dias)
                 INCLUI campo data_vencimento_real para cálculo preciso de atraso
        """
        return text("""
            WITH ExpectedDates AS (
                SELECT
                    a.*,
                    c.nome_conta, c.tipo_conta,
                    s.nome_sub as categoria,
                    m.nome_macro,
                    g.nome_grupo,
                    -- Calcula data esperada para mês atual
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :target_date) + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :target_date) + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :target_date) + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_atual,
                    -- Calcula data esperada para mês anterior
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :target_date - INTERVAL '1 month') + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :target_date - INTERVAL '1 month') + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :target_date - INTERVAL '1 month') + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_anterior
                FROM Agendamentos a
                JOIN Contas c ON a.conta_id = c.id
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
                WHERE a.usuario_id = :uid
                  AND a.ativo = TRUE
                  AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
            )
            SELECT
                ed.id, ed.descricao, ed.valor_previsto, ed.dia_execucao,
                ed.conta_id, ed.subcategoria_id, ed.usuario_id,
                ed.nome_conta, ed.tipo_conta, ed.categoria,
                ed.nome_macro, ed.nome_grupo,
                COALESCE(ed.data_esperada_mes_atual, ed.data_esperada_mes_anterior) as data_vencimento_real
            FROM ExpectedDates ed
            WHERE (
                -- Mês atual: vence hoje ou atrasado nos últimos 7 dias
                (ed.data_esperada_mes_atual <= :target_date
                 AND ed.data_esperada_mes_atual >= :target_date - INTERVAL '7 days'
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND t.data_transacao >= :data_limite_transacao
                       AND t.data_transacao <= :target_date
                 ))
                OR
                -- Mês anterior: atrasado nos últimos 7 dias
                (ed.data_esperada_mes_anterior <= :target_date
                 AND ed.data_esperada_mes_anterior >= :target_date - INTERVAL '7 days'
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND t.data_transacao >= :data_limite_transacao
                       AND t.data_transacao <= :target_date
                 ))
            )
            -- Filtro para agendamentos anuais: incluir apenas se o mês bater
            AND (
                ed.periodicidade != 'ANUAL'
                OR (ed.periodicidade = 'ANUAL' AND ed.mes_execucao = EXTRACT(MONTH FROM :target_date))
            )
            ORDER BY data_vencimento_real DESC, ed.nome_grupo, ed.descricao
        """)

    @staticmethod
    def get_contas_atrasadas_com_data_real() -> text:
        """
        Busca contas atrasadas com data de vencimento real calculada.

        Usado em:
        - Alerta de contas atrasadas (job noturno)
        - Intent "Contas Atrasadas" (chatbot)

        Usa CTE (Common Table Expression) para calcular a data real de vencimento
        considerando virada de mês.

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :hoje (date) - Data atual
            :data_minima (date) - Data mínima para buscar (ex: hoje - 30 dias)
            :data_maxima (date) - Data máxima de atraso (ex: hoje - 8 dias para >7 dias)
            :data_limite_transacao (date) - Data limite para verificar transações (hoje - 60 dias)

        Retorna: Agendamentos atrasados com data_vencimento_real calculada
        """
        return text("""
            WITH ExpectedDates AS (
                SELECT
                    a.*,
                    c.nome_conta, c.tipo_conta,
                    s.nome_sub as categoria,
                    m.nome_macro,
                    g.nome_grupo,
                    -- Constrói a data esperada completa para o mês atual
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :hoje) + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :hoje) + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :hoje) + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_atual,
                    -- Constrói a data esperada completa para o mês anterior
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_anterior
                FROM Agendamentos a
                JOIN Contas c ON a.conta_id = c.id
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
                WHERE a.usuario_id = :uid
                  AND a.ativo = TRUE
                  AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
                  -- Exclui débitos recorrentes de cartão (assinaturas) pois vão para a fatura
                  AND NOT (a.tipo_agendamento = 'FIXO' AND g.nome_grupo = 'Despesa' AND c.tipo_conta = 'Cartão de Crédito')
            )
            SELECT
                ed.id, ed.descricao, ed.valor_previsto, ed.dia_execucao,
                ed.conta_id, ed.subcategoria_id, ed.usuario_id,
                ed.nome_conta, ed.tipo_conta, ed.categoria,
                ed.nome_macro, ed.nome_grupo,
                COALESCE(ed.data_esperada_mes_atual, ed.data_esperada_mes_anterior) as data_vencimento_real
            FROM ExpectedDates ed
            WHERE (
                -- Mês atual está atrasado (mais de 7 dias)
                (ed.data_esperada_mes_atual < :hoje
                 AND ed.data_esperada_mes_atual < :hoje - INTERVAL '7 days'
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND t.data_transacao >= :data_limite_transacao
                       AND t.data_transacao <= :hoje
                 ))
                OR
                -- Mês anterior está atrasado (mais de 7 dias)
                (ed.data_esperada_mes_anterior < :hoje
                 AND ed.data_esperada_mes_anterior >= :data_minima
                 AND ed.data_esperada_mes_anterior < :hoje - INTERVAL '7 days'
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND t.data_transacao >= :data_limite_transacao
                       AND t.data_transacao <= :hoje
                 ))
            )
            -- Filtro para agendamentos anuais
            AND (
                ed.periodicidade != 'ANUAL'
                OR (ed.periodicidade = 'ANUAL' AND ed.mes_execucao = EXTRACT(MONTH FROM :hoje))
            )
            ORDER BY data_vencimento_real DESC, ed.nome_grupo, ed.descricao
        """)

    @staticmethod
    def get_contas_vencendo_hoje() -> text:
        """
        Busca contas que vencem hoje (ainda não pagas).

        Usado em: Alerta de vencimentos do dia

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :hoje (date) - Data atual
            :data_limite_transacao (date) - Data limite para verificar transações (hoje - 60 dias)

        Retorna: Agendamentos que vencem hoje
        """
        return text("""
            SELECT
                a.id, a.descricao, a.valor_previsto,
                c.nome_conta, c.tipo_conta,
                s.nome_sub as categoria,
                g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao = EXTRACT(DAY FROM :hoje)
              -- Filtro para agendamentos anuais
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = EXTRACT(MONTH FROM :hoje))
              )
              -- Ainda não foi pago
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                    AND t.usuario_id = a.usuario_id
                    AND t.data_transacao >= :data_limite_transacao
                    AND t.data_transacao <= :hoje
              )
              -- Exclui débitos recorrentes de cartão (vão para a fatura)
              AND NOT (a.tipo_agendamento = 'FIXO' AND g.nome_grupo = 'Despesa' AND c.tipo_conta = 'Cartão de Crédito')
            ORDER BY g.nome_grupo, a.descricao
        """)

    @staticmethod
    def get_lembrete_variavel_by_description() -> text:
        """
        Busca um agendamento (FIXO ou LEMBRETE_VARIAVEL) por descrição (busca fuzzy).

        Usado em: Extract income/expense params (quando valor não é informado)

        REGRA: Quando o usuário diz "recebi X" ou "paguei X" sem valor, busca
        qualquer agendamento recorrente (FIXO ou LEMBRETE_VARIAVEL) e usa o valor_previsto.

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :descricao (str) - Descrição para buscar (usa ILIKE para busca parcial)

        Retorna: Agendamento que corresponde à descrição
        """
        return text("""
            SELECT
                a.id, a.descricao, a.valor_previsto,
                a.conta_id, a.subcategoria_id,
                c.nome_conta, c.tipo_conta,
                s.nome_sub as categoria,
                g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.descricao ILIKE :descricao
            ORDER BY
              -- Priorizar matches exatos
              CASE WHEN LOWER(a.descricao) = LOWER(:descricao_exact) THEN 0 ELSE 1 END,
              -- Priorizar LEMBRETE_VARIAVEL sobre FIXO (mais flexível)
              CASE WHEN a.tipo_agendamento = 'LEMBRETE_VARIAVEL' THEN 0 ELSE 1 END,
              a.descricao
            LIMIT 1
        """)

    @staticmethod
    def get_parametros_padrao(usuario_id: int, hoje) -> Dict[str, Any]:
        """
        Retorna parâmetros padrão usados na maioria das queries.

        Args:
            usuario_id: ID do usuário
            hoje: Data atual (date object)

        Returns:
            Dict com parâmetros comuns
        """
        from datetime import timedelta

        return {
            "uid": usuario_id,
            "hoje": hoje,
            "target_date": hoje,
            # dia_minimo removido - não mais necessário com nova query usando CTE
            "data_limite_transacao": hoje - timedelta(days=60),  # Aceita pagamentos dos últimos 60 dias
            "data_minima": hoje - timedelta(days=30),  # Últimos 30 dias (para query de atrasadas)
            "data_maxima": hoje - timedelta(days=7),   # CORRIGIDO: era days=8, agora days=7 (atrasadas há mais de 7 dias)
        }

    @staticmethod
    def get_parametros_busca_lembrete(usuario_id: int, descricao: str) -> Dict[str, Any]:
        """
        Retorna parâmetros para busca de lembrete variável por descrição.

        Args:
            usuario_id: ID do usuário
            descricao: Descrição do agendamento (ex: "salário", "meu salário")

        Returns:
            Dict com parâmetros para busca
        """
        return {
            "uid": usuario_id,
            "descricao": f"%{descricao}%",  # Busca parcial com ILIKE
            "descricao_exact": descricao  # Para priorizar match exato
        }
