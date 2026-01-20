"""
Serviço de geração de relatórios mensais automáticos.
Coleta dados financeiros, gera estatísticas e formata mensagens para WhatsApp.
"""

from datetime import datetime, date
from calendar import monthrange
from zoneinfo import ZoneInfo
from sqlalchemy import text
from app import db_engine
from app.utils import with_db_retry
from app.services.chart_service import generate_pie_chart

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


def calcular_periodo_relatorio(momento_envio: str) -> tuple:
    """
    Calcula o período (mês/ano) do relatório baseado no momento de envio.

    Args:
        momento_envio: 'INICIO_MES' (relata mês anterior) ou 'FIM_MES' (relata mês atual)

    Returns:
        tuple: (mes: int, ano: int, data_inicio: date, data_fim: date)
    """
    hoje = datetime.now(TIMEZONE_BR).date()

    if momento_envio == 'INICIO_MES':
        # Relatório do mês anterior
        if hoje.month == 1:
            mes, ano = 12, hoje.year - 1
        else:
            mes, ano = hoje.month - 1, hoje.year
    else:  # FIM_MES
        # Relatório do mês atual
        mes, ano = hoje.month, hoje.year

    # Primeiro e último dia do mês
    data_inicio = date(ano, mes, 1)
    ultimo_dia = monthrange(ano, mes)[1]
    data_fim = date(ano, mes, ultimo_dia)

    return mes, ano, data_inicio, data_fim


@with_db_retry()
def get_gastos_totais(usuario_id: int, data_inicio: date, data_fim: date) -> dict:
    """
    Calcula gastos totais por tipo de transação no período.

    Returns:
        dict: {
            'total_despesas': Decimal,
            'total_rendas': Decimal,
            'saldo_periodo': Decimal,
            'total_transacoes': int
        }
    """
    query_sql = text("""
        SELECT
            tipo_transacao,
            SUM(valor) AS total,
            COUNT(*) AS quantidade
        FROM Transacoes
        WHERE usuario_id = :usuario_id
          AND data_transacao BETWEEN :data_inicio AND :data_fim
          AND consolidada = TRUE
          AND tipo_transacao IN ('Despesa', 'Renda')
        GROUP BY tipo_transacao
    """)

    with db_engine.connect() as conn:
        results = conn.execute(query_sql, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchall()

        despesas = 0
        rendas = 0
        total_transacoes = 0

        for row in results:
            tipo = row[0]
            valor = float(row[1]) if row[1] else 0
            qtd = row[2]

            if tipo == 'Despesa':
                despesas = valor
            elif tipo == 'Renda':
                rendas = valor

            total_transacoes += qtd

        return {
            'total_despesas': despesas,
            'total_rendas': rendas,
            'saldo_periodo': rendas - despesas,
            'total_transacoes': total_transacoes
        }


@with_db_retry()
def get_top_categorias(usuario_id: int, data_inicio: date, data_fim: date, limit: int = 5) -> list:
    """
    Retorna as top N categorias com maior gasto no período.

    Returns:
        list: [{'categoria': str, 'valor': float, 'percentual': float}, ...]
    """
    query_sql = text("""
        SELECT
            sc.nome_sub AS categoria,
            SUM(t.valor) AS total_gasto
        FROM Transacoes t
        INNER JOIN SubCategoria sc ON t.subcategoria_id = sc.id
        WHERE t.usuario_id = :usuario_id
          AND t.data_transacao BETWEEN :data_inicio AND :data_fim
          AND t.tipo_transacao = 'Despesa'
          AND t.consolidada = TRUE
        GROUP BY sc.nome_sub
        ORDER BY total_gasto DESC
        LIMIT :limit
    """)

    with db_engine.connect() as conn:
        results = conn.execute(query_sql, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim,
            "limit": limit
        }).fetchall()

        # Calcular total para percentuais
        total_geral = sum(float(row[1]) for row in results)

        return [
            {
                'categoria': row[0],
                'valor': float(row[1]),
                'percentual': (float(row[1]) / total_geral * 100) if total_geral != 0 else 0
            }
            for row in results
        ]


