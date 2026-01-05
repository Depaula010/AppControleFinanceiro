# app/services/finance/invoice_service.py
"""
Serviço de gerenciamento de faturas de cartão de crédito.

Este módulo contém funções para criar, gerenciar e calcular faturas de cartões.
"""

from datetime import date
from typing import Optional, List, Dict, Any
from ._database import text, monthrange, Connection


def get_or_create_fatura(conn, conta_id, data_transacao, usuario_id):
    """
    Serviço centralizado para calcular e obter/criar faturas.
    Nota: Esta função recebe a 'conn' (conexão) como parâmetro
    pois ela é feita para rodar DENTRO de uma transação existente.
    """
    # (Lógica completa de cálculo de fatura, como antes)
    sql_get_card_info = text("SELECT dia_fechamento, dia_vencimento FROM Contas WHERE id = :conta_id AND usuario_id = :uid AND tipo_conta = 'Cartão de Crédito'")
    card_info = conn.execute(sql_get_card_info, {"conta_id": conta_id, "uid": usuario_id}).fetchone()
    
    if not card_info or not card_info.dia_fechamento or not card_info.dia_vencimento: 
        return None 
        
    dia_fechamento = card_info.dia_fechamento; dia_vencimento = card_info.dia_vencimento
    dia_transacao = data_transacao.day; mes_transacao = data_transacao.month; ano_transacao = data_transacao.year
    data_fatura_fechamento = None; data_fatura_vencimento = None
    
    try: 
        data_fechamento_mes_atual = date(ano_transacao, mes_transacao, dia_fechamento)
    except ValueError: 
        _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao)
        data_fechamento_mes_atual = date(ano_transacao, mes_transacao, ultimo_dia_mes)
    
    if data_transacao <= data_fechamento_mes_atual:
        try: 
            data_fatura_vencimento = date(ano_transacao, mes_transacao, dia_vencimento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_transacao, mes_transacao)
            data_fatura_vencimento = date(ano_transacao, mes_transacao, ultimo_dia_mes)
        data_fatura_fechamento = data_fechamento_mes_atual
        if dia_vencimento < dia_fechamento: 
            ano_venc, mes_venc = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1)
            try: 
                data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
            except ValueError: 
                _, ultimo_dia_mes = monthrange(ano_venc, mes_venc)
                data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
    else:
        ano_fech, mes_fech = (ano_transacao, mes_transacao + 1) if mes_transacao < 12 else (ano_transacao + 1, 1)
        ano_venc, mes_venc = (ano_fech, mes_fech + 1) if mes_fech < 12 else (ano_fech + 1, 1)
        try: 
            data_fatura_fechamento = date(ano_fech, mes_fech, dia_fechamento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_fech, mes_fech)
            data_fatura_fechamento = date(ano_fech, mes_fech, ultimo_dia_mes)
        try: 
            data_fatura_vencimento = date(ano_venc, mes_venc, dia_vencimento)
        except ValueError: 
            _, ultimo_dia_mes = monthrange(ano_venc, mes_venc)
            data_fatura_vencimento = date(ano_venc, mes_venc, ultimo_dia_mes)
            
    sql_find_fatura = text("SELECT id FROM Faturas WHERE conta_id = :cid AND data_vencimento = :dv")
    result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento})
    fatura_id = result.scalar_one_or_none()
    
    if fatura_id is None:
        sql_create_fatura = text("INSERT INTO Faturas (conta_id, data_vencimento, data_fechamento, status) VALUES (:cid, :dv, :df, 'Aberta') ON CONFLICT (conta_id, data_vencimento) DO NOTHING RETURNING id")
        result = conn.execute(sql_create_fatura, {"cid": conta_id, "dv": data_fatura_vencimento, "df": data_fatura_fechamento})
        fatura_id = result.scalar_one_or_none()
        if fatura_id is None: 
            result = conn.execute(sql_find_fatura, {"cid": conta_id, "dv": data_fatura_vencimento})
            fatura_id = result.scalar_one_or_none()
        print(f"[SERVICE-FIN] Fatura ID {fatura_id} (Venc: {data_fatura_vencimento}) sendo usada/criada para Cartão ID {conta_id}")
        
    return fatura_id


