# app/services/forecast_service.py
"""
Serviço de Previsão de Gastos Futuros
Projeta gastos futuros baseado em histórico, contas fixas e padrões de consumo
"""

import json
from sqlalchemy import text
from datetime import date, datetime, timedelta
from calendar import monthrange
from zoneinfo import ZoneInfo

from app import db_engine, gemini_model
from app.utils import formatar_mes_pt, formatar_mes_ano_pt


def get_forecast_data(usuario_id, meses_historico=6, meses_projecao=3):
    """
    Coleta dados históricos e calcula projeções de gastos futuros.

    Args:
        usuario_id (int): ID do usuário
        meses_historico (int): Quantos meses de histórico usar (padrão: 6)
        meses_projecao (int): Quantos meses projetar (padrão: 3)

    Returns:
        dict: Dados estruturados com histórico e projeções
    """
    with db_engine.connect() as conn:
        hoje = date.today()
        data_inicio = date(hoje.year, hoje.month, 1) - timedelta(days=30 * meses_historico)

        # 1. Histórico de gastos mensais por categoria
        sql_historico_categorias = text("""
            SELECT
                TO_CHAR(t.data_transacao, 'YYYY-MM') as mes,
                mc.nome_macro,
                sc.nome_sub as nome_subcategoria,
                SUM(t.valor) as total,
                COUNT(*) as quantidade
            FROM Transacoes t
            JOIN SubCategoria sc ON t.subcategoria_id = sc.id
            JOIN MacroCategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = :uid
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND t.data_transacao >= :data_inicio
            GROUP BY TO_CHAR(t.data_transacao, 'YYYY-MM'), mc.nome_macro, sc.nome_sub
            ORDER BY mes DESC, total DESC
        """)

        historico_categorias = conn.execute(sql_historico_categorias, {
            "uid": usuario_id,
            "data_inicio": data_inicio
        }).fetchall()

        # 2. Total de gastos mensais (resumo)
        sql_gastos_mensais = text("""
            SELECT
                TO_CHAR(data_transacao, 'YYYY-MM') as mes,
                SUM(valor) as total
            FROM Transacoes
            WHERE usuario_id = :uid
                AND tipo_transacao = 'Despesa'
                AND consolidada = true
                AND data_transacao >= :data_inicio
            GROUP BY TO_CHAR(data_transacao, 'YYYY-MM')
            ORDER BY mes DESC
        """)

        gastos_mensais = conn.execute(sql_gastos_mensais, {
            "uid": usuario_id,
            "data_inicio": data_inicio
        }).fetchall()

        # 3. Contas fixas ativas (para incluir na projeção)
        sql_contas_fixas = text("""
            SELECT
                descricao,
                valor_previsto,
                periodicidade,
                dia_execucao,
                tipo_agendamento,
                total_parcelas,
                parcelas_executadas
            FROM Agendamentos
            WHERE usuario_id = :uid
                AND ativo = true
                AND tipo_agendamento IN ('FIXO', 'PARCELADO', 'LEMBRETE_VARIAVEL')
            ORDER BY valor_previsto DESC
        """)

        contas_fixas = conn.execute(sql_contas_fixas, {
            "uid": usuario_id
        }).fetchall()

        # 4. Gastos do mês atual até hoje
        mes_atual = hoje.strftime('%Y-%m')
        sql_gastos_mes_atual = text("""
            SELECT
                SUM(valor) as total_ate_hoje,
                COUNT(*) as quantidade
            FROM Transacoes
            WHERE usuario_id = :uid
                AND tipo_transacao = 'Despesa'
                AND consolidada = true
                AND TO_CHAR(data_transacao, 'YYYY-MM') = :mes
        """)

        gasto_ate_hoje = conn.execute(sql_gastos_mes_atual, {
            "uid": usuario_id,
            "mes": mes_atual
        }).fetchone()

        # 5. Contas fixas que ainda não foram executadas este mês
        # Separando despesas normais de despesas de cartão
        sql_contas_pendentes = text("""
            SELECT
                a.descricao,
                a.valor_previsto,
                a.dia_execucao,
                c.tipo_conta,
                g.nome_grupo
            FROM Agendamentos a
            JOIN Contas c ON a.conta_id = c.id
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
                AND a.ativo = true
                AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
                AND a.periodicidade = 'MENSAL'
                AND NOT EXISTS (
                    SELECT 1 FROM Transacoes t
                    WHERE t.usuario_id = :uid
                        AND TO_CHAR(t.data_transacao, 'YYYY-MM') = :mes
                        AND t.descricao ILIKE '%' || a.descricao || '%'
                        AND t.consolidada = true
                )
            ORDER BY a.dia_execucao
        """)

        contas_pendentes = conn.execute(sql_contas_pendentes, {
            "uid": usuario_id,
            "mes": mes_atual
        }).fetchall()

        # 6. Padrão de gastos por dia do mês (últimos 6 meses)
        sql_padrao_diario = text("""
            SELECT
                EXTRACT(DAY FROM data_transacao) as dia_mes,
                AVG(valor) as media_dia,
                COUNT(*) as transacoes_total
            FROM Transacoes
            WHERE usuario_id = :uid
                AND tipo_transacao = 'Despesa'
                AND consolidada = true
                AND data_transacao >= :data_inicio
            GROUP BY EXTRACT(DAY FROM data_transacao)
            ORDER BY dia_mes
        """)

        padrao_diario = conn.execute(sql_padrao_diario, {
            "uid": usuario_id,
            "data_inicio": data_inicio
        }).fetchall()

        # Estruturar dados
        return {
            "usuario_id": usuario_id,
            "data_atual": hoje.strftime('%Y-%m-%d'),
            "dia_atual": hoje.day,
            "dias_no_mes": monthrange(hoje.year, hoje.month)[1],
            "mes_atual": mes_atual,
            "meses_historico": meses_historico,
            "meses_projecao": meses_projecao,
            "historico_categorias": [
                {
                    "mes": row.mes,
                    "categoria": row.nome_macro,
                    "subcategoria": row.nome_subcategoria,
                    "total": float(row.total),
                    "quantidade": row.quantidade
                }
                for row in historico_categorias
            ],
            "gastos_mensais": [
                {
                    "mes": row.mes,
                    "total": float(row.total)
                }
                for row in gastos_mensais
            ],
            "contas_fixas": [
                {
                    "descricao": row.descricao,
                    "valor": float(row.valor_previsto),
                    "periodicidade": row.periodicidade,
                    "dia": row.dia_execucao,
                    "tipo": row.tipo_agendamento,
                    "parcelas_totais": row.total_parcelas if row.tipo_agendamento == 'PARCELADO' else None,
                    "parcelas_executadas": row.parcelas_executadas if row.tipo_agendamento == 'PARCELADO' else None
                }
                for row in contas_fixas
            ],
            "gasto_ate_hoje": {
                "total": float(gasto_ate_hoje.total_ate_hoje) if gasto_ate_hoje.total_ate_hoje else 0,
                "quantidade": gasto_ate_hoje.quantidade if gasto_ate_hoje.quantidade else 0
            },
            "contas_pendentes": [
                {
                    "descricao": row.descricao,
                    "valor": float(row.valor_previsto),
                    "dia": row.dia_execucao,
                    "tipo_conta": row.tipo_conta,
                    "nome_grupo": row.nome_grupo
                }
                for row in contas_pendentes
            ],
            "padrao_diario": [
                {
                    "dia": int(row.dia_mes),
                    "media": float(row.media_dia),
                    "transacoes": row.transacoes_total
                }
                for row in padrao_diario
            ]
        }


