# app/routes/webhooks/intents/notification_intents.py
"""
Intent handlers para notificações e vencimentos.

Permite usuários:
- Configurarem notificações automáticas via WhatsApp
- Consultarem vencimentos de contas (hoje, amanhã, semana)
- Verificarem contas atrasadas
"""

from typing import Dict, Any, Optional
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from .base_intent import BaseIntent
from app.services.finance_service import get_vencimentos_periodo, format_vencimentos_message


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
    - "Tenho conta que vence hoje?"
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        """Sem validação necessária."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca vencimentos de hoje."""
        hoje = datetime.now(self.TIMEZONE_BR).date()

        vencimentos = get_vencimentos_periodo(
            conn=self.conn,
            usuario_id=self.usuario_id,
            data_inicio=hoje,
            data_fim=hoje
        )

        return {
            "vencimentos": vencimentos,
            "data_referencia": hoje,
            "periodo": "HOJE"
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta usando função centralizada."""
        return format_vencimentos_message(
            vencimentos=data["vencimentos"],
            periodo=data["periodo"],
            data_referencia=data["data_referencia"]
        )


class VencimentosAmanhaIntent(BaseIntent):
    """
    Handler para intent 'Vencimentos Amanhã'.

    Consulta contas que vencem amanhã.

    Exemplo de mensagem:
    - "O que vence amanhã?"
    - "Contas de amanhã"
    - "Tenho conta que vence amanhã?"
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca vencimentos de amanhã."""
        hoje = datetime.now(self.TIMEZONE_BR).date()
        amanha = hoje + timedelta(days=1)

        vencimentos = get_vencimentos_periodo(
            conn=self.conn,
            usuario_id=self.usuario_id,
            data_inicio=amanha,
            data_fim=amanha
        )

        return {
            "vencimentos": vencimentos,
            "data_referencia": amanha,
            "periodo": "AMANHÃ"
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta usando função centralizada."""
        return format_vencimentos_message(
            vencimentos=data["vencimentos"],
            periodo=data["periodo"],
            data_referencia=data["data_referencia"]
        )


class VencimentosSemanaIntent(BaseIntent):
    """
    Handler para intent 'Vencimentos Essa Semana'.

    Consulta contas que vencem nos próximos 7 dias.

    Exemplo de mensagem:
    - "O que vence essa semana?"
    - "Contas que vencem essa semana"
    - "Vencimentos dos próximos dias"
    """

    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca vencimentos dos próximos 7 dias."""
        hoje = datetime.now(self.TIMEZONE_BR).date()
        fim_semana = hoje + timedelta(days=7)

        vencimentos = get_vencimentos_periodo(
            conn=self.conn,
            usuario_id=self.usuario_id,
            data_inicio=hoje,
            data_fim=fim_semana
        )

        return {
            "vencimentos": vencimentos,
            "data_referencia": hoje,
            "periodo": "NOS PRÓXIMOS 7 DIAS"
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata resposta usando função centralizada."""
        return format_vencimentos_message(
            vencimentos=data["vencimentos"],
            periodo=data["periodo"],
            data_referencia=data["data_referencia"]
        )


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