def ensure_current_invoice_exists(conn, usuario_id, conta_id_cartao=None):
    """
    Garante que exista uma fatura aberta para o período atual de cada cartão.
    Se a fatura do período já passou, cria automaticamente a próxima.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id_cartao: ID do cartão específico (opcional)
    """
    from datetime import date
    from calendar import monthrange

    # Buscar cartões do usuário
    if conta_id_cartao:
        sql_cartoes = text("""
            SELECT id, dia_fechamento, dia_vencimento
            FROM Contas
            WHERE id = :cid AND usuario_id = :uid AND tipo_conta = 'Cartão de Crédito'
        """)
        cartoes = conn.execute(sql_cartoes, {"cid": conta_id_cartao, "uid": usuario_id}).fetchall()
    else:
        sql_cartoes = text("""
            SELECT id, dia_fechamento, dia_vencimento
            FROM Contas
            WHERE usuario_id = :uid AND tipo_conta = 'Cartão de Crédito'
        """)
        cartoes = conn.execute(sql_cartoes, {"uid": usuario_id}).fetchall()

    hoje = date.today()

    for cartao in cartoes:
        conta_id = cartao.id
        dia_fechamento = cartao.dia_fechamento
        dia_vencimento = cartao.dia_vencimento

        if not dia_fechamento or not dia_vencimento:
            continue

        # Calcular qual deveria ser a data da fatura atual
        # Se hoje é depois do fechamento, a fatura atual é do próximo mês
        try:
            data_fechamento_atual = date(hoje.year, hoje.month, dia_fechamento)
        except ValueError:
            _, ultimo_dia = monthrange(hoje.year, hoje.month)
            data_fechamento_atual = date(hoje.year, hoje.month, ultimo_dia)

        if hoje > data_fechamento_atual:
            # Já passou do fechamento, calcular próximo mês
            if hoje.month == 12:
                ano_venc = hoje.year + 1
                mes_venc = 1
            else:
                ano_venc = hoje.year
                mes_venc = hoje.month + 1
        else:
            # Ainda não fechou este mês
            ano_venc = hoje.year
            mes_venc = hoje.month

        # Calcular data de vencimento
        try:
            data_venc_esperada = date(ano_venc, mes_venc, dia_vencimento)
        except ValueError:
            _, ultimo_dia = monthrange(ano_venc, mes_venc)
            data_venc_esperada = date(ano_venc, mes_venc, ultimo_dia)

        # Ajustar se vencimento < fechamento
        if dia_vencimento < dia_fechamento and hoje <= data_fechamento_atual:
            # Vencimento é no mês seguinte ao fechamento
            if mes_venc == 12:
                ano_venc = ano_venc + 1
                mes_venc = 1
            else:
                mes_venc = mes_venc + 1
            try:
                data_venc_esperada = date(ano_venc, mes_venc, dia_vencimento)
            except ValueError:
                _, ultimo_dia = monthrange(ano_venc, mes_venc)
                data_venc_esperada = date(ano_venc, mes_venc, ultimo_dia)

        # Verificar se já existe fatura com essa data de vencimento
        sql_check = text("SELECT id FROM Faturas WHERE conta_id = :cid AND data_vencimento = :dv AND status = 'Aberta'")
        fatura_existe = conn.execute(sql_check, {"cid": conta_id, "dv": data_venc_esperada}).fetchone()

        if not fatura_existe:
            # Criar fatura automaticamente
            # Recalcular data de fechamento para a fatura
            if dia_vencimento < dia_fechamento:
                # Fechamento é no mês anterior ao vencimento
                if mes_venc == 1:
                    ano_fech = ano_venc - 1
                    mes_fech = 12
                else:
                    ano_fech = ano_venc
                    mes_fech = mes_venc - 1
            else:
                # Fechamento é no mesmo mês do vencimento
                ano_fech = ano_venc
                mes_fech = mes_venc

            try:
                data_fech_esperada = date(ano_fech, mes_fech, dia_fechamento)
            except ValueError:
                _, ultimo_dia = monthrange(ano_fech, mes_fech)
                data_fech_esperada = date(ano_fech, mes_fech, ultimo_dia)

            sql_create = text("""
                INSERT INTO Faturas (conta_id, data_vencimento, data_fechamento, status)
                VALUES (:cid, :dv, :df, 'Aberta')
                ON CONFLICT (conta_id, data_vencimento) DO NOTHING
            """)
            conn.execute(sql_create, {
                "cid": conta_id,
                "dv": data_venc_esperada,
                "df": data_fech_esperada
            })
            print(f"[AUTO-FATURA] Fatura criada automaticamente para cartão ID {conta_id}, vencimento {data_venc_esperada}")

