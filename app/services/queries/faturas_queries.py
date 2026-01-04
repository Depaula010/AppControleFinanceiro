"""
Queries SQL centralizadas para Faturas.

Este módulo contém todas as queries SQL relacionadas a faturas de cartão de crédito
para evitar duplicação de código e facilitar manutenção.
"""

from sqlalchemy import text
from typing import Dict, Any


class FaturasQueries:
    """
    Queries SQL reutilizáveis para operações com Faturas.
    """

    @staticmethod
    def get_faturas_vencidas() -> text:
        """
        Busca faturas vencidas (status Aberta e data_vencimento < hoje).

        Usado em:
        - Alerta de faturas vencidas (job noturno)
        - Intent "Contas Atrasadas" (chatbot)

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :hoje (date) - Data atual
            :limite_inferior (date) - Data mínima para buscar (ex: hoje - 30 dias)

        Retorna: Faturas vencidas com valor total calculado
        """
        return text("""
            SELECT
                f.id,
                c.nome_conta as cartao,
                f.data_vencimento,
                f.status,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
              AND f.status = 'Aberta'
              AND f.data_vencimento < :hoje
              AND f.data_vencimento >= :limite_inferior
            GROUP BY f.id, c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento DESC
        """)

    @staticmethod
    def get_faturas_vencendo_em_x_dias() -> text:
        """
        Busca faturas que vencem em X dias.

        Usado em: Alerta de vencimento próximo (3 dias antes)

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :data_vencimento_alvo (date) - Data de vencimento a buscar (ex: hoje + 3 dias)

        Retorna: Faturas que vencem na data especificada
        """
        return text("""
            SELECT
                f.id,
                c.nome_conta as cartao,
                f.data_vencimento,
                f.status,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
              AND f.status = 'Aberta'
              AND f.data_vencimento = :data_vencimento_alvo
            GROUP BY f.id, c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento
        """)

    @staticmethod
    def get_invoice_by_due_date() -> text:
        """
        Busca fatura de um cartão por data de vencimento.

        Usado em:
        - Verificar se fatura já existe para um período
        - Evitar duplicação de faturas
        - get_or_create_fatura()

        Parâmetros necessários:
            :cid (int) - ID da conta (cartão)
            :dv (date) - Data de vencimento

        Retorna: ID da fatura ou NULL
        """
        return text("""
            SELECT id
            FROM Faturas
            WHERE conta_id = :cid
              AND data_vencimento = :dv
            LIMIT 1
        """)

    @staticmethod
    def get_open_invoices_by_account() -> text:
        """
        Busca faturas abertas de uma conta específica.

        Usado em:
        - Listar faturas pendentes de um cartão
        - Dashboard de faturas

        Parâmetros necessários:
            :conta_id (int) - ID da conta (cartão)

        Retorna: Lista de faturas abertas com valor total
        """
        return text("""
            SELECT
                f.id,
                f.data_vencimento,
                f.data_fechamento,
                f.status,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
            FROM Faturas f
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE f.conta_id = :conta_id
              AND f.status = 'Aberta'
            GROUP BY f.id, f.data_vencimento, f.data_fechamento, f.status
            ORDER BY f.data_vencimento
        """)

    @staticmethod
    def get_current_month_invoice() -> text:
        """
        Busca fatura do mês atual para um cartão.

        Usado em:
        - Registrar despesa no cartão (buscar fatura corrente)
        - Exibir fatura atual

        Parâmetros necessários:
            :conta_id (int) - ID da conta (cartão)
            :mes_ref (int) - Mês de referência (1-12)
            :ano_ref (int) - Ano de referência

        Retorna: ID da fatura ou NULL
        """
        return text("""
            SELECT id
            FROM Faturas
            WHERE conta_id = :conta_id
              AND EXTRACT(MONTH FROM data_vencimento) = :mes_ref
              AND EXTRACT(YEAR FROM data_vencimento) = :ano_ref
            LIMIT 1
        """)

    @staticmethod
    def get_parametros_padrao(usuario_id: int, hoje) -> Dict[str, Any]:
        """
        Retorna parâmetros padrão usados na maioria das queries de faturas.

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
            "limite_inferior": hoje - timedelta(days=30),  # Últimas 30 dias
        }

    @staticmethod
    def get_parametros_fatura_por_vencimento(conta_id: int, data_vencimento) -> Dict[str, Any]:
        """
        Retorna parâmetros para buscar fatura por vencimento.

        Args:
            conta_id: ID da conta (cartão)
            data_vencimento: Data de vencimento da fatura

        Returns:
            Dict com parâmetros
        """
        return {
            "cid": conta_id,
            "dv": data_vencimento
        }
