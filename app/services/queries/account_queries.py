"""
Queries SQL centralizadas para Contas (Accounts).

Este módulo contém todas as queries SQL relacionadas a contas bancárias e cartões
para evitar duplicação de código e facilitar manutenção.

IMPORTANTE: Ao modificar uma query aqui, a mudança afeta TODOS os lugares que a utilizam.
"""

from sqlalchemy import text
from typing import Dict, Any, Optional, List


class AccountQueries:
    """
    Queries SQL reutilizáveis para operações com Contas.

    Todas as queries estão documentadas com parâmetros necessários e uso.
    """

    @staticmethod
    def get_all_user_accounts() -> text:
        """
        Busca todas as contas de um usuário.

        Usado em:
        - Extração de parâmetros Gemini (transferências, pagamentos)
        - Listagem de contas disponíveis
        - Validação de contas do usuário

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: Lista de contas com id, nome_conta, tipo_conta
        """
        return text("""
            SELECT id, nome_conta, tipo_conta
            FROM Contas
            WHERE usuario_id = :uid
            ORDER BY nome_conta
        """)

    @staticmethod
    def get_account_by_exact_name() -> text:
        """
        Busca conta pelo nome exato (case-sensitive).

        Usado em:
        - Primeira tentativa de encontrar conta por nome
        - Validação de nome exato

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :nome (str) - Nome exato da conta
            :tipo (str, opcional) - Tipo da conta para filtrar

        Retorna: ID da conta ou None
        """
        return text("""
            SELECT id
            FROM Contas
            WHERE usuario_id = :uid
              AND nome_conta = :nome
              AND (:tipo IS NULL OR tipo_conta = :tipo)
            LIMIT 1
        """)

    @staticmethod
    def get_account_by_fuzzy_name() -> text:
        """
        Busca contas pelo nome parcial (ILIKE - case-insensitive).

        Usado em:
        - Fallback quando busca exata não funciona
        - Fuzzy matching para nomes aproximados

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :nome_like (str) - Pattern para busca (ex: "%inter%")
            :tipo (str, opcional) - Tipo da conta para filtrar

        Retorna: Lista de contas com id, nome_conta

        IMPORTANTE: Pode retornar múltiplos resultados!
        """
        return text("""
            SELECT id, nome_conta
            FROM Contas
            WHERE usuario_id = :uid
              AND nome_conta ILIKE :nome_like
              AND (:tipo IS NULL OR tipo_conta = :tipo)
            ORDER BY nome_conta
        """)

    @staticmethod
    def get_first_account_by_type() -> text:
        """
        Busca primeira conta do usuário de um tipo específico.

        Usado em:
        - Fallback quando não encontra conta específica
        - Buscar qualquer conta de crédito/débito

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :tipo (str) - Tipo da conta ("Cartão de Crédito", "Conta Corrente", etc)

        Retorna: ID da primeira conta do tipo
        """
        return text("""
            SELECT id
            FROM Contas
            WHERE usuario_id = :uid
              AND tipo_conta = :tipo
            ORDER BY nome_conta
            LIMIT 1
        """)

    @staticmethod
    def get_first_account() -> text:
        """
        Busca primeira conta do usuário (qualquer tipo).

        Usado em:
        - Último fallback quando não encontra nenhuma conta específica
        - Garantir que sempre há uma conta para usar

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: ID da primeira conta
        """
        return text("""
            SELECT id
            FROM Contas
            WHERE usuario_id = :uid
            ORDER BY nome_conta
            LIMIT 1
        """)

    @staticmethod
    def get_account_name() -> text:
        """
        Busca nome de uma conta pelo ID.

        Usado em:
        - Formatação de mensagens
        - Exibição de nomes de conta em logs

        Parâmetros necessários:
            :id (int) - ID da conta

        Retorna: nome_conta
        """
        return text("""
            SELECT nome_conta
            FROM Contas
            WHERE id = :id
        """)

    @staticmethod
    def get_credit_card_info() -> text:
        """
        Busca informações de cartão de crédito (datas de fechamento/vencimento).

        Usado em:
        - Criação/busca de faturas
        - Cálculo de período de fatura

        Parâmetros necessários:
            :conta_id (int) - ID do cartão
            :uid (int) - ID do usuário (validação)

        Retorna: dia_fechamento, dia_vencimento
        """
        return text("""
            SELECT dia_fechamento, dia_vencimento
            FROM Contas
            WHERE id = :conta_id
              AND usuario_id = :uid
              AND tipo_conta = 'Cartão de Crédito'
        """)

    @staticmethod
    def get_account_balance() -> text:
        """
        Busca saldo atual de uma conta.

        Usado em:
        - Validação de saldo antes de transferências
        - Exibição de saldo disponível

        Parâmetros necessários:
            :conta_id (int) - ID da conta

        Retorna: Saldo calculado (soma de transações)
        """
        return text("""
            SELECT COALESCE(SUM(valor), 0) as saldo_atual
            FROM Transacoes
            WHERE conta_id = :conta_id
        """)

    @staticmethod
    def get_parametros_busca_conta(
        usuario_id: int,
        nome_conta: str,
        tipo_conta: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Retorna parâmetros para buscar conta por nome (exato e fuzzy).

        Args:
            usuario_id: ID do usuário
            nome_conta: Nome da conta a buscar
            tipo_conta: Tipo da conta (opcional)

        Returns:
            Dict com parâmetros para as queries
        """
        return {
            "uid": usuario_id,
            "nome": nome_conta,
            "nome_like": f"%{nome_conta}%",
            "tipo": tipo_conta
        }

    @staticmethod
    def executar_busca_completa(conn, usuario_id: int, nome_conta: str, tipo_conta: Optional[str] = None) -> Optional[int]:
        """
        Executa busca completa de conta: exata → fuzzy → fallback.

        Esta é uma função helper que centraliza a lógica de busca de contas
        para evitar duplicação de código.

        Args:
            conn: Conexão com banco
            usuario_id: ID do usuário
            nome_conta: Nome da conta a buscar
            tipo_conta: Tipo da conta (opcional)

        Returns:
            ID da conta encontrada ou None

        Lógica:
        1. Tenta busca exata
        2. Se não encontrar, tenta fuzzy (retorna primeira se múltiplas)
        3. Se não encontrar, tenta primeira conta do tipo (se tipo fornecido)
        4. Se não encontrar, tenta primeira conta qualquer
        5. Se nada funcionar, retorna None
        """
        params = AccountQueries.get_parametros_busca_conta(usuario_id, nome_conta, tipo_conta)

        # 1. Busca exata
        sql_exact = AccountQueries.get_account_by_exact_name()
        conta_id = conn.execute(sql_exact, params).scalar_one_or_none()
        if conta_id:
            return conta_id

        # 2. Busca fuzzy
        sql_fuzzy = AccountQueries.get_account_by_fuzzy_name()
        result = conn.execute(sql_fuzzy, params).fetchall()

        if len(result) == 1:
            return result[0][0]
        elif len(result) > 1:
            # Múltiplos matches - retorna primeiro e loga aviso
            print(f"[FUZZY-MATCH] Múltiplos matches para '{nome_conta}': {[r[1] for r in result]}. Usando: {result[0][1]}")
            return result[0][0]

        # 3. Fallback por tipo (se fornecido)
        if tipo_conta:
            sql_by_type = AccountQueries.get_first_account_by_type()
            conta_id = conn.execute(sql_by_type, {"uid": usuario_id, "tipo": tipo_conta}).scalar_one_or_none()
            if conta_id:
                print(f"[FALLBACK] Conta '{nome_conta}' não encontrada. Usando primeira conta do tipo '{tipo_conta}'")
                return conta_id

        # 4. Fallback geral
        sql_first = AccountQueries.get_first_account()
        conta_id = conn.execute(sql_first, {"uid": usuario_id}).scalar_one_or_none()
        if conta_id:
            print(f"[FALLBACK] Conta '{nome_conta}' não encontrada. Usando primeira conta disponível")
            return conta_id

        return None
