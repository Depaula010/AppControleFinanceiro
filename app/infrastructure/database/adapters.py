"""
Adaptadores para migração gradual SQL → ORM usando Feature Flags.

Este módulo fornece funções wrapper que verificam feature flags e roteiam
chamadas para ORM (novo) ou SQL legado (antigo).

Usage:
    from app.infrastructure.database.adapters import get_user_by_whatsapp

    # Automaticamente usa ORM ou SQL baseado em feature flags
    user = get_user_by_whatsapp("+5511999999999")
"""

from typing import Optional, List
from datetime import date
from decimal import Decimal
from sqlalchemy.orm import Session
from sqlalchemy import text

from app.core.feature_flags import feature_flags
from app.core.dependencies import get_db_session
from app.infrastructure.database.models import UserModel, AccountModel, TransactionModel


# ============================================================================
# USER ADAPTERS
# ============================================================================

def get_user_by_whatsapp(numero_whatsapp: str) -> Optional[dict]:
    """
    Busca usuário por WhatsApp.

    Args:
        numero_whatsapp: Número do WhatsApp

    Returns:
        Dicionário com dados do usuário ou None
    """
    if feature_flags.use_orm_for_users:
        # Usar ORM
        from app.core import get_user_repository

        user_repo = get_user_repository()
        user = user_repo.get_by_whatsapp(numero_whatsapp)

        if user is None:
            return None

        return {
            'id': user.id,
            'nome': user.nome,
            'numero_whatsapp': user.numero_whatsapp,
            'email': user.email,
            'conta_padrao_id': user.conta_padrao_id,
            'fuso_horario': user.fuso_horario,
            'ativo': user.ativo,
            'api_key_automate': user.api_key_automate,
            'ultimo_acesso': user.ultimo_acesso,
            'created_at': user.created_at,
        }
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql = text("""
            SELECT id, nome, numero_whatsapp, email, conta_padrao_id,
                   fuso_horario, ativo, api_key_automate, ultimo_acesso, created_at
            FROM "Usuarios"
            WHERE numero_whatsapp = :numero
        """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {"numero": numero_whatsapp})
            row = result.fetchone()

            if row is None:
                return None

            return {
                'id': row[0],
                'nome': row[1],
                'numero_whatsapp': row[2],
                'email': row[3],
                'conta_padrao_id': row[4],
                'fuso_horario': row[5],
                'ativo': row[6],
                'api_key_automate': row[7],
                'ultimo_acesso': row[8],
                'created_at': row[9],
            }


def get_user_by_id(user_id: int) -> Optional[dict]:
    """
    Busca usuário por ID.

    Args:
        user_id: ID do usuário

    Returns:
        Dicionário com dados do usuário ou None
    """
    if feature_flags.use_orm_for_users:
        # Usar ORM
        from app.core import get_user_repository

        user_repo = get_user_repository()
        user = user_repo.get_by_id(user_id)

        if user is None:
            return None

        return {
            'id': user.id,
            'nome': user.nome,
            'numero_whatsapp': user.numero_whatsapp,
            'email': user.email,
            'conta_padrao_id': user.conta_padrao_id,
            'fuso_horario': user.fuso_horario,
            'ativo': user.ativo,
            'api_key_automate': user.api_key_automate,
            'ultimo_acesso': user.ultimo_acesso,
            'created_at': user.created_at,
        }
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql = text("""
            SELECT id, nome, numero_whatsapp, email, conta_padrao_id,
                   fuso_horario, ativo, api_key_automate, ultimo_acesso, created_at
            FROM "Usuarios"
            WHERE id = :user_id
        """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {"user_id": user_id})
            row = result.fetchone()

            if row is None:
                return None

            return {
                'id': row[0],
                'nome': row[1],
                'numero_whatsapp': row[2],
                'email': row[3],
                'conta_padrao_id': row[4],
                'fuso_horario': row[5],
                'ativo': row[6],
                'api_key_automate': row[7],
                'ultimo_acesso': row[8],
                'created_at': row[9],
            }


def update_user_last_access(user_id: int) -> bool:
    """
    Atualiza último acesso do usuário.

    Args:
        user_id: ID do usuário

    Returns:
        True se atualizou com sucesso
    """
    if feature_flags.use_orm_for_users:
        # Usar ORM
        from app.core import get_user_repository

        user_repo = get_user_repository()
        return user_repo.update_last_access(user_id)
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql = text("""
            UPDATE "Usuarios"
            SET ultimo_acesso = CURRENT_TIMESTAMP
            WHERE id = :user_id
        """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {"user_id": user_id})
            conn.commit()
            return result.rowcount > 0


# ============================================================================
# ACCOUNT ADAPTERS
# ============================================================================