def calculate_simple_forecast(dados):
    """
    Calcula uma projeção simples de gastos baseado em média móvel.

    Args:
        dados (dict): Dados retornados por get_forecast_data()

    Returns:
        dict: Projeção calculada
    """
    # Calcular média móvel dos últimos meses
    gastos = dados["gastos_mensais"]
    if not gastos:
        return {
            "projecao_mes_atual": 0,
            "media_historica": 0,
            "contas_pendentes_total": 0
        }

    media_historica = sum(g["total"] for g in gastos) / len(gastos)

    # Gastos até hoje no mês atual
    gasto_ate_hoje = dados["gasto_ate_hoje"]["total"]
    dia_atual = dados["dia_atual"]
    dias_no_mes = dados["dias_no_mes"]

    # Projeção linear simples: (gasto até hoje / dias passados) * dias totais
    if dia_atual > 0:
        taxa_diaria = gasto_ate_hoje / dia_atual
        projecao_linear = taxa_diaria * dias_no_mes
    else:
        projecao_linear = media_historica

    # Separar contas pendentes por tipo
    despesas_normais_pendentes = sum(
        c["valor"] for c in dados["contas_pendentes"]
        if c["nome_grupo"] != 'Renda' and c["tipo_conta"] != 'Cartão de Crédito'
    )

    despesas_cartao_pendentes = sum(
        c["valor"] for c in dados["contas_pendentes"]
        if c["tipo_conta"] == 'Cartão de Crédito'
    )

    receitas_pendentes = sum(
        c["valor"] for c in dados["contas_pendentes"]
        if c["nome_grupo"] == 'Renda'
    )

    contas_pendentes_total = despesas_normais_pendentes + despesas_cartao_pendentes

    # Projeção final: maior valor entre projeção linear e (gasto até hoje + pendentes)
    projecao_final = max(
        projecao_linear,
        gasto_ate_hoje + contas_pendentes_total,
        media_historica * 0.85  # pelo menos 85% da média histórica
    )

    return {
        "projecao_mes_atual": round(projecao_final, 2),
        "media_historica": round(media_historica, 2),
        "contas_pendentes_total": round(contas_pendentes_total, 2),
        "despesas_normais_pendentes": round(despesas_normais_pendentes, 2),
        "despesas_cartao_pendentes": round(despesas_cartao_pendentes, 2),
        "receitas_pendentes": round(receitas_pendentes, 2),
        "gasto_ate_hoje": round(gasto_ate_hoje, 2),
        "taxa_diaria_atual": round(gasto_ate_hoje / dia_atual, 2) if dia_atual > 0 else 0
    }


