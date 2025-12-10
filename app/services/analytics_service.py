# app/services/analytics_service.py
"""
Serviço de Análise de Gastos com IA
Gera insights personalizados sobre padrões de consumo usando Gemini
"""

import json
from sqlalchemy import text
from datetime import date, datetime, timedelta
from calendar import monthrange
from zoneinfo import ZoneInfo

from app import db_engine, gemini_model
from app.utils import formatar_mes_pt, formatar_mes_ano_pt


def get_spending_analysis(usuario_id, meses_analise=3):
    """
    Analisa o histórico de gastos dos últimos N meses e retorna dados estruturados
    para processamento com IA.

    Args:
        usuario_id (int): ID do usuário
        meses_analise (int): Quantos meses analisar (padrão: 3)

    Returns:
        dict: Dados estruturados sobre gastos do usuário
    """
    with db_engine.connect() as conn:
        # Data de início da análise
        hoje = date.today()
        data_inicio = date(hoje.year, hoje.month, 1) - timedelta(days=30 * meses_analise)

        # 1. Total de gastos por mês
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

        # 2. Gastos por categoria no mês atual
        mes_atual = hoje.strftime('%Y-%m')
        sql_gastos_categoria = text("""
            SELECT
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
                AND TO_CHAR(t.data_transacao, 'YYYY-MM') = :mes
            GROUP BY mc.nome_macro, sc.nome_sub
            ORDER BY total DESC
            LIMIT 10
        """)

        gastos_categoria = conn.execute(sql_gastos_categoria, {
            "uid": usuario_id,
            "mes": mes_atual
        }).fetchall()

        # 3. Gastos por dia da semana (últimos 90 dias)
        sql_gastos_dia_semana = text("""
            SELECT
                TRIM(TO_CHAR(data_transacao, 'Day')) as dia_semana,
                SUM(valor) as total,
                COUNT(*) as quantidade
            FROM Transacoes
            WHERE usuario_id = :uid
                AND tipo_transacao = 'Despesa'
                AND consolidada = true
                AND data_transacao >= CURRENT_DATE - INTERVAL '90 days'
            GROUP BY dia_semana, EXTRACT(DOW FROM data_transacao)
            ORDER BY EXTRACT(DOW FROM data_transacao)
        """)

        gastos_dia_semana = conn.execute(sql_gastos_dia_semana, {
            "uid": usuario_id
        }).fetchall()

        # 4. Comparação mês atual vs mês anterior
        mes_anterior_date = date(hoje.year, hoje.month, 1) - timedelta(days=1)
        mes_anterior = mes_anterior_date.strftime('%Y-%m')

        sql_comparacao = text("""
            SELECT
                TO_CHAR(data_transacao, 'YYYY-MM') as mes,
                SUM(valor) as total
            FROM Transacoes
            WHERE usuario_id = :uid
                AND tipo_transacao = 'Despesa'
                AND consolidada = true
                AND TO_CHAR(data_transacao, 'YYYY-MM') IN (:mes_atual, :mes_anterior)
            GROUP BY TO_CHAR(data_transacao, 'YYYY-MM')
        """)

        comparacao = conn.execute(sql_comparacao, {
            "uid": usuario_id,
            "mes_atual": mes_atual,
            "mes_anterior": mes_anterior
        }).fetchall()

        # 5. Potes de gastos e utilização
        sql_potes = text("""
            SELECT
                p.nome_pote,
                p.valor_limite,
                p.periodicidade,
                COALESCE(SUM(t.valor), 0) as gasto_atual
            FROM PotesDeGastos p
            LEFT JOIN PoteSubCategorias psc ON p.id = psc.pote_id
            LEFT JOIN Transacoes t ON psc.subcategoria_id = t.subcategoria_id
                AND t.usuario_id = :uid
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND TO_CHAR(t.data_transacao, 'YYYY-MM') = :mes
            WHERE p.usuario_id = :uid AND p.ativo = true
            GROUP BY p.nome_pote, p.valor_limite, p.periodicidade
        """)

        potes = conn.execute(sql_potes, {
            "uid": usuario_id,
            "mes": mes_atual
        }).fetchall()

        # 6. Maiores gastos individuais do mês
        sql_maiores_gastos = text("""
            SELECT
                descricao,
                valor,
                data_transacao,
                sc.nome_sub as nome_subcategoria
            FROM Transacoes t
            JOIN SubCategoria sc ON t.subcategoria_id = sc.id
            WHERE t.usuario_id = :uid
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND TO_CHAR(t.data_transacao, 'YYYY-MM') = :mes
            ORDER BY valor DESC
            LIMIT 5
        """)

        maiores_gastos = conn.execute(sql_maiores_gastos, {
            "uid": usuario_id,
            "mes": mes_atual
        }).fetchall()

        # 7. Comparação de categoria específica (ex: delivery) entre meses
        sql_categoria_especifica = text("""
            SELECT
                TO_CHAR(t.data_transacao, 'YYYY-MM') as mes,
                sc.nome_sub as nome_subcategoria,
                SUM(t.valor) as total
            FROM Transacoes t
            JOIN SubCategoria sc ON t.subcategoria_id = sc.id
            WHERE t.usuario_id = :uid
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND sc.nome_sub ILIKE '%delivery%'
                AND t.data_transacao >= :data_inicio
            GROUP BY TO_CHAR(t.data_transacao, 'YYYY-MM'), sc.nome_sub
            ORDER BY mes DESC
        """)

        gastos_delivery = conn.execute(sql_categoria_especifica, {
            "uid": usuario_id,
            "data_inicio": data_inicio
        }).fetchall()

        # 8. Contas fixas do usuário
        sql_contas_fixas = text("""
            SELECT
                descricao,
                valor_previsto,
                periodicidade,
                dia_execucao
            FROM Agendamentos
            WHERE usuario_id = :uid
                AND ativo = true
                AND tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
            ORDER BY valor_previsto DESC
        """)

        contas_fixas = conn.execute(sql_contas_fixas, {
            "uid": usuario_id
        }).fetchall()

        # Estruturar dados para análise
        return {
            "periodo_analise": f"Últimos {meses_analise} meses",
            "mes_atual": mes_atual,
            "gastos_mensais": [
                {"mes": row.mes, "total": float(row.total)}
                for row in gastos_mensais
            ],
            "gastos_por_categoria": [
                {
                    "categoria": row.nome_macro,
                    "subcategoria": row.nome_subcategoria,
                    "total": float(row.total),
                    "quantidade": row.quantidade
                }
                for row in gastos_categoria
            ],
            "gastos_por_dia_semana": [
                {
                    "dia": row.dia_semana.strip(),
                    "total": float(row.total),
                    "quantidade": row.quantidade
                }
                for row in gastos_dia_semana
            ],
            "comparacao_mensal": {
                row.mes: float(row.total)
                for row in comparacao
            },
            "potes": [
                {
                    "nome": row.nome_pote,
                    "limite": float(row.valor_limite),
                    "gasto_atual": float(row.gasto_atual),
                    "percentual_uso": round((float(row.gasto_atual) / float(row.valor_limite) * 100), 1) if row.valor_limite > 0 else 0,
                    "periodicidade": row.periodicidade
                }
                for row in potes
            ],
            "maiores_gastos": [
                {
                    "descricao": row.descricao,
                    "valor": float(row.valor),
                    "data": row.data_transacao.strftime('%d/%m/%Y'),
                    "categoria": row.nome_subcategoria
                }
                for row in maiores_gastos
            ],
            "gastos_delivery": [
                {
                    "mes": row.mes,
                    "total": float(row.total)
                }
                for row in gastos_delivery
            ],
            "contas_fixas": [
                {
                    "descricao": row.descricao,
                    "valor": float(row.valor_previsto),
                    "periodicidade": row.periodicidade,
                    "dia": row.dia_execucao
                }
                for row in contas_fixas
            ]
        }


