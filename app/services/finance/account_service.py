# app/services/finance/account_service.py
"""
Serviço de gerenciamento de contas.

Este módulo contém funções para buscar, gerenciar e calcular saldos de contas do usuário.
"""

from typing import List, Dict, Any, Optional, Tuple
from ._database import text, Connection


def get_user_accounts(conn: Connection, usuario_id: int) -> List[Any]:
    """
    Busca todas as contas de um usuário.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário

    Returns:
        Lista de tuplas (id, nome_conta, tipo_conta)
    """
    sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid")
    return conn.execute(sql, {"uid": usuario_id}).fetchall()


def get_account_by_name(
    conn: Connection,
    usuario_id: int,
    nome_conta: str,
    fallback: bool = False,
    tipo_conta: Optional[str] = None
) -> Optional[int]:
    """
    Busca um ID de conta pelo nome (exato ou ILIKE).

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        nome_conta: Nome da conta
        fallback: Se True, retorna primeira conta caso não encontre
        tipo_conta: Filtrar por tipo (ex: "Cartão de Crédito", "Conta Corrente")

    Returns:
        ID da conta ou None
    """
    tipo_filter = "AND tipo_conta = :tipo" if tipo_conta else ""
    params = {"uid": usuario_id, "nome": nome_conta}
    if tipo_conta:
        params["tipo"] = tipo_conta

    # Busca exata pelo nome
    sql_exact = text(f"SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta = :nome {tipo_filter}")
    conta_id = conn.execute(sql_exact, params).scalar_one_or_none()

    if conta_id:
        return conta_id

    # Busca parcial (fuzzy matching)
    params["nome_like"] = f"%{nome_conta}%"
    sql_like = text(f"SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome_like {tipo_filter}")
    result = conn.execute(sql_like, params).fetchall()

    # Só retorna se houver EXATAMENTE uma correspondência (evita matches ambíguos)
    if len(result) == 1:
        return result[0][0]

    # Fallback com filtro de tipo
    if fallback and tipo_conta:
        sql_fallback = text(f"SELECT id FROM Contas WHERE usuario_id = :uid AND tipo_conta = :tipo LIMIT 1")
        return conn.execute(sql_fallback, params).scalar_one()
    elif fallback:
        sql_fallback = text("SELECT id FROM Contas WHERE usuario_id = :uid LIMIT 1")
        return conn.execute(sql_fallback, params).scalar_one()

    return None


def get_account_details_by_name(
    conn: Connection,
    usuario_id: int,
    nome_conta: str
) -> Optional[Dict[str, Any]]:
    """
    Busca detalhes completos de uma conta pelo nome (id, nome, tipo).

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        nome_conta: Nome da conta

    Returns:
        Dict com {id, nome, tipo} ou None
    """
    # Tentar busca exata
    sql_exact = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid AND nome_conta = :nome")
    conta = conn.execute(sql_exact, {"uid": usuario_id, "nome": nome_conta}).fetchone()

    if conta:
        return {"id": conta[0], "nome": conta[1], "tipo": conta[2]}

    # Tentar busca parcial (ILIKE)
    sql_like = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE :nome_like")
    conta = conn.execute(sql_like, {"uid": usuario_id, "nome_like": f"%{nome_conta}%"}).fetchone()

    if conta:
        return {"id": conta[0], "nome": conta[1], "tipo": conta[2]}

    return None


def get_saldo_contas(
    conn: Connection,
    usuario_id: int,
    conta_id: Optional[int] = None
) -> List[Dict[str, Any]]:
    """
    Consulta o saldo atual das contas do usuário.

    Calcula saldo como: saldo_inicial + soma de transações.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id: ID da conta específica (opcional). Se None, retorna todas.

    Returns:
        List de dicts com saldos:
        [{
            "nome_conta": "Nubank",
            "tipo_conta": "Conta Corrente",
            "saldo": 1500.50
        }]
    """
    if conta_id:
        # Consultar saldo de uma conta específica
        sql = text("""
            SELECT
                c.nome_conta,
                c.tipo_conta,
                c.saldo_inicial + COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
                AND c.id = :cid
            GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
        """)
        result = conn.execute(sql, {"uid": usuario_id, "cid": conta_id}).fetchall()
    else:
        # Consultar todas as contas
        sql = text("""
            SELECT
                c.nome_conta,
                c.tipo_conta,
                c.saldo_inicial + COALESCE(SUM(t.valor), 0) as saldo
            FROM Contas c
            LEFT JOIN Transacoes t ON c.id = t.conta_id
            WHERE c.usuario_id = :uid
            GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
            ORDER BY c.tipo_conta, c.nome_conta
        """)
        result = conn.execute(sql, {"uid": usuario_id}).fetchall()

    contas = []
    for row in result:
        contas.append({
            "nome_conta": row[0],
            "tipo_conta": row[1],
            "saldo": float(row[2])
        })

    return contas