def get_fatura_valor(conn, usuario_id, conta_id_cartao=None):
    """
    Consulta o valor atual da(s) fatura(s) em aberto.
    Garante que sempre exista uma fatura para o período atual.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id_cartao: ID do cartão (opcional). Se None, retorna todas as faturas.

    Returns:
        List de dicts com informações das faturas:
        [{
            "nome_cartao": "Nubank",
            "valor_fatura": 1500.50,
            "data_vencimento": date(2025, 12, 15),
            "status": "Aberta"
        }]
    """
    # Garantir que existe fatura para o período atual
    ensure_current_invoice_exists(conn, usuario_id, conta_id_cartao)

    if conta_id_cartao:
        # Consultar fatura específica de um cartão
        sql = text("""
            SELECT
                c.nome_conta,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
                f.data_vencimento,
                f.status
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
                AND c.id = :cid
                AND f.status = 'Aberta'
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC
            LIMIT 1
        """)
        result = conn.execute(sql, {"uid": usuario_id, "cid": conta_id_cartao}).fetchall()
    else:
        # Consultar todas as faturas abertas
        sql = text("""
            SELECT
                c.nome_conta,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
                f.data_vencimento,
                f.status
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
                AND f.status = 'Aberta'
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento ASC
        """)
        result = conn.execute(sql, {"uid": usuario_id}).fetchall()

    faturas = []
    for row in result:
        faturas.append({
            "nome_cartao": row[0],
            "valor_fatura": float(row[1]),
            "data_vencimento": row[2],
            "status": row[3]
        })

    return faturas


