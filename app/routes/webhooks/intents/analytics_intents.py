# app/routes/webhooks/intents/analytics_intents.py
"""
Intent handlers para análises financeiras e insights inteligentes.

Implementa análises avançadas usando:
- Comparações mensais
- Previsões de gastos
- Análises de categorias
- Geração de gráficos
- Insights de IA (Gemini)

TODO: Implementar lógica completa quando analytics_service estiver pronto.
"""

from typing import Dict, Any
from .base_intent import BaseIntent


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
        """Extrai parâmetros de análise."""
        # TODO: Implementar extração de período/foco com gemini_service
        return {
            "periodo": "mes_atual",  # mes_atual, mes_passado, trimestre, ano
            "foco": None,            # gastos, receitas, economia, categorias
        }

    def validate(self) -> str | None:
        """Sem validação necessária (usa defaults)."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera análise inteligente via Gemini."""
        # TODO: Implementar via gemini_service
        # 1. Buscar dados financeiros do período
        # transacoes = finance_service.get_transacoes_periodo(...)
        # saldos = finance_service.get_saldo_contas(...)
        # categorias = finance_service.get_gastos_por_categoria(...)
        #
        # 2. Gerar análise com Gemini
        # analise = gemini_service.generate_financial_insights(
        #     usuario_id=self.usuario_id,
        #     transacoes=transacoes,
        #     saldos=saldos,
        #     categorias=categorias
        # )

        raise NotImplementedError(
            "AnaliseInteligenteIntent ainda não implementado. "
            "Aguardando analytics_service e gemini_service completos."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata análise inteligente."""
        msg = "🤖 *Análise Financeira Inteligente*\n\n"
        msg += data.get("analise_texto", "Análise indisponível")
        return msg


class ComparacaoMensalIntent(BaseIntent):
    """
    Handler para intent 'Comparação Mensal'.

    Compara gastos/receitas do mês atual com mês anterior.

    Exemplo de mensagem:
    - "Comparar meus gastos com mês passado"
    - "Gastei mais ou menos esse mês?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de comparação."""
        return {
            "tipo": "gastos",  # gastos, receitas, economia
            "mes_referencia": None,  # None = mês atual vs anterior
        }

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera comparação mensal."""
        # TODO: Implementar via analytics_service
        # comparacao = analytics_service.comparar_meses(
        #     conn=self.conn,
        #     usuario_id=self.usuario_id,
        #     mes_atual=data.hoje(),
        #     mes_anterior=data.hoje() - relativedelta(months=1),
        #     tipo=self.params["tipo"]
        # )

        raise NotImplementedError(
            "ComparacaoMensalIntent ainda não implementado. "
            "Aguardando analytics_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata comparação."""
        msg = "📊 *Comparação Mensal*\n\n"

        msg += f"*{data['mes_atual_nome']}*\n"
        msg += f"Total: {data['total_atual']}\n\n"

        msg += f"*{data['mes_anterior_nome']}*\n"
        msg += f"Total: {data['total_anterior']}\n\n"

        diferenca = data['diferenca']
        diferenca_pct = data['diferenca_percentual']

        if diferenca > 0:
            emoji = "📈"
            texto = "aumento"
        else:
            emoji = "📉"
            texto = "redução"

        msg += f"{emoji} {texto.title()} de {abs(diferenca)} ({abs(diferenca_pct):.1f}%)"

        return msg


class PrevisaoGastosIntent(BaseIntent):
    """
    Handler para intent 'Previsão de Gastos'.

    Prevê gastos futuros baseado em histórico.

    Exemplo de mensagem:
    - "Quanto vou gastar esse mês?"
    - "Previsão de gastos para próximo mês"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de previsão."""
        return {
            "periodo": "mes_atual",  # mes_atual, proximo_mes
            "categoria": None,       # None = todas categorias
        }

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera previsão de gastos."""
        # TODO: Implementar via analytics_service
        # previsao = analytics_service.prever_gastos(
        #     conn=self.conn,
        #     usuario_id=self.usuario_id,
        #     periodo=self.params["periodo"],
        #     categoria=self.params.get("categoria")
        # )

        raise NotImplementedError(
            "PrevisaoGastosIntent ainda não implementado. "
            "Aguardando analytics_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata previsão."""
        msg = "🔮 *Previsão de Gastos*\n\n"

        msg += f"Período: {data['periodo']}\n"
        msg += f"Previsão: {data['valor_previsto']}\n\n"

        msg += f"📊 Baseado em:\n"
        msg += f"• Média últimos 3 meses: {data['media_historica']}\n"
        msg += f"• Tendência: {data['tendencia']}\n"

        if data.get("confianca"):
            msg += f"\n🎯 Confiança: {data['confianca']}%"

        return msg


class GraficoGastosIntent(BaseIntent):
    """
    Handler para intent 'Gráfico de Gastos'.

    Gera gráfico visual de gastos por categoria/período.

    Exemplo de mensagem:
    - "Gráfico dos meus gastos"
    - "Mostrar distribuição de despesas"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros do gráfico."""
        return {
            "tipo_grafico": "pizza",  # pizza, barras, linha
            "periodo": "mes_atual",
            "agrupamento": "categoria",  # categoria, conta, dia
        }

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        """Gera gráfico de gastos."""
        # TODO: Implementar via analytics_service
        # 1. Buscar dados
        # dados = finance_service.get_gastos_agrupados(...)
        #
        # 2. Gerar gráfico (matplotlib/plotly)
        # grafico_url = analytics_service.gerar_grafico(
        #     tipo=self.params["tipo_grafico"],
        #     dados=dados,
        #     titulo="Gastos por Categoria"
        # )

        raise NotImplementedError(
            "GraficoGastosIntent ainda não implementado. "
            "Aguardando analytics_service e geração de gráficos."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna URL do gráfico."""
        # WhatsApp pode receber imagem via URL
        msg = "📊 *Gráfico de Gastos*\n\n"
        msg += f"Período: {data['periodo']}\n"
        msg += f"Ver gráfico: {data['grafico_url']}"

        return msg


class ConsultaPeriodoIntent(BaseIntent):
    """
    Handler para intent 'Consulta Período'.

    Consulta transações de um período específico.

    Exemplo de mensagem:
    - "Gastos de janeiro"
    - "Receitas do mês passado"
    - "Transações da última semana"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de período."""
        # TODO: Implementar extração com gemini_service
        return {
            "data_inicio": None,  # date object
            "data_fim": None,     # date object
            "tipo": None,         # despesa, renda, None (todas)
        }

    def validate(self) -> str | None:
        """Valida período."""
        if not self.params.get("data_inicio"):
            return "❌ Não consegui identificar o período. Seja mais específico."
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca transações do período."""
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "ConsultaPeriodoIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de transações."""
        transacoes = data.get("transacoes", [])

        msg = f"📅 *Transações - {data['periodo']}*\n\n"

        total_receitas = 0
        total_despesas = 0

        for t in transacoes:
            emoji = "💰" if t['tipo'] == 'renda' else "💸"
            msg += f"{emoji} {t['descricao']}: {t['valor_formatado']}\n"

            if t['tipo'] == 'renda':
                total_receitas += t['valor']
            else:
                total_despesas += t['valor']

        msg += f"\n📊 Resumo:\n"
        msg += f"Receitas: {total_receitas}\n"
        msg += f"Despesas: {total_despesas}\n"
        msg += f"Saldo: {total_receitas - total_despesas}"

        return msg


class ConsultaCategoriaIntent(BaseIntent):
    """
    Handler para intent 'Consulta Categoria Específica'.

    Consulta gastos de uma categoria específica.

    Exemplo de mensagem:
    - "Quanto gastei com alimentação?"
    - "Gastos de transporte esse mês"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai categoria e período."""
        # TODO: Implementar extração com gemini_service
        return {
            "categoria": None,
            "periodo": "mes_atual",
        }

    def validate(self) -> str | None:
        """Valida categoria."""
        if not self.params.get("categoria"):
            return "❌ Não consegui identificar a categoria. Qual categoria quer consultar?"
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca gastos da categoria."""
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "ConsultaCategoriaIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata gastos da categoria."""
        msg = f"📊 *{data['categoria']}*\n\n"
        msg += f"Período: {data['periodo']}\n"
        msg += f"Total: {data['total']}\n\n"

        if data.get("transacoes"):
            msg += "Últimas transações:\n"
            for t in data["transacoes"][:5]:
                msg += f"• {t['descricao']}: {t['valor']}\n"

        return msg


__all__ = [
    'AnaliseInteligenteIntent',
    'ComparacaoMensalIntent',
    'PrevisaoGastosIntent',
    'GraficoGastosIntent',
    'ConsultaPeriodoIntent',
    'ConsultaCategoriaIntent',
]
