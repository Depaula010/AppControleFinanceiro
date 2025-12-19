# app/routes/webhooks/intents/query_intents.py
"""
Intent handlers para consultas financeiras.

Exemplos implementados:
- ConsultaSaldoIntent: Consulta saldo de contas
- ConsultaReservaIntent: Status da reserva de emergência

TODO: Implementar outros query intents quando necessário:
- ConsultaPotesIntent
- ListarContasIntent
- AjustarSaldoIntent
- ConsultaPeriodoIntent
- ConsultaContasFixasIntent
- etc.
"""

from typing import Dict, Any
from app.services import finance_service, gemini_service
from app.utils import formatar_moeda
from .base_intent import BaseIntent


class ConsultaSaldoIntent(BaseIntent):
    """
    Handler para intent 'Consulta Saldo'.

    Consulta o saldo de uma ou mais contas do usuário.
    Se nenhuma conta for mencionada, mostra todas.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai nome da conta (opcional) da mensagem."""
        # Usar Gemini para extrair conta mencionada
        params = gemini_service.extract_account_query_params(
            self.mensagem,
            self.usuario_id
        )

        return {
            "conta_nome": params.get("conta_nome"),  # None = todas as contas
        }

    def execute(self) -> Dict[str, Any]:
        """Consulta saldo e retorna dados."""
        conta_nome = self.params.get("conta_nome")

        # Buscar saldos
        saldos = finance_service.get_saldo_contas(
            self.conn,
            self.usuario_id,
            conta_id=None  # None = todas
        )

        # Filtrar por nome se especificado
        if conta_nome:
            saldos = [s for s in saldos if conta_nome.lower() in s["nome_conta"].lower()]

        return {
            "saldos": saldos,
            "conta_especifica": conta_nome
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata saldos para WhatsApp."""
        saldos = data["saldos"]
        conta_especifica = data["conta_especifica"]

        if not saldos:
            if conta_especifica:
                return f"❌ Conta '{conta_especifica}' não encontrada."
            return "❌ Você ainda não tem contas cadastradas."

        # Título
        if conta_especifica and len(saldos) == 1:
            msg = f"💰 *Saldo {saldos[0]['nome_conta']}*\n\n"
        else:
            msg = "💰 *Seus Saldos*\n\n"

        # Listar saldos
        total = 0
        for s in saldos:
            saldo_atual = s["saldo_atual"]
            total += saldo_atual

            # Emoji baseado no tipo de conta
            emoji = self._get_account_emoji(s["tipo_conta"])

            msg += f"{emoji} *{s['nome_conta']}*\n"
            msg += f"   {formatar_moeda(saldo_atual)}\n\n"

        # Total (apenas se múltiplas contas)
        if len(saldos) > 1:
            msg += f"━━━━━━━━━━━━━━\n"
            msg += f"💵 *Total:* {formatar_moeda(total)}"

        return msg

    def _get_account_emoji(self, tipo_conta: str) -> str:
        """Retorna emoji baseado no tipo de conta."""
        emojis = {
            "Conta Corrente": "🏦",
            "Conta Poupança": "💰",
            "Investimento": "📈",
            "Cartão de Crédito": "💳",
            "Dinheiro": "💵",
            "Outro": "📊"
        }
        return emojis.get(tipo_conta, "💰")


class ConsultaReservaIntent(BaseIntent):
    """
    Handler para intent 'Consulta Reserva'.

    Mostra status da reserva de emergência:
    - Gasto mensal essencial
    - Reserva ideal (6 meses)
    - Progresso atual
    """

    def extract_params(self) -> Dict[str, Any]:
        """Não precisa extrair parâmetros."""
        return {}

    def execute(self) -> Dict[str, Any]:
        """Calcula status da reserva."""
        gasto_mensal, reserva_ideal, meses_config = finance_service.get_reserva_status(
            self.conn,
            self.usuario_id
        )

        # Buscar saldo atual em contas de reserva
        # (por simplicidade, pegar soma de todas as contas tipo "Poupança" ou "Investimento")
        saldos = finance_service.get_saldo_contas(self.conn, self.usuario_id)
        reserva_atual = sum(
            s["saldo_atual"]
            for s in saldos
            if s["tipo_conta"] in ["Conta Poupança", "Investimento"]
        )

        # Calcular progresso
        if reserva_ideal > 0:
            progresso_pct = (reserva_atual / reserva_ideal) * 100
        else:
            progresso_pct = 0

        return {
            "gasto_mensal": gasto_mensal,
            "reserva_ideal": reserva_ideal,
            "reserva_atual": reserva_atual,
            "progresso_pct": progresso_pct,
            "meses_config": meses_config
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata status da reserva para WhatsApp."""
        gasto = data["gasto_mensal"]
        ideal = data["reserva_ideal"]
        atual = data["reserva_atual"]
        progresso = data["progresso_pct"]
        meses = data["meses_config"]

        msg = "🏦 *Reserva de Emergência*\n\n"

        # Gastos essenciais
        msg += f"📊 *Gastos Essenciais Mensais:*\n"
        msg += f"   {formatar_moeda(gasto)}\n\n"

        # Reserva ideal
        msg += f"🎯 *Reserva Ideal ({meses} meses):*\n"
        msg += f"   {formatar_moeda(ideal)}\n\n"

        # Reserva atual
        msg += f"💰 *Reserva Atual:*\n"
        msg += f"   {formatar_moeda(atual)}\n\n"

        # Progresso
        msg += f"📈 *Progresso:*\n"
        msg += f"   {progresso:.1f}%\n"
        msg += f"   {self._get_progress_bar(progresso)}\n\n"

        # Status e recomendação
        if progresso >= 100:
            msg += "✅ *Parabéns!* Sua reserva está completa!"
        elif progresso >= 50:
            msg += f"💪 *Bom progresso!* Faltam {formatar_moeda(ideal - atual)}"
        else:
            msg += f"⚠️ *Continue firme!* Faltam {formatar_moeda(ideal - atual)}"

        return msg

    def _get_progress_bar(self, progresso_pct: float) -> str:
        """Cria barra de progresso visual."""
        bars = int(progresso_pct / 10)  # 0-10 barras
        bars = min(bars, 10)  # Max 10

        filled = "█" * bars
        empty = "░" * (10 - bars)

        return f"{filled}{empty}"


class ConsultaPotesIntent(BaseIntent):
    """
    Handler para intent 'Consulta Potes'.

    Mostra distribuição de potes/categorias de gastos do usuário.
    Sistema de envelope budgeting.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de consulta."""
        return {
            "mes_referencia": None,  # None = mês atual
        }

    def execute(self) -> Dict[str, Any]:
        """Busca potes configurados e gastos."""
        # Buscar configuração de potes do usuário
        # Por enquanto, vamos usar categorias como "potes"
        gastos_por_categoria = finance_service.get_gastos_por_categoria(
            self.conn,
            self.usuario_id,
            mes_referencia=self.params.get("mes_referencia")
        )

        return {
            "potes": gastos_por_categoria,
            "mes": self.params.get("mes_referencia") or "atual"
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata potes para WhatsApp."""
        potes = data["potes"]

        if not potes:
            return "❌ Nenhum pote/categoria encontrado para este período."

        msg = "🏺 *Seus Potes/Categorias*\n\n"

        total_gasto = sum(p["valor_gasto"] for p in potes)

        for pote in potes:
            nome = pote["categoria"]
            gasto = pote["valor_gasto"]
            percentual = (gasto / total_gasto * 100) if total_gasto > 0 else 0

            msg += f"• *{nome}*\n"
            msg += f"  {formatar_moeda(gasto)} ({percentual:.1f}%)\n\n"

        msg += f"━━━━━━━━━━━━━━\n"
        msg += f"💰 *Total:* {formatar_moeda(total_gasto)}"

        return msg


__all__ = [
    'ConsultaSaldoIntent',
    'ConsultaReservaIntent',
    'ConsultaPotesIntent',
]