def get_accounts_by_user(user_id: int, active_only: bool = False) -> List[dict]:
    """
    Lista contas de um usuário.

    Args:
        user_id: ID do usuário
        active_only: Se True, retorna apenas contas ativas

    Returns:
        Lista de dicionários com dados das contas
    """
    if feature_flags.use_orm_for_accounts:
        # Usar ORM
        from app.core import get_account_repository

        account_repo = get_account_repository()

        if active_only:
            accounts = account_repo.get_active_by_user(user_id)
        else:
            accounts = account_repo.get_by_user(user_id)

        return [
            {
                'id': acc.id,
                'usuario_id': acc.usuario_id,
                'nome_conta': acc.nome_conta,
                'tipo_conta': acc.tipo_conta,
                'saldo_inicial': float(acc.saldo_inicial),
                'ativa': acc.ativa,
                'ordem': acc.ordem,
                'dia_vencimento': acc.dia_vencimento,
                'dia_fechamento': acc.dia_fechamento,
                'limite_credito': float(acc.limite_credito) if acc.limite_credito else None,
            }
            for acc in accounts
        ]
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        if active_only:
            sql = text("""
                SELECT id, usuario_id, nome_conta, tipo_conta, saldo_inicial,
                       ativa, ordem, dia_vencimento, dia_fechamento, limite_credito
                FROM "Contas"
                WHERE usuario_id = :user_id AND ativa = true
                ORDER BY ordem NULLS LAST, nome_conta
            """)
        else:
            sql = text("""
                SELECT id, usuario_id, nome_conta, tipo_conta, saldo_inicial,
                       ativa, ordem, dia_vencimento, dia_fechamento, limite_credito
                FROM "Contas"
                WHERE usuario_id = :user_id
                ORDER BY ordem NULLS LAST, nome_conta
            """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {"user_id": user_id})
            rows = result.fetchall()

            return [
                {
                    'id': row[0],
                    'usuario_id': row[1],
                    'nome_conta': row[2],
                    'tipo_conta': row[3],
                    'saldo_inicial': float(row[4]),
                    'ativa': row[5],
                    'ordem': row[6],
                    'dia_vencimento': row[7],
                    'dia_fechamento': row[8],
                    'limite_credito': float(row[9]) if row[9] else None,
                }
                for row in rows
            ]


def get_account_by_id(account_id: int) -> Optional[dict]:
    """
    Busca conta por ID.

    Args:
        account_id: ID da conta

    Returns:
        Dicionário com dados da conta ou None
    """
    if feature_flags.use_orm_for_accounts:
        # Usar ORM
        from app.core import get_account_repository

        account_repo = get_account_repository()
        account = account_repo.get_by_id(account_id)

        if account is None:
            return None

        return {
            'id': account.id,
            'usuario_id': account.usuario_id,
            'nome_conta': account.nome_conta,
            'tipo_conta': account.tipo_conta,
            'saldo_inicial': float(account.saldo_inicial),
            'ativa': account.ativa,
            'ordem': account.ordem,
            'dia_vencimento': account.dia_vencimento,
            'dia_fechamento': account.dia_fechamento,
            'limite_credito': float(account.limite_credito) if account.limite_credito else None,
        }
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql = text("""
            SELECT id, usuario_id, nome_conta, tipo_conta, saldo_inicial,
                   ativa, ordem, dia_vencimento, dia_fechamento, limite_credito
            FROM "Contas"
            WHERE id = :account_id
        """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {"account_id": account_id})
            row = result.fetchone()

            if row is None:
                return None

            return {
                'id': row[0],
                'usuario_id': row[1],
                'nome_conta': row[2],
                'tipo_conta': row[3],
                'saldo_inicial': float(row[4]),
                'ativa': row[5],
                'ordem': row[6],
                'dia_vencimento': row[7],
                'dia_fechamento': row[8],
                'limite_credito': float(row[9]) if row[9] else None,
            }


# ============================================================================
# TRANSACTION ADAPTERS
# ============================================================================

def get_transactions_by_period(
    user_id: int,
    data_inicio: date,
    data_fim: date
) -> List[dict]:
    """
    Lista transações de um usuário em um período.

    Args:
        user_id: ID do usuário
        data_inicio: Data inicial
        data_fim: Data final

    Returns:
        Lista de dicionários com dados das transações
    """
    if feature_flags.use_orm_for_transactions:
        # Usar ORM
        from app.core import get_transaction_repository

        transaction_repo = get_transaction_repository()
        transactions = transaction_repo.get_by_period(user_id, data_inicio, data_fim)

        return [
            {
                'id': t.id,
                'usuario_id': t.usuario_id,
                'conta_id': t.conta_id,
                'descricao': t.descricao,
                'valor': float(t.valor),
                'data_transacao': t.data_transacao,
                'tipo_transacao': t.tipo_transacao,
                'consolidada': t.consolidada,
                'subcategoria_id': t.subcategoria_id,
                'fatura_id': t.fatura_id,
            }
            for t in transactions
        ]
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql = text("""
            SELECT id, usuario_id, conta_id, descricao, valor, data_transacao,
                   tipo_transacao, consolidada, subcategoria_id, fatura_id
            FROM "Transacoes"
            WHERE usuario_id = :user_id
              AND data_transacao >= :data_inicio
              AND data_transacao <= :data_fim
            ORDER BY data_transacao DESC, id DESC
        """)

        with get_db_connection() as conn:
            result = conn.execute(sql, {
                "user_id": user_id,
                "data_inicio": data_inicio,
                "data_fim": data_fim
            })
            rows = result.fetchall()

            return [
                {
                    'id': row[0],
                    'usuario_id': row[1],
                    'conta_id': row[2],
                    'descricao': row[3],
                    'valor': float(row[4]),
                    'data_transacao': row[5],
                    'tipo_transacao': row[6],
                    'consolidada': row[7],
                    'subcategoria_id': row[8],
                    'fatura_id': row[9],
                }
                for row in rows
            ]