def update_saldo_inicial(
    conn: Connection,
    usuario_id: int,
    conta_id: int,
    novo_saldo_inicial: float
) -> None:
    """
    Atualiza o saldo_inicial de uma conta.

    IMPORTANTE: Isso afeta o cálculo de saldo de todas as transações.
    Use apenas para correções ou ajustes necessários.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        conta_id: ID da conta
        novo_saldo_inicial: Novo valor do saldo inicial
    """
    sql = text("""
        UPDATE Contas
        SET saldo_inicial = :novo_saldo
        WHERE id = :cid AND usuario_id = :uid
    """)
    conn.execute(sql, {
        "uid": usuario_id,
        "cid": conta_id,
        "novo_saldo": novo_saldo_inicial
    })


def get_user_default_accounts(conn: Connection, usuario_id: int) -> Tuple[Optional[int], Optional[int]]:
    """
    Retorna as contas padrão configuradas pelo usuário.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário

    Returns:
        Tupla (conta_renda_id, conta_despesa_id) ou (None, None) se não configurado
    """
    sql = text("""
        SELECT conta_padrao_renda_id, conta_padrao_despesa_id
        FROM Usuarios
        WHERE id = :uid
    """)
    result = conn.execute(sql, {"uid": usuario_id}).fetchone()

    if result:
        return (result.conta_padrao_renda_id, result.conta_padrao_despesa_id)
    return (None, None)


def set_user_default_account(
    conn: Connection,
    usuario_id: int,
    tipo: str,
    conta_id: int
) -> None:
    """
    Configura a conta padrão do usuário.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        tipo: 'renda' ou 'despesa'
        conta_id: ID da conta a ser configurada como padrão

    Raises:
        ValueError: Se tipo não for 'renda' ou 'despesa'
    """
    if tipo == 'renda':
        sql = text("UPDATE Usuarios SET conta_padrao_renda_id = :cid WHERE id = :uid")
    elif tipo == 'despesa':
        sql = text("UPDATE Usuarios SET conta_padrao_despesa_id = :cid WHERE id = :uid")
    else:
        raise ValueError("Tipo deve ser 'renda' ou 'despesa'")

    conn.execute(sql, {"uid": usuario_id, "cid": conta_id})


def choose_account_for_transaction(
    conn: Connection,
    usuario_id: int,
    texto_msg: str,
    tipo_transacao: str
) -> Tuple[Optional[int], Optional[str], Optional[str], Optional[str]]:
    """
    Escolhe a conta para uma transação seguindo ordem de prioridade:
    1. Conta mencionada na mensagem (fuzzy matching)
    2. Conta padrão configurada pelo usuário
    3. Fallback: primeira conta disponível

    Args:
        conn: Conexão do banco
        usuario_id: ID do usuário
        texto_msg: Mensagem do WhatsApp
        tipo_transacao: 'Renda' ou 'Despesa'

    Returns:
        Tupla (conta_id, conta_nome, conta_tipo, origem)
        origem: 'mencionada' | 'padrao' | 'fallback' | None
    """
    # Import local para evitar dependência circular
    from .text_utils import extract_mentioned_account

    # 1. Verificar se mencionou conta na mensagem
    conta_mencionada = extract_mentioned_account(conn, usuario_id, texto_msg)
    if conta_mencionada:
        conta_id, nome, tipo = conta_mencionada
        print(f"[ESCOLHA-CONTA] Usando conta MENCIONADA: {nome}")
        return (conta_id, nome, tipo, 'mencionada')

    # 2. Verificar conta padrão configurada
    conta_renda_id, conta_despesa_id = get_user_default_accounts(conn, usuario_id)

    if tipo_transacao == 'Renda' and conta_renda_id:
        # Buscar detalhes da conta padrão de renda
        sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid")
        conta = conn.execute(sql, {"cid": conta_renda_id, "uid": usuario_id}).fetchone()
        if conta:
            print(f"[ESCOLHA-CONTA] Usando conta padrão RENDA: {conta.nome_conta}")
            return (conta.id, conta.nome_conta, conta.tipo_conta, 'padrao')

    if tipo_transacao == 'Despesa' and conta_despesa_id:
        # Buscar detalhes da conta padrão de despesa
        sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE id = :cid AND usuario_id = :uid")
        conta = conn.execute(sql, {"cid": conta_despesa_id, "uid": usuario_id}).fetchone()
        if conta:
            print(f"[ESCOLHA-CONTA] Usando conta padrão DESPESA: {conta.nome_conta}")
            return (conta.id, conta.nome_conta, conta.tipo_conta, 'padrao')

    # 3. Fallback: primeira conta disponível
    sql = text("SELECT id, nome_conta, tipo_conta FROM Contas WHERE usuario_id = :uid LIMIT 1")
    conta = conn.execute(sql, {"uid": usuario_id}).fetchone()
    if conta:
        print(f"[ESCOLHA-CONTA] Usando conta FALLBACK: {conta.nome_conta}")
        return (conta.id, conta.nome_conta, conta.tipo_conta, 'fallback')

    # Sem contas disponíveis
    return (None, None, None, None)


__all__ = [
    'get_user_accounts',
    'get_account_by_name',
    'get_account_details_by_name',
    'get_saldo_contas',
    'update_saldo_inicial',
    'get_user_default_accounts',
    'set_user_default_account',
    'choose_account_for_transaction',
]
