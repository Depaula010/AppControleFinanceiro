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