def calculate_financial_summary(
    user_id: int,
    data_inicio: date,
    data_fim: date
) -> dict:
    """
    Calcula resumo financeiro de um usuário em um período.

    Args:
        user_id: ID do usuário
        data_inicio: Data inicial
        data_fim: Data final

    Returns:
        Dicionário com receitas, despesas e saldo
    """
    if feature_flags.use_orm_for_transactions:
        # Usar ORM
        from app.core import get_transaction_repository

        transaction_repo = get_transaction_repository()

        receitas = transaction_repo.calculate_total_income(
            user_id, data_inicio, data_fim
        )
        despesas = transaction_repo.calculate_total_expenses(
            user_id, data_inicio, data_fim
        )

        return {
            'receitas': float(receitas),
            'despesas': float(despesas),
            'saldo': float(receitas - despesas),
        }
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        sql_receitas = text("""
            SELECT COALESCE(SUM(valor), 0)
            FROM "Transacoes"
            WHERE usuario_id = :user_id
              AND tipo_transacao = 'Renda'
              AND data_transacao >= :data_inicio
              AND data_transacao <= :data_fim
              AND consolidada = true
        """)

        sql_despesas = text("""
            SELECT COALESCE(SUM(valor), 0)
            FROM "Transacoes"
            WHERE usuario_id = :user_id
              AND tipo_transacao = 'Despesa'
              AND data_transacao >= :data_inicio
              AND data_transacao <= :data_fim
              AND consolidada = true
        """)

        with get_db_connection() as conn:
            receitas = conn.execute(sql_receitas, {
                "user_id": user_id,
                "data_inicio": data_inicio,
                "data_fim": data_fim
            }).scalar()

            despesas = conn.execute(sql_despesas, {
                "user_id": user_id,
                "data_inicio": data_inicio,
                "data_fim": data_fim
            }).scalar()

            return {
                'receitas': float(receitas),
                'despesas': float(despesas),
                'saldo': float(receitas - despesas),
            }


def calculate_account_balance(account_id: int, up_to_date: Optional[date] = None) -> float:
    """
    Calcula saldo de uma conta até uma data.

    Args:
        account_id: ID da conta
        up_to_date: Data limite (None = todas as transações)

    Returns:
        Saldo calculado
    """
    if feature_flags.use_orm_for_transactions:
        # Usar ORM
        from app.core import get_transaction_repository

        transaction_repo = get_transaction_repository()
        saldo = transaction_repo.calculate_balance(account_id, up_to_date)
        return float(saldo)
    else:
        # Usar SQL legado
        from app.database import get_db_connection

        # Buscar saldo inicial
        sql_saldo_inicial = text("""
            SELECT saldo_inicial FROM "Contas" WHERE id = :account_id
        """)

        # Query base para transações
        if up_to_date:
            sql_transacoes = text("""
                SELECT tipo_transacao, COALESCE(SUM(valor), 0)
                FROM "Transacoes"
                WHERE conta_id = :account_id
                  AND consolidada = true
                  AND data_transacao <= :up_to_date
                GROUP BY tipo_transacao
            """)
        else:
            sql_transacoes = text("""
                SELECT tipo_transacao, COALESCE(SUM(valor), 0)
                FROM "Transacoes"
                WHERE conta_id = :account_id
                  AND consolidada = true
                GROUP BY tipo_transacao
            """)

        with get_db_connection() as conn:
            saldo_inicial = conn.execute(
                sql_saldo_inicial, {"account_id": account_id}
            ).scalar() or Decimal('0.00')

            params = {"account_id": account_id}
            if up_to_date:
                params["up_to_date"] = up_to_date

            result = conn.execute(sql_transacoes, params)
            rows = result.fetchall()

            receitas = Decimal('0.00')
            despesas = Decimal('0.00')

            for tipo, valor in rows:
                if tipo == 'Renda':
                    receitas = valor
                elif tipo == 'Despesa':
                    despesas = valor

            return float(saldo_inicial + receitas - despesas)
