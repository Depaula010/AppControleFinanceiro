# app/services/finance/transaction_service.py
"""
Serviço de criação e gerenciamento de transações.

Este módulo contém funções para criar diferentes tipos de transações:
- Transações simples (Renda/Despesa)
- Transferências entre contas
- Pagamentos de faturas
"""

from datetime import date
from typing import Optional, Tuple
from ._database import text, Connection


def create_transaction(
    conn: Connection,
    usuario_id: int,
    conta_id: int,
    subcategoria_id: int,
    fatura_id: Optional[int],
    descricao: str,
    valor: float,
    tipo_transacao: str,
    data_transacao: date
) -> int:
    """
    Insere uma transação simples (Renda/Despesa).

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        conta_id: ID da conta
        subcategoria_id: ID da subcategoria
        fatura_id: ID da fatura (None para transações não relacionadas a faturas)
        descricao: Descrição da transação
        valor: Valor da transação (negativo para despesas)
        tipo_transacao: 'Renda' ou 'Despesa'
        data_transacao: Data da transação

    Returns:
        ID da transação criada
    """
    sql = text("""
        INSERT INTO Transacoes
        (usuario_id, conta_id, subcategoria_id, fatura_id, transferencia_par_id, descricao, valor, tipo_transacao, data_transacao)
        VALUES (:uid, :cid, :scid, :fid, NULL, :desc, :val, :tipo, :data)
        RETURNING id
    """)
    result = conn.execute(sql, {
        "uid": usuario_id,
        "cid": conta_id,
        "scid": subcategoria_id,
        "fid": fatura_id,
        "desc": descricao,
        "val": valor,
        "tipo": tipo_transacao,
        "data": data_transacao
    })
    return result.scalar_one()


def create_transfer_pair(
    conn: Connection,
    usuario_id: int,
    conta_id_origem: int,
    conta_id_destino: int,
    valor: float,
    data_transacao: date
) -> Tuple[str, str]:
    """
    Cria o par de transações (entrada/saída) para uma transferência.

    Cria duas transações vinculadas:
    - Saída da conta origem (valor negativo)
    - Entrada na conta destino (valor positivo)

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        conta_id_origem: ID da conta de origem
        conta_id_destino: ID da conta de destino
        valor: Valor da transferência (positivo)
        data_transacao: Data da transferência

    Returns:
        Tupla (nome_conta_origem, nome_conta_destino)

    Raises:
        Exception: Se subcategoria 'Investimentos de Curto Prazo' não for encontrada
    """
    valor_saida = (float(valor) * -1)
    valor_entrada = float(valor)

    # Buscar subcategoria para transferências
    sql_get_subcat = text("""
        SELECT s.id
        FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE g.nome_grupo = 'Meta Financeira'
          AND s.nome_sub = 'Investimentos de Curto Prazo'
          AND (s.usuario_id IS NULL OR s.usuario_id = :uid)
        LIMIT 1
    """)
    id_subcat_transfer = conn.execute(sql_get_subcat, {"uid": usuario_id}).scalar_one_or_none()
    if not id_subcat_transfer:
        raise Exception("Subcategoria 'Investimentos de Curto Prazo' não encontrada.")

    # Buscar nomes das contas
    sql_get_nomes = text("SELECT nome_conta FROM Contas WHERE id = :id")
    nome_origem = conn.execute(sql_get_nomes, {"id": conta_id_origem}).scalar()
    nome_destino = conn.execute(sql_get_nomes, {"id": conta_id_destino}).scalar()

    # Inserir transações
    sql_insert = text("""
        INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, descricao, valor, tipo_transacao, data_transacao)
        VALUES (:uid, :cid, :scid, :desc, :val, 'Transferência', :data)
        RETURNING id
    """)

    # Transação de saída
    desc_saida = f"Transferência para {nome_destino}"
    result_saida = conn.execute(sql_insert, {
        "uid": usuario_id,
        "cid": conta_id_origem,
        "scid": id_subcat_transfer,
        "desc": desc_saida,
        "val": valor_saida,
        "data": data_transacao
    })
    id_transacao_saida = result_saida.scalar_one()

    # Transação de entrada
    desc_entrada = f"Transferência de {nome_origem}"
    result_entrada = conn.execute(sql_insert, {
        "uid": usuario_id,
        "cid": conta_id_destino,
        "scid": id_subcat_transfer,
        "desc": desc_entrada,
        "val": valor_entrada,
        "data": data_transacao
    })
    id_transacao_entrada = result_entrada.scalar_one()

    # Vincular transações (transferencia_par_id)
    sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo")
    conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida})
    conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})

    return nome_origem, nome_destino


