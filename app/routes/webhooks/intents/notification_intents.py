# app/routes/webhooks/intents/notification_intents.py
"""
Intent handlers para configuração de notificações.

Permite usuários configurarem notificações automáticas via WhatsApp para:
- Vencimentos de contas
- Lembretes de reserva de emergência
- Relatórios mensais
- Alertas de gastos

TODO: Implementar lógica completa quando notification_service estiver pronto.
"""

from typing import Dict, Any
from .base_intent import BaseIntent


class ConfigurarNotificacoesIntent(BaseIntent):
    """
    Handler para intent 'Configurar Notificações'.

    Permite configurar preferências de notificações automáticas.

    Exemplo de mensagem:
    - "Quero receber lembretes de vencimentos"
    - "Ativar notificações diárias"
    - "Desativar alertas de gastos"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros de configuração de notificação."""
        # TODO: Implementar extração com gemini_service
        return {
            "tipo_notificacao": None,  # vencimentos, relatorio, alerta_gasto, etc.
            "acao": None,              # ativar, desativar, configurar
            "frequencia": None,        # diaria, semanal, mensal
            "horario": None,           # time object
        }

    def validate(self) -> str | None:
        """Valida parâmetros de configuração."""
        if not self.params.get("tipo_notificacao"):
            return (
                "❌ Não consegui identificar qual notificação configurar.\n\n"
                "Você pode configurar:\n"
                "• Vencimentos de contas\n"
                "• Relatórios mensais\n"
                "• Alertas de gastos\n"
                "• Lembretes de reserva"
            )

        if not self.params.get("acao"):
            return "❌ Não entendi se quer ativar ou desativar. Seja mais específico."

        return None

    def execute(self) -> Dict[str, Any]:
        """Configura notificações no sistema."""
        # TODO: Implementar via notification_service
        # notification_service.configure_user_notifications(
        #     usuario_id=self.usuario_id,
        #     tipo=self.params["tipo_notificacao"],
        #     acao=self.params["acao"],
        #     frequencia=self.params.get("frequencia"),
        #     horario=self.params.get("horario")
        # )

        raise NotImplementedError(
            "ConfigurarNotificacoesIntent ainda não implementado. "
            "Aguardando notification_service completo."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        tipo = data.get("tipo_notificacao", "Notificação")
        acao = data.get("acao", "configurada")

        msg = f"🔔 *{tipo.title()}* {acao} com sucesso!\n\n"

        if data.get("frequencia"):
            msg += f"📅 Frequência: {data['frequencia']}\n"

        if data.get("horario"):
            msg += f"🕐 Horário: {data['horario']}\n"

        return msg


class VencimentosHojeIntent(BaseIntent):
    """
    Handler para intent 'Vencimentos Hoje'.

    Consulta contas que vencem hoje.

    Exemplo de mensagem:
    - "O que vence hoje?"
    - "Contas de hoje"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros (nenhum necessário)."""
        return {}

    def validate(self) -> str | None:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca vencimentos de hoje."""
        # TODO: Implementar via finance_service
        # vencimentos = finance_service.get_vencimentos_por_periodo(
        #     conn=self.conn,
        #     usuario_id=self.usuario_id,
        #     periodo="hoje"
        # )

        raise NotImplementedError(
            "VencimentosHojeIntent ainda não implementado. "
            "Aguardando implementação em finance_service."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de vencimentos."""
        vencimentos = data.get("vencimentos", [])

        if not vencimentos:
            return "✅ Você não tem contas vencendo hoje!"

        msg = "📅 *Vencimentos Hoje*\n\n"
        total = 0

        for v in vencimentos:
            msg += f"• {v['descricao']}: {v['valor_formatado']}\n"
            total += v['valor']

        msg += f"\n💰 Total: {total}"
        return msg


class VencimentosAmanhaIntent(BaseIntent):
    """
    Handler para intent 'Vencimentos Amanhã'.

    Consulta contas que vencem amanhã.
    """

    def extract_params(self) -> Dict[str, Any]:
        return {}

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "VencimentosAmanhaIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        vencimentos = data.get("vencimentos", [])

        if not vencimentos:
            return "✅ Você não tem contas vencendo amanhã!"

        msg = "📅 *Vencimentos Amanhã*\n\n"
        for v in vencimentos:
            msg += f"• {v['descricao']}: {v['valor_formatado']}\n"

        return msg


class VencimentosSemanaIntent(BaseIntent):
    """
    Handler para intent 'Vencimentos Essa Semana'.

    Consulta contas que vencem nos próximos 7 dias.
    """

    def extract_params(self) -> Dict[str, Any]:
        return {}

    def validate(self) -> str | None:
        return None

    def execute(self) -> Dict[str, Any]:
        # TODO: Implementar via finance_service
        raise NotImplementedError(
            "VencimentosSemanaIntent ainda não implementado."
        )

    def format_response(self, data: Dict[str, Any]) -> str:
        vencimentos = data.get("vencimentos", [])

        if not vencimentos:
            return "✅ Você não tem contas vencendo essa semana!"

        msg = "📅 *Vencimentos - Próximos 7 Dias*\n\n"

        # Agrupar por dia
        por_dia = {}
        for v in vencimentos:
            dia = v['data_vencimento']
            if dia not in por_dia:
                por_dia[dia] = []
            por_dia[dia].append(v)

        for dia, contas in sorted(por_dia.items()):
            msg += f"*{dia}*\n"
            for v in contas:
                msg += f"  • {v['descricao']}: {v['valor_formatado']}\n"
            msg += "\n"

        return msg


class ContasAtrasadasIntent(BaseIntent):
    """
    Handler para intent 'Contas Atrasadas'.

    Consulta agendamentos e faturas pendentes que já passaram do vencimento.
    Considera apenas os últimos 30 dias para evitar débitos muito antigos.

    Exemplo de mensagem:
    - "Tenho alguma conta atrasada?"
    - "Contas vencidas"
    - "O que já passou do vencimento?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Não requer parâmetros."""
        return {}

    def validate(self) -> str | None:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """
        Busca contas e faturas atrasadas.

        REFATORADO (2026-01-11): Usa NightlyCheckinService.collect_financial_snapshot()
        para garantir consistência com o job noturno.
        """
        from app.services.nightly_checkin_service import NightlyCheckinService
        from datetime import date

        hoje = date.today()

        # Usar método centralizado (mesma fonte de dados do job noturno)
        snapshot = NightlyCheckinService.collect_financial_snapshot(
            self.conn, self.usuario_id, hoje
        )

        return {
            "snapshot": snapshot,
            "hoje": hoje
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """
        Formata lista de contas atrasadas para WhatsApp.

        REFATORADO (2026-01-11): Usa NightlyCheckinService.format_consolidated_checkin_message()
        com checkin_id=None (modo read-only) para garantir formatação idêntica ao job noturno.
        """
        from app.services.nightly_checkin_service import NightlyCheckinService

        snapshot = data["snapshot"]

        # Verificar se há dados para mostrar
        if (not snapshot['pending_bills'] and
            not snapshot['overdue_bills'] and
            not snapshot['overdue_invoices'] and
            not snapshot['invoices_due_today']):
            return "✅ Você não tem contas atrasadas! Tudo em dia! 🎉"

        # Usar método centralizado de formatação (modo read-only: checkin_id=None)
        mensagem = NightlyCheckinService.format_consolidated_checkin_message(
            pending_bills=snapshot['pending_bills'],
            overdue_bills=snapshot['overdue_bills'],
            bills_due_today=[],  # Intent não usa isso
            overdue_invoices=snapshot['overdue_invoices'],
            faturas_vencendo_hoje=snapshot['invoices_due_today'],
            checkin_id=None  # Modo read-only: sem sessão Redis
        )

        # Se format_consolidated_checkin_message retornar None (sem dados), retornar mensagem positiva
        if not mensagem:
            return "✅ Você não tem contas atrasadas! Tudo em dia! 🎉"

        return mensagem


__all__ = [
    'ConfigurarNotificacoesIntent',
    'VencimentosHojeIntent',
    'VencimentosAmanhaIntent',
    'VencimentosSemanaIntent',
    'ContasAtrasadasIntent',
]
