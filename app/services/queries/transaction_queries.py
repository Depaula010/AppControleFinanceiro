"""
Queries SQL centralizadas para Transações (Transacoes).

Este módulo contém todas as queries SQL relacionadas a transações financeiras
para evitar duplicação de código e facilitar manutenção.

IMPORTANTE: Ao modificar uma query aqui, a mudança afeta TODOS os lugares que a utilizam.
"""

from sqlalchemy import text
from typing import Dict, Any
from datetime import date


class TransactionQueries:
    """
    Queries SQL reutilizáveis para operações com Transações.

    Transações representam movimentações financeiras:
    - Despesas (valor negativo)
    - Receitas (valor positivo)
    - Transferências (duas transações: origem negativa, destino positiva)
    """

    @staticmethod
    def check_transaction_exists_in_month() -> text:
        """
        Verifica se já existe uma transação com a mesma descrição em um mês específico.

        Usado em:
        - Verificar se agendamento já foi pago no mês
        - Evitar duplicação de transações recorrentes
        - Check-in noturno (validar pagamentos)

        Parâmetros necessários:
            :descricao (str) - Descrição da transação
            :usuario_id (int) - ID do usuário
            :mes_ref (int) - Mês de referência (1-12)
            :ano_ref (int) - Ano de referência (ex: 2025)

        Retorna: ID da transação se existir, ou NULL

        NOTA: Busca por mês completo, não por data específica
        """
        return text("""
            SELECT id
            FROM Transacoes
            WHERE descricao = :descricao
              AND usuario_id = :usuario_id
              AND EXTRACT(MONTH FROM data_transacao) = :mes_ref
              AND EXTRACT(YEAR FROM data_transacao) = :ano_ref
            LIMIT 1
        """)

    @staticmethod
    def check_transaction_exists_in_period() -> text:
        """
        Verifica se já existe uma transação com a mesma descrição em um período.

        Usado em:
        - NOVO: Verificar pagamentos nos últimos 60 dias (bugfix Kotas)
        - Validação de pagamentos atrasados
        - Busca mais flexível de transações

        Parâmetros necessários:
            :descricao (str) - Descrição da transação
            :usuario_id (int) - ID do usuário
            :data_inicio (date) - Data inicial do período
            :data_fim (date) - Data final do período

        Retorna: ID da transação se existir, ou NULL

        VANTAGEM: Mais flexível que check_in_month, aceita qualquer período
        """
        return text("""
            SELECT id
            FROM Transacoes
            WHERE descricao = :descricao
              AND usuario_id = :usuario_id
              AND data_transacao >= :data_inicio
              AND data_transacao <= :data_fim
            LIMIT 1
        """)

    @staticmethod
    def get_account_balance() -> text:
        """
        Calcula saldo de uma conta (soma de todas as transações).

        Usado em:
        - Exibir saldo disponível
        - Validar transferências (saldo suficiente)
        - Dashboard e relatórios

        Parâmetros necessários:
            :conta_id (int) - ID da conta

        Retorna: saldo_atual (DECIMAL)

        NOTA: Retorna 0 se conta não tem transações
        """
        return text("""
            SELECT COALESCE(SUM(valor), 0) as saldo_atual
            FROM Transacoes
            WHERE conta_id = :conta_id
        """)

    @staticmethod
    def get_recent_transactions() -> text:
        """
        Busca transações recentes de um usuário.

        Usado em:
        - Extrato bancário
        - Histórico de movimentações
        - Dashboard

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :limit (int) - Número máximo de transações

        Retorna: Lista de transações com informações completas
        """
        return text("""
            SELECT
                t.id,
                t.descricao,
                t.valor,
                t.data_transacao,
                t.tipo_transacao,
                c.nome_conta,
                c.tipo_conta,
                s.nome_sub as categoria,
                m.nome_macro,
                g.nome_grupo
            FROM Transacoes t
            JOIN Contas c ON t.conta_id = c.id
            JOIN SubCategoria s ON t.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE c.usuario_id = :uid
            ORDER BY t.data_transacao DESC, t.id DESC
            LIMIT :limit
        """)

    @staticmethod
    def get_transactions_by_date_range() -> text:
        """
        Busca transações em um período específico.

        Usado em:
        - Relatórios mensais
        - Análise de gastos por período
        - Exportação de dados

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :data_inicio (date) - Data inicial
            :data_fim (date) - Data final

        Retorna: Lista de transações com informações completas
        """
        return text("""
            SELECT
                t.id,
                t.descricao,
                t.valor,
                t.data_transacao,
                t.tipo_transacao,
                c.nome_conta,
                c.tipo_conta,
                s.nome_sub as categoria,
                m.nome_macro,
                g.nome_grupo
            FROM Transacoes t
            JOIN Contas c ON t.conta_id = c.id
            JOIN SubCategoria s ON t.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE c.usuario_id = :uid
              AND t.data_transacao >= :data_inicio
              AND t.data_transacao <= :data_fim
            ORDER BY t.data_transacao DESC, t.id DESC
        """)

    @staticmethod
    def get_transactions_by_category() -> text:
        """
        Busca transações de uma categoria específica.

        Usado em:
        - Análise de gastos por categoria
        - Detalhamento de despesas

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :subcategoria_id (int) - ID da subcategoria
            :data_inicio (date, opcional) - Data inicial
            :data_fim (date, opcional) - Data final

        Retorna: Lista de transações da categoria
        """
        return text("""
            SELECT
                t.id,
                t.descricao,
                t.valor,
                t.data_transacao,
                t.tipo_transacao,
                c.nome_conta
            FROM Transacoes t
            JOIN Contas c ON t.conta_id = c.id
            WHERE c.usuario_id = :uid
              AND t.subcategoria_id = :subcategoria_id
              AND (:data_inicio IS NULL OR t.data_transacao >= :data_inicio)
              AND (:data_fim IS NULL OR t.data_transacao <= :data_fim)
            ORDER BY t.data_transacao DESC
        """)

    @staticmethod
    def delete_transaction() -> text:
        """
        Deleta uma transação (validando que pertence ao usuário).

        Usado em:
        - Exclusão de transações
        - Correção de lançamentos errados

        Parâmetros necessários:
            :transaction_id (int) - ID da transação
            :uid (int) - ID do usuário (validação de propriedade)

        Retorna: Nenhum (DELETE)
        """
        return text("""
            DELETE FROM Transacoes
            WHERE id = :transaction_id
              AND usuario_id = :uid
        """)

    @staticmethod
    def get_parametros_verificacao_mes(descricao: str, usuario_id: int, data_ref: date) -> Dict[str, Any]:
        """
        Retorna parâmetros para verificar transação no mês.

        Args:
            descricao: Descrição da transação
            usuario_id: ID do usuário
            data_ref: Data de referência

        Returns:
            Dict com parâmetros
        """
        return {
            "descricao": descricao,
            "usuario_id": usuario_id,
            "mes_ref": data_ref.month,
            "ano_ref": data_ref.year
        }

    @staticmethod
    def get_parametros_verificacao_periodo(descricao: str, usuario_id: int, data_inicio: date, data_fim: date) -> Dict[str, Any]:
        """
        Retorna parâmetros para verificar transação no período.

        Args:
            descricao: Descrição da transação
            usuario_id: ID do usuário
            data_inicio: Data inicial
            data_fim: Data final

        Returns:
            Dict com parâmetros
        """
        return {
            "descricao": descricao,
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }
