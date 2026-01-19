# app/routes/webhooks/intents/analytics_intents.py
"""
Intent handlers para análises financeiras e insights inteligentes.

Implementa análises avançadas usando:
- Comparações mensais
- Previsões de gastos
- Análises de categorias
- Geração de gráficos
- Insights de IA (Gemini)
"""

from typing import Dict, Any, Optional
from .base_intent import BaseIntent
from app.services import gemini_service
from app.shared.formatters.currency_formatter import formatar_moeda


class AnaliseInteligenteIntent(BaseIntent):
    """
    Handler para intent 'Análise Inteligente'.

    Usa Gemini AI para gerar insights financeiros personalizados.

    Exemplo de mensagem:
    - "Como estão minhas finanças?"
    - "Análise meus gastos desse mês"
    - "Me dê insights sobre minhas despesas"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários - usa contexto completo."""
        return {}

    def validate(self) -> Optional[str]:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera análise inteligente via Gemini."""
        from app.services.analytics_service import generate_ai_insights

        insights = generate_ai_insights(self.usuario_id)

        return {
            "insights": insights
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata análise inteligente."""
        insights = data.get("insights", "Análise indisponível")
        return f"📊 *Análise Inteligente de Gastos*\n\n{insights}"


class ComparacaoMensalIntent(BaseIntent):
    """
    Handler para intent 'Comparação Mensal'.

    Compara gastos/receitas do mês atual com mês anterior.

    Exemplo de mensagem:
    - "Comparar meus gastos com mês passado"
    - "Gastei mais ou menos esse mês?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera comparação mensal."""
        from app.services.analytics_service import get_monthly_comparison

        comparacao = get_monthly_comparison(self.usuario_id)

        return {
            "comparacao_formatada": comparacao
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna comparação já formatada pelo serviço."""
        return data.get("comparacao_formatada", "❌ Erro ao gerar comparação.")


class PrevisaoGastosIntent(BaseIntent):
    """
    Handler para intent 'Previsão de Gastos'.

    Prevê gastos futuros baseado em histórico.

    Exemplo de mensagem:
    - "Quanto vou gastar esse mês?"
    - "Previsão de gastos para próximo mês"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera previsão de gastos."""
        from app.services.forecast_service import generate_forecast_insights

        previsao = generate_forecast_insights(self.usuario_id)

        return {
            "previsao": previsao
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata previsão."""
        previsao = data.get("previsao", "Previsão indisponível")
        return f"📈 *Previsão de Gastos*\n\n{previsao}"


class GraficoGastosIntent(BaseIntent):
    """
    Handler para intent 'Gráfico de Gastos'.

    Gera gráfico visual de gastos por categoria/período
    e envia via WhatsApp.

    Exemplo de mensagem:
    - "Gráfico dos meus gastos"
    - "Mostrar distribuição de despesas"
    - "Gráfico de barras dos últimos meses"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai tipo de gráfico e período."""
        chart_info = gemini_service.extract_chart_type(self.mensagem, self.usuario_id)

        return {
            "tipo_grafico": chart_info.get('tipo_grafico', 'pizza'),
            "periodo_dias": chart_info.get('periodo_dias', 30),
            "num_meses": chart_info.get('num_meses', 6)
        }

    def validate(self) -> Optional[str]:
        """Valida se numero_whatsapp está disponível."""
        if not self.numero_whatsapp:
            return "❌ Não foi possível identificar seu número para enviar o gráfico."
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera e envia gráfico via WhatsApp."""
        from app.services import chart_service, notification_service
        import os

        tipo_grafico = self.params["tipo_grafico"]
        periodo_dias = self.params["periodo_dias"]
        num_meses = self.params["num_meses"]

        # Gerar gráfico apropriado
        chart_bytes = None
        caption = ""

        if tipo_grafico == 'pizza':
            chart_bytes = chart_service.generate_pie_chart(self.usuario_id, periodo_dias)
            caption = f"📊 Gastos por Categoria - Últimos {periodo_dias} dias"

        elif tipo_grafico == 'barras':
            chart_bytes = chart_service.generate_bar_chart(self.usuario_id, num_meses)
            caption = f"📊 Evolução Mensal - Últimos {num_meses} meses"

        elif tipo_grafico == 'linha':
            chart_bytes = chart_service.generate_line_chart(self.usuario_id, num_meses)
            caption = f"📈 Evolução do Saldo - Últimos {num_meses} meses"

        # Verificar se gráfico foi gerado
        if chart_bytes is None:
            return {
                "sucesso": False,
                "erro": "sem_dados",
                "mensagem": "❌ Não há dados suficientes para gerar o gráfico no período solicitado."
            }

        # Enviar imagem via WhatsApp
        BOT_WHATSAPP_URL = os.getenv("WHATSAPP_BOT_URL", "http://localhost:3003")
        API_SECRET_KEY = os.getenv("API_SECRET_KEY", "")

        sucesso = notification_service.enviar_imagem_whatsapp_bytes(
            self.numero_whatsapp,
            chart_bytes,
            caption,
            BOT_WHATSAPP_URL,
            API_SECRET_KEY
        )

        return {
            "sucesso": sucesso,
            "caption": caption,
            "tipo_grafico": tipo_grafico
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta do envio."""
        if data.get("erro") == "sem_dados":
            return data["mensagem"]

        if data.get("sucesso"):
            return f"✅ {data['caption']}"
        else:
            return "❌ Não consegui enviar o gráfico. Tente novamente mais tarde."


class ConsultaPeriodoIntent(BaseIntent):
    """
    Handler para intent 'Consulta Período'.

    Consulta transações de um período específico.

    Exemplo de mensagem:
    - "Gastos de janeiro"
    - "Receitas do mês passado"
    - "Transações da última semana"
    - "Quanto gastei com alimentação esse mês?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai período e filtro de categoria."""
        period_data = gemini_service.extract_period_query(self.mensagem, self.usuario_id)

        return {
            "period_type": period_data.get('period_type', 'hoje'),
            "categoria_filtro": period_data.get('categoria')
        }

    def validate(self) -> Optional[str]:
        """Sem validação necessária - fallback para 'hoje'."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca transações do período."""
        from app.services.period_query_service import PeriodQueryService

        period_type = self.params["period_type"]
        categoria_filtro = self.params.get("categoria_filtro")

        # Calcular datas
        data_inicio, data_fim, desc_periodo = PeriodQueryService.get_period_dates(period_type)

        if categoria_filtro:
            # Consulta com filtro de categoria
            total, transacoes_raw = PeriodQueryService.query_by_category_and_period(
                self.conn, self.usuario_id, categoria_filtro, data_inicio, data_fim
            )

            return {
                "tipo": "categoria",
                "categoria": categoria_filtro,
                "total": total,
                "transacoes": transacoes_raw,
                "desc_periodo": desc_periodo
            }
        else:
            # Consulta geral do período
            total, transacoes = PeriodQueryService.query_expenses_by_period(
                self.conn, self.usuario_id, data_inicio, data_fim
            )

            return {
                "tipo": "geral",
                "total": total,
                "transacoes": transacoes,
                "desc_periodo": desc_periodo
            }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta baseada no tipo de consulta."""
        from app.services.period_query_service import PeriodQueryService

        if data["tipo"] == "categoria":
            categoria = data["categoria"]
            total = data["total"]
            transacoes = data["transacoes"]
            desc_periodo = data["desc_periodo"]

            if total == 0:
                return f"✅ Você não gastou nada com '{categoria}' {desc_periodo}! 🎉"

            msg = f"💸 *GASTOS COM {categoria.upper()}* {desc_periodo.upper()}\n\n"
            msg += f"💰 Total: *{formatar_moeda(total)}*\n\n"
            msg += "📋 Transações:\n"

            for trans in transacoes[:10]:  # Limitar a 10
                desc, valor, data_trans = trans
                valor_abs = abs(float(valor))
                data_fmt = data_trans.strftime('%d/%m')
                msg += f"• {desc}: {formatar_moeda(valor_abs)} ({data_fmt})\n"

            if len(transacoes) > 10:
                msg += f"\n... e mais {len(transacoes) - 10} transação(ões)"

            return msg
        else:
            # Formato geral usando função do serviço
            return PeriodQueryService.format_period_query_response(
                data["total"], data["transacoes"], data["desc_periodo"]
            )


class ConsultaCategoriaIntent(BaseIntent):
    """
    Handler para intent 'Consulta Categoria Específica'.

    Consulta gastos de uma categoria específica no mês atual.

    Exemplo de mensagem:
    - "Quanto gastei com alimentação?"
    - "Gastos de transporte esse mês"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai categoria da mensagem."""
        cat_data = gemini_service.extract_category_query(self.mensagem, self.usuario_id)

        return {
            "nome_categoria": cat_data.get('nome_categoria')
        }

    def validate(self) -> Optional[str]:
        """Valida se categoria foi identificada."""
        if not self.params.get("nome_categoria"):
            return "❌ Não consegui identificar a categoria. Qual categoria quer consultar?"
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca gastos da categoria."""
        from app.services import finance_service

        nome_categoria = self.params["nome_categoria"]
        valor_gasto = finance_service.get_category_spending(
            self.conn, self.usuario_id, nome_categoria
        )

        return {
            "categoria": nome_categoria,
            "valor_gasto": valor_gasto
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata gastos da categoria."""
        categoria = data["categoria"]
        valor = data["valor_gasto"]

        msg = f"ℹ️ *Consulta de Categoria (Este Mês)*\n\n"
        msg += f"Você gastou *{formatar_moeda(valor)}* com '{categoria}'."

        return msg


__all__ = [
    'AnaliseInteligenteIntent',
    'ComparacaoMensalIntent',
    'PrevisaoGastosIntent',
    'GraficoGastosIntent',
    'ConsultaPeriodoIntent',
    'ConsultaCategoriaIntent',
]
