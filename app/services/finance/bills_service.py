# app/services/finance/bills_service.py
"""
Serviço de gerenciamento de vencimentos e contas a pagar.

Este módulo contém funções para consultar e formatar informações sobre:
- Contas fixas a vencer
- Faturas de cartão a vencer
- Agendamentos do período
"""

from datetime import date, timedelta
from typing import List, Dict, Any, Optional
from ._database import text, Connection


def get_upcoming_bills_and_invoices(conn, usuario_id, target_date=None):
    """
    Busca contas fixas e faturas que vão vencer hoje ou amanhã.

    Args:
        conn: Conexão com o banco
        usuario_id: ID do usuário
        target_date: Data de referência (padrão: hoje)

    Returns:
        dict: {
            'contas_hoje': [...],
            'contas_amanha': [...],
            'faturas_hoje': [...],
            'faturas_amanha': [...]
        }
    """
    from datetime import timedelta

    if target_date is None:
        target_date = date.today()

    amanha = target_date + timedelta(days=1)

    # Buscar contas fixas pendentes
    # IMPORTANTE: Executar queries separadas para HOJE e AMANHÃ para corrigir bug de virada de mês
    # (Ex: 31/12 busca dezembro, 01/01 busca janeiro)
    sql_contas = text("""
        SELECT
            a.id,
            a.descricao,
            a.valor_previsto,
            a.dia_execucao,
            a.periodicidade,
            a.data_inicio,
            s.nome_sub as categoria,
            c.nome_conta,
            g.nome_grupo
        FROM Agendamentos a
        JOIN SubCategoria s ON a.subcategoria_id = s.id
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        JOIN Contas c ON a.conta_id = c.id
        WHERE a.usuario_id = :uid
          AND a.ativo = TRUE
          AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
          AND a.dia_execucao = :dia
          -- Filtrar por periodicidade: se ANUAL, verificar se estamos no mês correto
          AND (
              a.periodicidade != 'ANUAL'
              OR EXTRACT(MONTH FROM a.data_inicio) = :mes_ref
          )
          -- Verificar se ainda não foi executado este mês/ano
          AND NOT EXISTS (
              SELECT 1 FROM Transacoes t
              WHERE t.descricao = a.descricao
                AND t.usuario_id = a.usuario_id
                AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
          )
        ORDER BY a.dia_execucao ASC
    """)

    # Query 1: Contas que vencem HOJE
    contas_hoje_result = conn.execute(sql_contas, {
        "uid": usuario_id,
        "dia": target_date.day,
        "mes_ref": target_date.month,
        "ano_ref": target_date.year
    }).fetchall()

    # Query 2: Contas que vencem AMANHÃ (usa mês/ano de amanhã - corrige virada de mês)
    contas_amanha_result = conn.execute(sql_contas, {
        "uid": usuario_id,
        "dia": amanha.day,
        "mes_ref": amanha.month,
        "ano_ref": amanha.year
    }).fetchall()

    # Processar resultados
    contas_hoje = []
    contas_amanha = []

    for conta in contas_hoje_result:
        # Determinar se é receita ou despesa baseado no grupo
        tipo = "Receita" if conta.nome_grupo == "Renda" else "Despesa"
        contas_hoje.append({
            "id": conta.id,
            "descricao": conta.descricao,
            "valor": float(conta.valor_previsto or 0),
            "categoria": conta.categoria,
            "conta": conta.nome_conta,
            "tipo": tipo
        })

    for conta in contas_amanha_result:
        # Determinar se é receita ou despesa baseado no grupo
        tipo = "Receita" if conta.nome_grupo == "Renda" else "Despesa"
        contas_amanha.append({
            "id": conta.id,
            "descricao": conta.descricao,
            "valor": float(conta.valor_previsto or 0),
            "categoria": conta.categoria,
            "conta": conta.nome_conta,
            "tipo": tipo
        })

    # Buscar faturas de cartão de crédito
    sql_faturas = text("""
        SELECT
            c.nome_conta,
            f.data_vencimento,
            COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura,
            f.status
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = :uid
            AND f.status = 'Aberta'
            AND (f.data_vencimento = :hoje OR f.data_vencimento = :amanha)
        GROUP BY c.nome_conta, f.data_vencimento, f.status
        ORDER BY f.data_vencimento ASC
    """)

    faturas_result = conn.execute(sql_faturas, {
        "uid": usuario_id,
        "hoje": target_date,
        "amanha": amanha
    }).fetchall()

    # Separar faturas por dia
    faturas_hoje = []
    faturas_amanha = []

    for fatura in faturas_result:
        fatura_dict = {
            "cartao": fatura.nome_conta,
            "valor": float(fatura.valor_fatura),
            "vencimento": fatura.data_vencimento
        }

        if fatura.data_vencimento == target_date:
            faturas_hoje.append(fatura_dict)
        elif fatura.data_vencimento == amanha:
            faturas_amanha.append(fatura_dict)

    return {
        'contas_hoje': contas_hoje,
        'contas_amanha': contas_amanha,
        'faturas_hoje': faturas_hoje,
        'faturas_amanha': faturas_amanha
    }