def generate_ai_insights(usuario_id):
    """
    Gera insights personalizados usando Gemini baseado no histórico de gastos.

    Args:
        usuario_id (int): ID do usuário

    Returns:
        str: Relatório formatado com insights e sugestões
    """
    if not gemini_model:
        raise Exception("Modelo Gemini não configurado.")

    # Obter dados estruturados
    dados = get_spending_analysis(usuario_id)

    # Calcular métricas adicionais
    mes_atual = dados["comparacao_mensal"].get(dados["mes_atual"], 0)
    meses_anteriores = [v for k, v in dados["comparacao_mensal"].items() if k != dados["mes_atual"]]
    mes_anterior = meses_anteriores[0] if meses_anteriores else 0

    variacao_percentual = 0
    if mes_anterior > 0:
        variacao_percentual = round(((mes_atual - mes_anterior) / mes_anterior) * 100, 1)

    # Preparar prompt para o Gemini
    prompt = f"""
Você é um assistente financeiro pessoal. Analise os dados abaixo e gere insights personalizados e acionáveis.

**DADOS DO USUÁRIO:**

📊 **Resumo Mensal:**
- Total gasto este mês: R$ {mes_atual:,.2f}
- Mês anterior: R$ {mes_anterior:,.2f}
- Variação: {variacao_percentual:+.1f}%

💰 **Gastos por Categoria (Top 5):**
{chr(10).join([f"- {cat['categoria']} / {cat['subcategoria']}: R$ {cat['total']:,.2f} ({cat['quantidade']} transações)" for cat in dados['gastos_por_categoria'][:5]])}

📅 **Padrão Semanal:**
{chr(10).join([f"- {dia['dia']}: R$ {dia['total']:,.2f} ({dia['quantidade']} transações)" for dia in dados['gastos_por_dia_semana']])}

🎯 **Status dos Potes:**
{chr(10).join([f"- {pote['nome']}: R$ {pote['gasto_atual']:,.2f} / R$ {pote['limite']:,.2f} ({pote['percentual_uso']}%)" for pote in dados['potes']]) if dados['potes'] else "- Nenhum pote configurado"}

💳 **Maiores Gastos:**
{chr(10).join([f"- {gasto['data']} - {gasto['descricao']}: R$ {gasto['valor']:,.2f}" for gasto in dados['maiores_gastos']])}

🔧 **Contas Fixas:**
{chr(10).join([f"- {conta['descricao']}: R$ {conta['valor']:,.2f} ({conta['periodicidade']})" for conta in dados['contas_fixas'][:5]]) if dados['contas_fixas'] else "- Nenhuma conta fixa cadastrada"}

---

**INSTRUÇÕES:**

Gere um relatório com as seguintes seções (use emojis e formatação clara):

1. 📊 Resumo do Mês: Resumo geral dos gastos (2-3 linhas)
2. 🔍 Principais Insights (3-5 pontos):
   - Identifique padrões importantes
   - Compare com meses anteriores
   - Destaque categorias que chamam atenção
   - Analise dias da semana com mais gastos
3. ⚠️ Alertas (se aplicável):
   - Potes próximos ou acima do limite
   - Categorias com aumento significativo
   - Gastos atípicos
4. 💡 Sugestões de Economia (2-4 sugestões práticas e acionáveis):
   - Baseie-se nos dados reais
   - Seja específico com valores
   - Priorize as oportunidades mais relevantes

**FORMATO DE RESPOSTA:**
- Seja direto e objetivo
- Use valores reais dos dados
- Evite frases genéricas
- Foque em insights acionáveis
- Máximo de 15 linhas no total
- NÃO use asteriscos ** para negrito nos títulos das seções, apenas os emojis e texto simples
"""

    try:
        response = gemini_model.generate_content(prompt)
        insights = response.text.strip()

        # Adicionar rodapé
        insights += "\n\n💬 _Para análises mais específicas, pergunte:_"
        insights += "\n• _\"Quanto gastei com [categoria]?\"_"
        insights += "\n• _\"Comparar gastos de [mês]\"_"

        return insights

    except Exception as e:
        print(f"[ANALYTICS-ERRO] Falha ao gerar insights: {e}")
        raise Exception(f"Não consegui gerar a análise. Erro: {str(e)}")