def generate_forecast_insights(usuario_id):
    """
    Gera insights de previsão de gastos usando Gemini.

    Args:
        usuario_id (int): ID do usuário

    Returns:
        str: Relatório formatado com projeções e insights
    """
    if not gemini_model:
        raise Exception("Modelo Gemini não configurado.")

    # Obter dados
    dados = get_forecast_data(usuario_id)
    projecao = calculate_simple_forecast(dados)

    # Preparar prompt para o Gemini
    hoje = date.today()
    mes_nome = formatar_mes_pt(hoje)

    # Formatar histórico mensal
    historico_formatado = "\n".join([
        f"- {g['mes']}: R$ {g['total']:,.2f}"
        for g in dados["gastos_mensais"][:6]
    ])

    # Formatar contas pendentes separadas por tipo
    despesas_normais_pendentes = [c for c in dados["contas_pendentes"]
                                   if c["nome_grupo"] != 'Renda' and c["tipo_conta"] != 'Cartão de Crédito']
    despesas_cartao_pendentes = [c for c in dados["contas_pendentes"]
                                  if c["tipo_conta"] == 'Cartão de Crédito']
    receitas_pendentes = [c for c in dados["contas_pendentes"]
                          if c["nome_grupo"] == 'Renda']

    pendentes_formatado = ""
    if despesas_normais_pendentes:
        pendentes_formatado += "💸 Despesas:\n"
        pendentes_formatado += "\n".join([
            f"- {c['descricao']}: R$ {c['valor']:,.2f} (dia {c['dia']})"
            for c in despesas_normais_pendentes
        ]) + "\n"

    if despesas_cartao_pendentes:
        pendentes_formatado += "💳 Cartão (na fatura):\n"
        pendentes_formatado += "\n".join([
            f"- {c['descricao']}: R$ {c['valor']:,.2f} (dia {c['dia']})"
            for c in despesas_cartao_pendentes
        ]) + "\n"

    if receitas_pendentes:
        pendentes_formatado += "💰 Receitas:\n"
        pendentes_formatado += "\n".join([
            f"- {c['descricao']}: R$ {c['valor']:,.2f} (dia {c['dia']})"
            for c in receitas_pendentes
        ])

    if not pendentes_formatado:
        pendentes_formatado = "- Nenhuma conta pendente"

    # Formatar contas fixas principais
    fixas_formatado = "\n".join([
        f"- {c['descricao']}: R$ {c['valor']:,.2f} ({c['periodicidade']})"
        for c in dados["contas_fixas"][:5]
    ]) if dados["contas_fixas"] else "- Nenhuma conta fixa cadastrada"

    prompt = f"""
Você é um assistente financeiro. Analise os dados e gere uma PROJEÇÃO DE GASTOS FUTUROS.

**DADOS DO USUÁRIO:**

📅 **Contexto Temporal:**
- Data atual: {dados['data_atual']} (dia {dados['dia_atual']} de {dados['dias_no_mes']})
- Mês atual: {mes_nome}

💰 **Situação Atual:**
- Gastos até hoje: R$ {projecao['gasto_ate_hoje']:,.2f}
- Taxa média diária: R$ {projecao['taxa_diaria_atual']:,.2f}
- Despesas pendentes: R$ {projecao['despesas_normais_pendentes']:,.2f}
- Cartão pendente: R$ {projecao['despesas_cartao_pendentes']:,.2f}
- Receitas pendentes: R$ {projecao['receitas_pendentes']:,.2f}

📊 **Histórico (últimos {dados['meses_historico']} meses):**
{historico_formatado}
- Média mensal: R$ {projecao['media_historica']:,.2f}

📋 **Contas Pendentes Este Mês:**
{pendentes_formatado}

🔧 **Contas Fixas Mensais:**
{fixas_formatado}

📈 **Projeção Calculada:**
- Projeção final {mes_nome}: R$ {projecao['projecao_mes_atual']:,.2f}

---

**INSTRUÇÕES:**

Gere um relatório de PROJEÇÃO DE GASTOS com as seguintes seções:

1. 📈 Projeção {mes_nome} (3-4 linhas):
   - Gastos até agora (dia {dados['dia_atual']})
   - Projeção final do mês (separar despesas normais e cartão se relevante)
   - Liste contas pendentes principais (IMPORTANTE: diferenciar despesas normais de cartão)
   - Se houver receitas pendentes, calcular o saldo líquido previsto
   - Base de cálculo (média + padrão)

   NOTA IMPORTANTE: Despesas de cartão já estão debitadas na fatura, são lembretes informativos.

2. 🔍 Análise de Tendências (2-3 pontos):
   - Compare com média histórica
   - Identifique se está acima/abaixo do esperado
   - Destaque mudanças de padrão

3. ⚠️ Alertas (se aplicável):
   - Se projeção está muito acima da média
   - Contas grandes pendentes
   - Padrão de gastos preocupante

4. 💡 Recomendações (2-3 sugestões):
   - Baseado na projeção
   - Categorias para controlar
   - Meta de gastos até fim do mês

**FORMATO:**
- Use emojis e formatação clara
- Seja objetivo (máximo 12 linhas)
- Use valores reais dos dados
- Foque em insights acionáveis
- NÃO use asteriscos ** para negrito nos títulos das seções, apenas os emojis e texto simples
"""

    try:
        response = gemini_model.generate_content(prompt)
        insights = response.text.strip()

        # Adicionar rodapé
        insights += "\n\n💬 _Baseado em: histórico de "
        insights += f"{dados['meses_historico']} meses + contas fixas_"

        return insights

    except Exception as e:
        print(f"[FORECAST-ERRO] Falha ao gerar previsão: {e}")
        raise Exception(f"Não consegui gerar a previsão. Erro: {str(e)}")


def generate_simple_forecast_text(usuario_id):
    """
    Gera uma previsão simples sem usar IA (fallback).

    Args:
        usuario_id (int): ID do usuário

    Returns:
        str: Texto formatado com projeção básica
    """
    dados = get_forecast_data(usuario_id)
    projecao = calculate_simple_forecast(dados)

    hoje = date.today()
    mes_nome = formatar_mes_pt(hoje)

    texto = f"📈 **Projeção {mes_nome}**\n\n"
    texto += f"💰 Gastos até agora: R$ {projecao['gasto_ate_hoje']:,.2f} (dia {dados['dia_atual']})\n"
    texto += f"📊 Projeção final: ~R$ {projecao['projecao_mes_atual']:,.2f}\n\n"

    # Separar contas pendentes por tipo
    despesas_normais = [c for c in dados["contas_pendentes"]
                        if c["nome_grupo"] != 'Renda' and c["tipo_conta"] != 'Cartão de Crédito']
    despesas_cartao = [c for c in dados["contas_pendentes"]
                       if c["tipo_conta"] == 'Cartão de Crédito']
    receitas = [c for c in dados["contas_pendentes"]
                if c["nome_grupo"] == 'Renda']

    if despesas_normais:
        texto += "📋 **Despesas pendentes:**\n"
        for conta in despesas_normais[:3]:
            texto += f"• {conta['descricao']}: R$ {conta['valor']:,.2f}\n"
        texto += "\n"

    if despesas_cartao:
        texto += "💳 **Cartão (na fatura):**\n"
        for conta in despesas_cartao[:3]:
            texto += f"• {conta['descricao']}: R$ {conta['valor']:,.2f}\n"
        texto += "\n"

    if receitas:
        texto += "💰 **Receitas pendentes:**\n"
        for conta in receitas[:3]:
            texto += f"• {conta['descricao']}: R$ {conta['valor']:,.2f}\n"
        texto += "\n"

    texto += f"📈 Baseado em: média últimos {dados['meses_historico']} meses "
    texto += f"(R$ {projecao['media_historica']:,.2f})"

    return texto