def get_fatura_detalhada(conn, usuario_id, conta_id_cartao):
    """
    Consulta os detalhes completos de uma fatura aberta, incluindo todas as transações.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        conta_id_cartao: ID do cartão

    Returns:
        Dict com informações detalhadas da fatura:
        {
            "nome_cartao": "Nubank",
            "valor_total": 1500.50,
            "data_vencimento": date(2026, 01, 20),
            "status": "Aberta",
            "transacoes": [
                {
                    "descricao": "Netflix",
                    "valor": 49.90,
                    "data": date(2026, 01, 05),
                    "tipo_agendamento": "FIXO",  # ou "PARCELADO", ou None
                    "parcela_info": None  # ou "3/12" para parcelados
                },
                ...
            ]
        }
    """
    # Garantir que existe fatura para o período atual
    ensure_current_invoice_exists(conn, usuario_id, conta_id_cartao)

    # Buscar informações gerais da fatura
    sql_fatura = text("""
        SELECT
            c.nome_conta,
            f.id as fatura_id,
            f.data_vencimento,
            f.status,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = :uid
            AND c.id = :cid
            AND f.status = 'Aberta'
        GROUP BY c.nome_conta, f.id, f.data_vencimento, f.status
        ORDER BY f.data_vencimento ASC
        LIMIT 1
    """)

    fatura_info = conn.execute(sql_fatura, {"uid": usuario_id, "cid": conta_id_cartao}).fetchone()

    if not fatura_info:
        return None

    # Buscar todas as transações da fatura com informações de agendamento
    sql_transacoes = text("""
        SELECT
            t.descricao,
            t.valor,
            t.data_transacao,
            t.agendamento_id,
            a.tipo_agendamento,
            a.parcelas_executadas,
            a.total_parcelas
        FROM Transacoes t
        LEFT JOIN Agendamentos a ON t.agendamento_id = a.id
        WHERE t.fatura_id = :fid
            AND t.valor < 0
        ORDER BY t.data_transacao DESC, t.descricao
    """)

    transacoes_rows = conn.execute(sql_transacoes, {"fid": fatura_info.fatura_id}).fetchall()

    transacoes = []
    for row in transacoes_rows:
        parcela_info = None
        tipo_agendamento = row.tipo_agendamento

        # Se é parcelado, formatar info da parcela
        if tipo_agendamento == 'PARCELADO' and row.total_parcelas:
            parcela_info = f"{row.parcelas_executadas}/{row.total_parcelas}"

        transacoes.append({
            "descricao": row.descricao,
            "valor": abs(float(row.valor)),
            "data": row.data_transacao,
            "tipo_agendamento": tipo_agendamento,
            "parcela_info": parcela_info
        })

    return {
        "nome_cartao": fatura_info.nome_conta,
        "valor_total": float(fatura_info.valor_total),
        "data_vencimento": fatura_info.data_vencimento,
        "status": fatura_info.status,
        "transacoes": transacoes
    }


def get_fatura_id_if_credit_card(
    conn,
    conta_id: int,
    conta_tipo: str,
    data_transacao,
    usuario_id: int
) -> Optional[int]:
    """
    Retorna fatura_id se conta for cartão de crédito, None caso contrário.
    Centraliza lógica de ensure_current_invoice_exists + get_or_create_fatura.

    Este helper elimina código duplicado nos webhooks onde o padrão abaixo
    era repetido:
        if conta_tipo == 'Cartão de Crédito':
            ensure_current_invoice_exists(conn, usuario_id, conta_id)
            fatura_id = get_or_create_fatura(conn, conta_id, data, usuario_id)

    Args:
        conn: Conexão do banco
        conta_id: ID da conta
        conta_tipo: Tipo da conta ('Conta Corrente', 'Cartão de Crédito', etc.)
        data_transacao: Data da transação
        usuario_id: ID do usuário

    Returns:
        ID da fatura se for cartão de crédito, None caso contrário
    """
    if conta_tipo != 'Cartão de Crédito':
        return None

    ensure_current_invoice_exists(conn, usuario_id, conta_id)
    return get_or_create_fatura(conn, conta_id, data_transacao, usuario_id)


