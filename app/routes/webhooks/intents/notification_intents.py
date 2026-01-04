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
        """Busca contas e faturas atrasadas."""
        from app.services.nightly_checkin_service import NightlyCheckinService
        from app.services.queries import AgendamentosQueries, FaturasQueries
        from datetime import date

        hoje = date.today()

        # Buscar agendamentos atrasados usando query centralizada
        # Ajustar parâmetros para buscar TODAS as contas atrasadas (não só +7 dias)
        sql_contas = AgendamentosQueries.get_contas_atrasadas_com_data_real()
        params_contas = AgendamentosQueries.get_parametros_padrao(self.usuario_id, hoje)
        # Ajustar data_maxima para buscar TODAS as contas atrasadas (não filtrar por dias)
        params_contas["data_maxima"] = hoje  # Inclui todas até hoje

        contas_result = self.conn.execute(sql_contas, params_contas).fetchall()
        contas_atrasadas = [dict(row._mapping) for row in contas_result]

        # Buscar faturas vencidas usando query centralizada
        sql_faturas = FaturasQueries.get_faturas_vencidas()
        params_faturas = FaturasQueries.get_parametros_padrao(self.usuario_id, hoje)

        faturas_result = self.conn.execute(sql_faturas, params_faturas).fetchall()
        faturas_atrasadas = [dict(row._mapping) for row in faturas_result]

        return {
            "contas_atrasadas": contas_atrasadas,
            "faturas_atrasadas": faturas_atrasadas,
            "hoje": hoje
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata lista de contas atrasadas para WhatsApp."""
        from app.utils import formatar_moeda
        from app.services.nightly_checkin_service import NightlyCheckinService

        contas = data["contas_atrasadas"]
        faturas = data["faturas_atrasadas"]
        hoje = data["hoje"]

        # Separar receitas de despesas
        despesas = [c for c in contas if c.get('nome_grupo') == 'Despesa']
        receitas = [c for c in contas if c.get('nome_grupo') == 'Renda']

        # Se não há nada atrasado
        if not despesas and not faturas and not receitas:
            return "✅ Você não tem contas atrasadas! Tudo em dia! 🎉"

        msg = ""

        # Seção 1: DESPESAS ATRASADAS (contas a pagar)
        if despesas or faturas:
            msg += "🔴 *CONTAS ATRASADAS*\n\n"

            # Agrupar despesas por dia de vencimento
            contas_por_dia = {}
            for conta in despesas:
                dia = conta['dia_execucao']
                if dia not in contas_por_dia:
                    contas_por_dia[dia] = []
                contas_por_dia[dia].append(conta)

            # Listar despesas agrupadas
            total_despesas = 0
            for dia in sorted(contas_por_dia.keys(), reverse=True):
                # Calcular dias de atraso
                dias_atraso = NightlyCheckinService.calculate_days_overdue(dia, hoje)

                if dias_atraso == 1:
                    msg += "*Venceu ontem*\n"
                elif dias_atraso <= 7:
                    msg += f"*Venceu dia {dia:02d}* ({dias_atraso} dias atrás)\n"
                else:
                    msg += f"*Venceu dia {dia:02d}* ({dias_atraso} dias atrás) ⚠️\n"

                for conta in contas_por_dia[dia]:
                    valor = conta['valor_previsto'] or 0
                    total_despesas += valor
                    msg += f"💸 {conta['descricao']} - {formatar_moeda(valor)}\n"

            msg += "\n"

            # Faturas atrasadas
            total_faturas = 0
            if faturas:
                msg += "*💳 Faturas Vencidas:*\n"
                for fatura in faturas:
                    valor = fatura['valor_fatura'] or 0
                    total_faturas += valor
                    data_venc = fatura['data_vencimento']
                    dias_atraso = (hoje - data_venc).days

                    msg += f"• {fatura['cartao']} - {formatar_moeda(valor)}\n"
                    msg += f"  Venceu em {data_venc.strftime('%d/%m')} ({dias_atraso} dias)\n"
                msg += "\n"

            # Totais de despesas
            msg += "━━━━━━━━━━━━━━\n"
            total_despesas_geral = total_despesas + total_faturas
            msg += f"💸 *Total Despesas:* {formatar_moeda(total_despesas_geral)}\n"
            msg += f"⚠️ *{len(despesas) + len(faturas)} {'conta' if len(despesas) + len(faturas) == 1 else 'contas'} atrasada{'s' if len(despesas) + len(faturas) != 1 else ''}*\n\n"

        # Seção 2: RECEITAS PENDENTES (dinheiro que você ainda não recebeu)
        if receitas:
            msg += "💵 *RECEITAS PENDENTES*\n"
            msg += "_Valores previstos que ainda não foram recebidos_\n\n"

            # Agrupar receitas por dia de vencimento
            receitas_por_dia = {}
            for conta in receitas:
                dia = conta['dia_execucao']
                if dia not in receitas_por_dia:
                    receitas_por_dia[dia] = []
                receitas_por_dia[dia].append(conta)

            # Listar receitas agrupadas
            total_receitas = 0
            for dia in sorted(receitas_por_dia.keys(), reverse=True):
                dias_atraso = NightlyCheckinService.calculate_days_overdue(dia, hoje)
                msg += f"*Previsto dia {dia:02d}* (há {dias_atraso} dias)\n"

                for conta in receitas_por_dia[dia]:
                    valor = conta['valor_previsto'] or 0
                    total_receitas += valor
                    msg += f"💵 {conta['descricao']} - {formatar_moeda(valor)}\n"

                msg += "\n"

            msg += "━━━━━━━━━━━━━━\n"
            msg += f"💰 *Total Receitas Pendentes:* {formatar_moeda(total_receitas)}\n"
            msg += f"ℹ️ *{len(receitas)} receita{'s' if len(receitas) != 1 else ''} aguardando confirmação*"

        return msg


__all__ = [
    'ConfigurarNotificacoesIntent',
    'VencimentosHojeIntent',
    'VencimentosAmanhaIntent',
    'VencimentosSemanaIntent',
    'ContasAtrasadasIntent',
]