def get_category_comparison(usuario_id, categoria_nome, meses=3):
    """
    Compara gastos de uma categoria específica ao longo do tempo.

    Args:
        usuario_id (int): ID do usuário
        categoria_nome (str): Nome da categoria/subcategoria para comparar
        meses (int): Quantos meses comparar

    Returns:
        str: Relatório formatado da comparação
    """
    with db_engine.connect() as conn:
        hoje = date.today()
        data_inicio = date(hoje.year, hoje.month, 1) - timedelta(days=30 * meses)

        sql_comparacao = text("""
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
                AND (
                    mc.nome_macro ILIKE :categoria
                    OR sc.nome_sub ILIKE :categoria
                )
                AND t.data_transacao >= :data_inicio
            GROUP BY TO_CHAR(t.data_transacao, 'YYYY-MM'), mc.nome_macro, sc.nome_sub
            ORDER BY mes DESC
        """)

        resultados = conn.execute(sql_comparacao, {
            "uid": usuario_id,
            "categoria": f"%{categoria_nome}%",
            "data_inicio": data_inicio
        }).fetchall()

        if not resultados:
            return f"❌ Não encontrei gastos com '{categoria_nome}' nos últimos {meses} meses."

        # Montar relatório
        relatorio = f"📊 **Análise: {resultados[0].nome_subcategoria}**\n\n"

        total_geral = sum(float(r.total) for r in resultados)
        media_mensal = total_geral / len(set(r.mes for r in resultados))

        relatorio += f"💰 **Total em {meses} meses:** R$ {total_geral:,.2f}\n"
        relatorio += f"📈 **Média mensal:** R$ {media_mensal:,.2f}\n\n"
        relatorio += "**Detalhamento:**\n"

        for row in resultados:
            mes_formatado = datetime.strptime(row.mes, '%Y-%m').strftime('%b/%Y')
            relatorio += f"• {mes_formatado}: R$ {float(row.total):,.2f} ({row.quantidade}x)\n"

        # Calcular tendência
        if len(resultados) >= 2:
            primeiro = float(resultados[-1].total)
            ultimo = float(resultados[0].total)
            variacao = ((ultimo - primeiro) / primeiro) * 100 if primeiro > 0 else 0

            emoji = "📈" if variacao > 0 else "📉"
            relatorio += f"\n{emoji} **Variação:** {variacao:+.1f}% (primeiro vs último mês)"

        return relatorio