@with_db_retry()
def get_comparacao_mes_anterior(usuario_id: int, data_inicio: date, data_fim: date) -> dict:
    """
    Compara gastos do mês atual com o mês anterior.

    Returns:
        dict: {
            'mes_atual': float,
            'mes_anterior': float,
            'variacao_valor': float,
            'variacao_percentual': float
        }
    """
    # Calcular período do mês anterior
    if data_inicio.month == 1:
        mes_ant, ano_ant = 12, data_inicio.year - 1
    else:
        mes_ant, ano_ant = data_inicio.month - 1, data_inicio.year

    data_inicio_ant = date(ano_ant, mes_ant, 1)
    ultimo_dia_ant = monthrange(ano_ant, mes_ant)[1]
    data_fim_ant = date(ano_ant, mes_ant, ultimo_dia_ant)

    # Query para ambos os meses
    query_sql = text("""
        SELECT
            SUM(CASE WHEN data_transacao BETWEEN :data_inicio AND :data_fim
                THEN valor ELSE 0 END) AS mes_atual,
            SUM(CASE WHEN data_transacao BETWEEN :data_inicio_ant AND :data_fim_ant
                THEN valor ELSE 0 END) AS mes_anterior
        FROM Transacoes
        WHERE usuario_id = :usuario_id
          AND tipo_transacao = 'Despesa'
          AND consolidada = TRUE
          AND data_transacao BETWEEN :data_inicio_ant AND :data_fim
    """)

    with db_engine.connect() as conn:
        result = conn.execute(query_sql, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim,
            "data_inicio_ant": data_inicio_ant,
            "data_fim_ant": data_fim_ant
        }).fetchone()

        mes_atual = float(result[0]) if result[0] else 0
        mes_anterior = float(result[1]) if result[1] else 0

        variacao_valor = mes_atual - mes_anterior
        variacao_percentual = (variacao_valor / mes_anterior * 100) if mes_anterior > 0 else 0

        return {
            'mes_atual': mes_atual,
            'mes_anterior': mes_anterior,
            'variacao_valor': variacao_valor,
            'variacao_percentual': variacao_percentual
        }