def create_fatura_payment(
    conn: Connection,
    usuario_id: int,
    conta_id_origem: int,
    conta_id_cartao: int,
    valor: float,
    data_transacao: date
) -> str:
    """
    Cria o par de transações (pagamento/recebimento) para uma fatura.

    Cria duas transações vinculadas e marca a fatura como paga:
    - Saída da conta corrente (pagamento)
    - Entrada no cartão (recebimento do pagamento)

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        conta_id_origem: ID da conta corrente (origem do pagamento)
        conta_id_cartao: ID da conta cartão
        valor: Valor do pagamento (positivo)
        data_transacao: Data do pagamento

    Returns:
        Nome do cartão

    Raises:
        Exception: Se subcategoria 'Quitação de Empréstimos (Principal)' não for encontrada
    """
    # Import local para evitar dependência circular
    from .invoice_service import get_or_create_fatura

    valor_saida = (float(valor) * -1)
    valor_entrada = float(valor)

    # Buscar/criar fatura
    fatura_id_pagar = get_or_create_fatura(conn, conta_id_cartao, data_transacao, usuario_id)

    # Buscar subcategoria para pagamentos de fatura
    sql_get_subcat = text("""
        SELECT s.id
        FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE g.nome_grupo = 'Meta Financeira'
          AND s.nome_sub = 'Quitação de Empréstimos (Principal)'
          AND (s.usuario_id IS NULL OR s.usuario_id = :uid)
        LIMIT 1
    """)
    id_subcat_pagto = conn.execute(sql_get_subcat, {"uid": usuario_id}).scalar_one_or_none()
    if not id_subcat_pagto:
        raise Exception("Subcategoria 'Quitação de Empréstimos (Principal)' não encontrada.")

    # Buscar nomes das contas
    sql_get_nomes = text("SELECT nome_conta FROM Contas WHERE id = :id")
    nome_origem = conn.execute(sql_get_nomes, {"id": conta_id_origem}).scalar()
    nome_cartao = conn.execute(sql_get_nomes, {"id": conta_id_cartao}).scalar()

    # Inserir transações
    sql_insert = text("""
        INSERT INTO Transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao, data_transacao)
        VALUES (:uid, :cid, :scid, :fid, :desc, :val, 'Pagamento Fatura', :data)
        RETURNING id
    """)

    # Transação de saída (pagamento)
    desc_saida = f"Pagamento Fatura {nome_cartao}"
    result_saida = conn.execute(sql_insert, {
        "uid": usuario_id,
        "cid": conta_id_origem,
        "scid": id_subcat_pagto,
        "fid": fatura_id_pagar,
        "desc": desc_saida,
        "val": valor_saida,
        "data": data_transacao
    })
    id_transacao_saida = result_saida.scalar_one()

    # Transação de entrada (recebimento)
    desc_entrada = f"Pagamento Recebido (de {nome_origem})"
    result_entrada = conn.execute(sql_insert, {
        "uid": usuario_id,
        "cid": conta_id_cartao,
        "scid": id_subcat_pagto,
        "fid": fatura_id_pagar,
        "desc": desc_entrada,
        "val": valor_entrada,
        "data": data_transacao
    })
    id_transacao_entrada = result_entrada.scalar_one()

    # Vincular transações
    sql_update_par = text("UPDATE Transacoes SET transferencia_par_id = :par_id WHERE id = :id_alvo")
    conn.execute(sql_update_par, {"par_id": id_transacao_entrada, "id_alvo": id_transacao_saida})
    conn.execute(sql_update_par, {"par_id": id_transacao_saida, "id_alvo": id_transacao_entrada})

    # Marcar fatura como paga
    sql_update_fatura = text("UPDATE Faturas SET status = 'Paga' WHERE id = :fid")
    conn.execute(sql_update_fatura, {"fid": fatura_id_pagar})

    return nome_cartao


__all__ = [
    'create_transaction',
    'create_transfer_pair',
    'create_fatura_payment',
]