def close_expired_invoices(conn, dry_run=False):
    """
    Fecha faturas cujo data_fechamento já passou.

    Regra de Negócio: Faturas fecham às 23:59:59 do dia de fechamento.
    Portanto, fechamos todas as faturas cuja data_fechamento < hoje.

    Args:
        conn: Database connection
        dry_run: If True, only returns what would be closed without updating

    Returns:
        List of dicts: [{
            'id': int,
            'conta_id': int,
            'nome_conta': str,
            'data_fechamento': date,
            'data_vencimento': date,
            'valor_total': float,
            'usuario_id': int,
            'numero_whatsapp': str
        }]
    """
    # Query invoices to close
    sql_find = text("""
        SELECT
            f.id,
            f.conta_id,
            f.data_fechamento,
            f.data_vencimento,
            c.nome_conta,
            c.usuario_id,
            u.numero_whatsapp,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        JOIN Usuarios u ON c.usuario_id = u.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE f.status = 'Aberta'
          AND f.data_fechamento < CURRENT_DATE
        GROUP BY f.id, f.conta_id, f.data_fechamento, f.data_vencimento,
                 c.nome_conta, c.usuario_id, u.numero_whatsapp
        ORDER BY f.data_fechamento ASC
    """)

    result = conn.execute(sql_find).fetchall()

    invoices_to_close = []
    for row in result:
        invoices_to_close.append({
            'id': row.id,
            'conta_id': row.conta_id,
            'nome_conta': row.nome_conta,
            'data_fechamento': row.data_fechamento,
            'data_vencimento': row.data_vencimento,
            'valor_total': float(row.valor_total),
            'usuario_id': row.usuario_id,
            'numero_whatsapp': row.numero_whatsapp
        })

    if dry_run:
        return invoices_to_close

    # Actually close the invoices
    if invoices_to_close:
        invoice_ids = [inv['id'] for inv in invoices_to_close]
        sql_update = text("""
            UPDATE Faturas
            SET status = 'Fechada', updated_at = CURRENT_TIMESTAMP
            WHERE id = ANY(:ids)
        """)
        conn.execute(sql_update, {"ids": invoice_ids})
        conn.commit()

    return invoices_to_close


def get_invoices_due_soon(conn, days_before=3):
    """
    Busca faturas fechadas que vencerão em N dias.

    Args:
        conn: Database connection
        days_before: Alertar X dias antes do vencimento

    Returns:
        List of dicts with invoice details
    """
    from datetime import timedelta

    target_date = date.today() + timedelta(days=days_before)

    sql = text("""
        SELECT
            f.id,
            f.conta_id,
            f.data_vencimento,
            c.nome_conta,
            c.usuario_id,
            u.numero_whatsapp,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        JOIN Usuarios u ON c.usuario_id = u.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE f.status = 'Fechada'
          AND f.data_vencimento = :target_date
        GROUP BY f.id, f.conta_id, f.data_vencimento, c.nome_conta, c.usuario_id, u.numero_whatsapp
    """)

    result = conn.execute(sql, {"target_date": target_date}).fetchall()

    invoices = []
    for row in result:
        invoices.append({
            'id': row.id,
            'conta_id': row.conta_id,
            'nome_conta': row.nome_conta,
            'data_vencimento': row.data_vencimento,
            'valor_total': float(row.valor_total),
            'usuario_id': row.usuario_id,
            'numero_whatsapp': row.numero_whatsapp,
            'dias_ate_vencimento': days_before
        })

    return invoices


def get_overdue_invoices(conn):
    """
    Busca faturas vencidas e não pagas.

    Returns:
        List of dicts with overdue invoice details
    """
    sql = text("""
        SELECT
            f.id,
            f.conta_id,
            f.data_vencimento,
            c.nome_conta,
            c.usuario_id,
            u.numero_whatsapp,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_total,
            CURRENT_DATE - f.data_vencimento as dias_atrasado
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        JOIN Usuarios u ON c.usuario_id = u.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE f.status IN ('Aberta', 'Fechada')
          AND f.data_vencimento < CURRENT_DATE
        GROUP BY f.id, f.conta_id, f.data_vencimento, c.nome_conta, c.usuario_id, u.numero_whatsapp
        ORDER BY f.data_vencimento ASC
    """)

    result = conn.execute(sql).fetchall()

    invoices = []
    for row in result:
        invoices.append({
            'id': row.id,
            'conta_id': row.conta_id,
            'nome_conta': row.nome_conta,
            'data_vencimento': row.data_vencimento,
            'valor_total': float(row.valor_total),
            'usuario_id': row.usuario_id,
            'numero_whatsapp': row.numero_whatsapp,
            'dias_atrasado': row.dias_atrasado
        })

    return invoices


__all__ = [
    'get_or_create_fatura',
    'ensure_current_invoice_exists',
    'get_fatura_valor',
    'get_fatura_id_if_credit_card',
    'close_expired_invoices',
    'get_invoices_due_soon',
    'get_overdue_invoices',
]