@with_db_retry()
def get_status_potes(usuario_id: int, data_inicio: date, data_fim: date) -> list:
    """
    Retorna status de utilização dos potes no período.

    Returns:
        list: [{'nome': str, 'limite': float, 'usado': float, 'saldo': float, 'percentual': float}, ...]
    """
    query_sql = text("""
        SELECT
            p.nome_pote,
            p.valor_limite,
            COALESCE(SUM(t.valor), 0) AS total_usado
        FROM PotesDeGastos p
        LEFT JOIN PoteSubCategorias psc ON p.id = psc.pote_id
        LEFT JOIN Transacoes t ON psc.subcategoria_id = t.subcategoria_id
            AND t.data_transacao BETWEEN :data_inicio AND :data_fim
            AND t.tipo_transacao = 'Despesa'
            AND t.consolidada = TRUE
        WHERE p.usuario_id = :usuario_id
          AND p.ativo = TRUE
        GROUP BY p.id, p.nome_pote, p.valor_limite
        ORDER BY p.nome_pote
    """)

    with db_engine.connect() as conn:
        results = conn.execute(query_sql, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchall()

        return [
            {
                'nome': row[0],
                'limite': float(row[1]),
                'usado': float(row[2]),
                'saldo': float(row[1]) - float(row[2]),
                'percentual': (float(row[2]) / float(row[1]) * 100) if float(row[1]) > 0 else 0
            }
            for row in results
        ]


@with_db_retry()
def get_contas_status(usuario_id: int, data_inicio: date, data_fim: date) -> dict:
    """
    Retorna status de contas pagas vs pendentes no período.

    Returns:
        dict: {
            'pagas': int,
            'valor_pago': float,
            'pendentes': int,
            'valor_pendente': float
        }
    """
    # Contas pagas (transações consolidadas)
    query_pagas = text("""
        SELECT
            COUNT(*) AS quantidade,
            SUM(valor) AS total
        FROM Transacoes
        WHERE usuario_id = :usuario_id
          AND data_transacao BETWEEN :data_inicio AND :data_fim
          AND tipo_transacao = 'Despesa'
          AND consolidada = TRUE
    """)

    # Contas pendentes (agendamentos não executados)
    query_pendentes = text("""
        SELECT
            COUNT(*) AS quantidade,
            SUM(valor_previsto) AS total
        FROM Agendamentos
        WHERE usuario_id = :usuario_id
          AND ativo = TRUE
          AND (
              (tipo_agendamento = 'FIXO' AND data_inicio <= :data_fim)
              OR
              (tipo_agendamento = 'PARCELADO'
               AND parcelas_executadas < total_parcelas
               AND data_inicio <= :data_fim)
          )
    """)

    with db_engine.connect() as conn:
        # Contas pagas
        result_pagas = conn.execute(query_pagas, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchone()

        qtd_pagas = result_pagas[0] if result_pagas[0] else 0
        valor_pago = float(result_pagas[1]) if result_pagas[1] else 0

        # Contas pendentes
        result_pendentes = conn.execute(query_pendentes, {
            "usuario_id": usuario_id,
            "data_inicio": data_inicio,
            "data_fim": data_fim
        }).fetchone()

        qtd_pendentes = result_pendentes[0] if result_pendentes[0] else 0
        valor_pendente = float(result_pendentes[1]) if result_pendentes[1] else 0

        return {
            'pagas': qtd_pagas,
            'valor_pago': valor_pago,
            'pendentes': qtd_pendentes,
            'valor_pendente': valor_pendente
        }


def generate_monthly_report_data(usuario_id: int, momento_envio: str) -> dict:
    """
    Gera todos os dados necessários para o relatório mensal.

    Args:
        usuario_id: ID do usuário
        momento_envio: 'INICIO_MES' ou 'FIM_MES'

    Returns:
        dict: Dados completos do relatório com todas as seções
    """
    mes, ano, data_inicio, data_fim = calcular_periodo_relatorio(momento_envio)

    return {
        'mes': mes,
        'ano': ano,
        'periodo': {
            'inicio': data_inicio,
            'fim': data_fim
        },
        'totais': get_gastos_totais(usuario_id, data_inicio, data_fim),
        'top_categorias': get_top_categorias(usuario_id, data_inicio, data_fim, limit=5),
        'comparacao': get_comparacao_mes_anterior(usuario_id, data_inicio, data_fim),
        'potes': get_status_potes(usuario_id, data_inicio, data_fim),
        'contas': get_contas_status(usuario_id, data_inicio, data_fim)
    }


def generate_monthly_report_chart(report_data: dict) -> bytes:
    """
    Gera gráfico de pizza com o Top 5 categorias.

    Args:
        report_data: Dados do relatório (retorno de generate_monthly_report_data)

    Returns:
        bytes: PNG do gráfico em bytes
    """
    top_categorias = report_data['top_categorias']

    if not top_categorias:
        # Retorna gráfico vazio se não houver dados
        return generate_pie_chart({}, 0)

    # Preparar dados para o gráfico
    dados_grafico = {cat['categoria']: abs(cat['valor']) for cat in top_categorias}
    dias = (report_data['periodo']['fim'] - report_data['periodo']['inicio']).days + 1

    return generate_pie_chart(dados_grafico, dias)


def format_report_message(report_data: dict, nome_usuario: str) -> str:
    """
    Formata mensagem do relatório mensal para WhatsApp.

    Args:
        report_data: Dados do relatório
        nome_usuario: Nome do usuário

    Returns:
        str: Mensagem formatada em texto
    """
    meses = [
        '', 'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
        'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro'
    ]

    mes_nome = meses[report_data['mes']]
    ano = report_data['ano']
    totais = report_data['totais']
    comparacao = report_data['comparacao']
    top_cat = report_data['top_categorias']
    potes = report_data['potes']
    contas = report_data['contas']

    # Construir mensagem
    msg = f"📊 *RELATÓRIO MENSAL - {mes_nome.upper()}/{ano}*\n"
    msg += f"Olá, {nome_usuario}!\n\n"

    # Resumo financeiro
    msg += "💰 *RESUMO FINANCEIRO*\n"
    msg += f"• Receitas: R$ {totais['total_rendas']:,.2f}\n"
    msg += f"• Despesas: R$ {totais['total_despesas']:,.2f}\n"
    msg += f"• Saldo: R$ {totais['saldo_periodo']:,.2f}\n"
    msg += f"• Transações: {totais['total_transacoes']}\n\n"

    # Comparação com mês anterior
    simbolo = "📈" if comparacao['variacao_valor'] > 0 else "📉"
    msg += f"{simbolo} *COMPARAÇÃO COM MÊS ANTERIOR*\n"
    msg += f"• Mês anterior: R$ {comparacao['mes_anterior']:,.2f}\n"
    msg += f"• Variação: R$ {comparacao['variacao_valor']:,.2f} "
    msg += f"({comparacao['variacao_percentual']:+.1f}%)\n\n"

    # Top 5 categorias
    if top_cat:
        msg += "🏆 *TOP 5 CATEGORIAS*\n"
        for i, cat in enumerate(top_cat, 1):
            msg += f"{i}. {cat['categoria']}: R$ {cat['valor']:,.2f} ({cat['percentual']:.1f}%)\n"
        msg += "\n"

    # Potes de gastos
    if potes:
        msg += "🎯 *POTES DE GASTOS*\n"
        for pote in potes:
            emoji = "✅" if pote['percentual'] <= 100 else "⚠️"
            msg += f"{emoji} {pote['nome']}:\n"
            msg += f"   Usado: R$ {pote['usado']:,.2f} / R$ {pote['limite']:,.2f}\n"
            msg += f"   Saldo: R$ {pote['saldo']:,.2f} ({pote['percentual']:.1f}%)\n"
        msg += "\n"

    # Contas pagas vs pendentes
    msg += "💳 *STATUS DE CONTAS*\n"
    msg += f"✅ Pagas: {contas['pagas']} (R$ {contas['valor_pago']:,.2f})\n"
    msg += f"⏳ Pendentes: {contas['pendentes']} (R$ {contas['valor_pendente']:,.2f})\n\n"

    # Rodapé
    msg += "_📷 Veja o gráfico de pizza anexo para visualização das categorias!_"

    return msg