def get_vencimentos_periodo(conn, usuario_id, data_inicio, data_fim):
    """
    Busca contas fixas e faturas que vencem em um período específico.

    Args:
        conn: Conexão do banco
        usuario_id: ID do usuário
        data_inicio: Data inicial (date)
        data_fim: Data final (date)

    Returns:
        dict com contas_fixas, faturas e totais
    """
    from calendar import monthrange

    # 1. BUSCAR CONTAS FIXAS PENDENTES
    # Precisa considerar virada de mês
    dia_inicio = data_inicio.day
    dia_fim = data_fim.day
    mes_ref = data_inicio.month
    ano_ref = data_inicio.year

    # Se período cruza virada de mês, buscar em dois meses
    if data_inicio.month != data_fim.month:
        # Buscar do início até fim do primeiro mês
        sql_contas_mes1 = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao >= :dia_inicio
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_mes1 = conn.execute(sql_contas_mes1, {
            "uid": usuario_id,
            "dia_inicio": dia_inicio,
            "mes_ref": data_inicio.month,
            "ano_ref": data_inicio.year
        }).fetchall()

        # Buscar do início do mês seguinte até dia_fim
        sql_contas_mes2 = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao <= :dia_fim
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_mes2 = conn.execute(sql_contas_mes2, {
            "uid": usuario_id,
            "dia_fim": dia_fim,
            "mes_ref": data_fim.month,
            "ano_ref": data_fim.year
        }).fetchall()

        contas_fixas = list(contas_mes1) + list(contas_mes2)
    else:
        # Mesmo mês, busca simples
        sql_contas = text("""
            SELECT a.descricao, a.valor_previsto, a.dia_execucao,
                   a.periodicidade, a.mes_execucao,
                   s.nome_sub as categoria, c.nome_conta, g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
              AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
              AND a.dia_execucao BETWEEN :dia_inicio AND :dia_fim
              -- Filtro para agendamentos anuais: incluir apenas se o mês bater
              AND (
                  a.periodicidade != 'ANUAL'
                  OR (a.periodicidade = 'ANUAL' AND a.mes_execucao = :mes_ref)
              )
              AND NOT EXISTS (
                  SELECT 1 FROM Transacoes t
                  WHERE t.descricao = a.descricao
                  AND EXTRACT(MONTH FROM t.data_transacao) = :mes_ref
                  AND EXTRACT(YEAR FROM t.data_transacao) = :ano_ref
              )
            ORDER BY a.dia_execucao ASC
        """)

        contas_fixas = conn.execute(sql_contas, {
            "uid": usuario_id,
            "dia_inicio": dia_inicio,
            "dia_fim": dia_fim,
            "mes_ref": mes_ref,
            "ano_ref": ano_ref
        }).fetchall()

    # 2. BUSCAR FATURAS ABERTAS
    sql_faturas = text("""
        SELECT c.nome_conta, f.data_vencimento, f.status,
               COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura
        FROM Faturas f
        JOIN Contas c ON f.conta_id = c.id
        LEFT JOIN Transacoes t ON f.id = t.fatura_id
        WHERE c.usuario_id = :uid
          AND f.status = 'Aberta'
          AND f.data_vencimento BETWEEN :data_inicio AND :data_fim
        GROUP BY c.nome_conta, f.data_vencimento, f.status
        ORDER BY f.data_vencimento ASC
    """)

    faturas = conn.execute(sql_faturas, {
        "uid": usuario_id,
        "data_inicio": data_inicio,
        "data_fim": data_fim
    }).fetchall()

    # 3. CALCULAR TOTAIS
    total_contas = sum(row.valor_previsto or 0 for row in contas_fixas)
    total_faturas = sum(row.valor_fatura or 0 for row in faturas)

    return {
        "contas_fixas": contas_fixas,
        "faturas": faturas,
        "total_contas": total_contas,
        "total_faturas": total_faturas,
        "valor_total": total_contas + total_faturas
    }