def get_monthly_comparison(usuario_id, mes_referencia=None):
    """
    Compara o mês atual (ou especificado) com o mês anterior.

    Args:
        usuario_id (int): ID do usuário
        mes_referencia (str): Mês no formato 'YYYY-MM' (opcional, default: mês atual)

    Returns:
        str: Relatório formatado da comparação
    """
    with db_engine.connect() as conn:
        if not mes_referencia:
            mes_referencia = date.today().strftime('%Y-%m')

        # Calcular mês anterior
        mes_ref_date = datetime.strptime(mes_referencia, '%Y-%m').date()
        mes_anterior_date = date(mes_ref_date.year, mes_ref_date.month, 1) - timedelta(days=1)
        mes_anterior = mes_anterior_date.strftime('%Y-%m')

        sql_comparacao_categorias = text("""
            SELECT
                TO_CHAR(t.data_transacao, 'YYYY-MM') as mes,
                mc.nome_macro,
                SUM(t.valor) as total
            FROM Transacoes t
            JOIN SubCategoria sc ON t.subcategoria_id = sc.id
            JOIN MacroCategoria mc ON sc.macro_id = mc.id
            WHERE t.usuario_id = :uid
                AND t.tipo_transacao = 'Despesa'
                AND t.consolidada = true
                AND TO_CHAR(t.data_transacao, 'YYYY-MM') IN (:mes_ref, :mes_ant)
            GROUP BY TO_CHAR(t.data_transacao, 'YYYY-MM'), mc.nome_macro
            ORDER BY total DESC
        """)

        resultados = conn.execute(sql_comparacao_categorias, {
            "uid": usuario_id,
            "mes_ref": mes_referencia,
            "mes_ant": mes_anterior
        }).fetchall()

        if not resultados:
            return f"❌ Não encontrei gastos para comparar em {mes_referencia}."

        # Agrupar por mês
        gastos_ref = {}
        gastos_ant = {}

        for row in resultados:
            if row.mes == mes_referencia:
                gastos_ref[row.nome_macro] = float(row.total)
            else:
                gastos_ant[row.nome_macro] = float(row.total)

        total_ref = sum(gastos_ref.values())
        total_ant = sum(gastos_ant.values())

        # Montar relatório
        mes_ref_date = datetime.strptime(mes_referencia, '%Y-%m').date()
        mes_ant_date = datetime.strptime(mes_anterior, '%Y-%m').date()
        mes_ref_nome = formatar_mes_ano_pt(mes_ref_date)
        mes_ant_nome = formatar_mes_ano_pt(mes_ant_date)

        relatorio = f"📊 **Comparação: {mes_ref_nome} vs {mes_ant_nome}**\n\n"

        variacao_total = ((total_ref - total_ant) / total_ant * 100) if total_ant > 0 else 0
        emoji_total = "📈" if variacao_total > 0 else "📉"

        relatorio += f"💰 **{mes_ref_nome}:** R$ {total_ref:,.2f}\n"
        relatorio += f"💰 **{mes_ant_nome}:** R$ {total_ant:,.2f}\n"
        relatorio += f"{emoji_total} **Variação:** {variacao_total:+.1f}%\n\n"

        # Top 5 categorias com maiores variações
        relatorio += "**Maiores mudanças por categoria:**\n"

        todas_categorias = set(gastos_ref.keys()) | set(gastos_ant.keys())
        variacoes = []

        for cat in todas_categorias:
            val_ref = gastos_ref.get(cat, 0)
            val_ant = gastos_ant.get(cat, 0)

            if val_ant > 0:
                var_perc = ((val_ref - val_ant) / val_ant) * 100
            elif val_ref > 0:
                var_perc = 100
            else:
                var_perc = 0

            variacoes.append({
                "categoria": cat,
                "ref": val_ref,
                "ant": val_ant,
                "variacao": var_perc
            })

        # Ordenar por maior variação absoluta
        variacoes.sort(key=lambda x: abs(x["variacao"]), reverse=True)

        for var in variacoes[:5]:
            emoji = "🔴" if var["variacao"] > 10 else "🟢" if var["variacao"] < -10 else "⚪"
            relatorio += f"{emoji} **{var['categoria']}:** R$ {var['ref']:,.2f} ({var['variacao']:+.1f}%)\n"

        return relatorio