def get_category_forecast(usuario_id, categoria_nome, meses=3):
    """
    Prevê gastos futuros de uma categoria específica.

    Args:
        usuario_id (int): ID do usuário
        categoria_nome (str): Nome da categoria
        meses (int): Quantos meses projetar

    Returns:
        str: Relatório formatado
    """
    dados = get_forecast_data(usuario_id, meses_historico=6)

    # Filtrar histórico da categoria
    historico_cat = [
        h for h in dados["historico_categorias"]
        if categoria_nome.lower() in h["subcategoria"].lower()
        or categoria_nome.lower() in h["categoria"].lower()
    ]

    if not historico_cat:
        return f"❌ Não encontrei histórico de '{categoria_nome}' para fazer projeção."

    # Calcular média mensal
    meses_unicos = set(h["mes"] for h in historico_cat)
    total_historico = sum(h["total"] for h in historico_cat)
    media_mensal = total_historico / len(meses_unicos) if meses_unicos else 0

    # Projeção para próximos meses
    projecao_futura = media_mensal * meses

    nome_categoria = historico_cat[0]["subcategoria"]

    texto = f"📈 **Projeção: {nome_categoria}**\n\n"
    texto += f"💰 Média mensal: R$ {media_mensal:,.2f}\n"
    texto += f"📊 Projeção {meses} meses: R$ {projecao_futura:,.2f}\n\n"
    texto += "**Histórico recente:**\n"

    for h in sorted(historico_cat, key=lambda x: x["mes"], reverse=True)[:3]:
        mes_formatado = datetime.strptime(h["mes"], '%Y-%m').strftime('%b/%Y')
        texto += f"• {mes_formatado}: R$ {h['total']:,.2f}\n"

    return texto