def format_vencimentos_message(vencimentos, periodo, data_referencia):
    """
    Formata mensagem de vencimentos para WhatsApp.
    Separa receitas de despesas com subtotais.

    Args:
        vencimentos: Dict retornado por get_vencimentos_periodo()
        periodo: String descritiva (ex: "HOJE", "AMANHÃ", "NOS PRÓXIMOS 7 DIAS")
        data_referencia: Data de referência para exibição

    Returns:
        String formatada para WhatsApp
    """
    from app.utils import formatar_moeda

    contas_fixas = vencimentos["contas_fixas"]
    faturas = vencimentos["faturas"]

    # Se não houver vencimentos
    if not contas_fixas and not faturas:
        return f"✅ Nenhuma conta vence {periodo.lower()}!"

    # Separar contas fixas em receitas e despesas
    receitas = [c for c in contas_fixas if c.nome_grupo == 'Renda']
    despesas = [c for c in contas_fixas if c.nome_grupo != 'Renda']

    # Montar mensagem
    msg = f"📋 *CONTAS QUE VENCEM {periodo}* ({data_referencia.strftime('%d/%m')})\n\n"

    # Receitas Previstas
    if receitas:
        msg += "*💵 Receitas Previstas:*\n"
        for conta in receitas:
            descricao = conta.descricao
            valor = formatar_moeda(conta.valor_previsto or 0)
            dia = conta.dia_execucao
            msg += f"• {descricao} - {valor} (dia {dia})\n"
        msg += "\n"

    # Despesas Fixas
    if despesas:
        msg += "*💰 Despesas Fixas:*\n"
        for conta in despesas:
            descricao = conta.descricao
            valor = formatar_moeda(conta.valor_previsto or 0)
            dia = conta.dia_execucao
            msg += f"• {descricao} - {valor} (dia {dia})\n"
        msg += "\n"

    # Faturas
    if faturas:
        msg += "*💳 Faturas:*\n"
        for fatura in faturas:
            cartao = fatura.nome_conta
            valor = formatar_moeda(fatura.valor_fatura or 0)
            data_venc = fatura.data_vencimento.strftime('%d/%m')
            msg += f"• {cartao} - {valor} (vence {data_venc})\n"
        msg += "\n"

    # Calcular subtotais
    total_receitas = sum(c.valor_previsto or 0 for c in receitas)
    total_despesas = sum(c.valor_previsto or 0 for c in despesas)
    total_faturas = sum(f.valor_fatura or 0 for f in faturas)
    saldo_previsto = total_receitas - total_despesas - total_faturas

    # Totais com separação
    if receitas or despesas or faturas:
        if receitas:
            msg += f"*Receitas:* {formatar_moeda(total_receitas)}\n"
        if despesas:
            msg += f"*Despesas:* {formatar_moeda(total_despesas)}\n"
        if faturas:
            msg += f"*Faturas:* {formatar_moeda(total_faturas)}\n"

        # Mostrar saldo previsto apenas se houver receitas OU despesas+faturas
        if receitas or despesas or faturas:
            msg += f"*Saldo Previsto:* {formatar_moeda(saldo_previsto)}"

    return msg


__all__ = [
    'get_upcoming_bills_and_invoices',
    'get_vencimentos_periodo',
    'format_vencimentos_message',
]
