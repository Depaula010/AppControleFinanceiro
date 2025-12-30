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
        from datetime import date, timedelta
        from sqlalchemy import text
        import calendar

        hoje = date.today()

        # Buscar agendamentos pendentes dos últimos 30 dias
        data_minima = hoje - timedelta(days=30)

        # Correção Bug #9: Reescreve SQL com comparação de datas adequada usando CTE
        # Problema: A query antiga comparava apenas números de dias (1-31), não datas completas
        # Exemplo do erro: Conta com vencimento dia 25 de novembro não aparecia como atrasada
        # no dia 30 de dezembro porque comparava 25 < 30 (só os dias, sem considerar o mês)
        # Solução: Construir datas completas (ano-mês-dia) para comparação correta
        sql_contas = text("""
            WITH ExpectedDates AS (
                SELECT
                    a.*,
                    c.nome_conta, c.tipo_conta,
                    s.nome_sub as categoria,
                    m.nome_macro,
                    g.nome_grupo,
                    -- Constrói a data esperada completa para o mês atual
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :hoje) + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :hoje) + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :hoje) + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_atual,
                    -- Constrói a data esperada completa para o mês anterior
                    CASE
                        WHEN a.dia_execucao <= EXTRACT(DAY FROM (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day'))
                        THEN (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 day' * (a.dia_execucao - 1))::date
                        ELSE (DATE_TRUNC('month', :hoje - INTERVAL '1 month') + INTERVAL '1 month - 1 day')::date
                    END as data_esperada_mes_anterior
                FROM Agendamentos a
                JOIN Contas c ON a.conta_id = c.id
                JOIN SubCategoria s ON a.subcategoria_id = s.id
                JOIN MacroCategoria m ON s.macro_id = m.id
                JOIN GrupoCategoria g ON m.grupo_id = g.id
                WHERE a.usuario_id = :uid
                  AND a.ativo = TRUE
                  AND a.tipo_agendamento IN ('FIXO', 'LEMBRETE_VARIAVEL')
            )
            SELECT
                ed.id, ed.descricao, ed.valor_previsto, ed.dia_execucao,
                ed.conta_id, ed.subcategoria_id, ed.usuario_id,
                ed.nome_conta, ed.tipo_conta, ed.categoria,
                ed.nome_macro, ed.nome_grupo,
                COALESCE(ed.data_esperada_mes_atual, ed.data_esperada_mes_anterior) as data_vencimento_real
            FROM ExpectedDates ed
            WHERE (
                -- Mês atual está atrasado
                (ed.data_esperada_mes_atual < :hoje
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND DATE_TRUNC('month', t.data_transacao) = DATE_TRUNC('month', :hoje)
                       AND DATE_TRUNC('year', t.data_transacao) = DATE_TRUNC('year', :hoje)
                 ))
                OR
                -- Mês anterior está atrasado
                (ed.data_esperada_mes_anterior < :hoje
                 AND ed.data_esperada_mes_anterior >= :data_minima
                 AND NOT EXISTS (
                     SELECT 1 FROM Transacoes t
                     WHERE t.descricao = ed.descricao
                       AND t.usuario_id = ed.usuario_id
                       AND DATE_TRUNC('month', t.data_transacao) = DATE_TRUNC('month', ed.data_esperada_mes_anterior)
                       AND DATE_TRUNC('year', t.data_transacao) = DATE_TRUNC('year', ed.data_esperada_mes_anterior)
                 ))
            )
            -- Aplica filtro para agendamentos anuais se necessário
            AND (
                ed.periodicidade != 'ANUAL'
                OR (ed.periodicidade = 'ANUAL' AND ed.mes_execucao = EXTRACT(MONTH FROM :hoje))
            )
            ORDER BY data_vencimento_real DESC, ed.nome_grupo, ed.descricao
        """)

        contas_result = self.conn.execute(sql_contas, {
            "uid": self.usuario_id,
            "hoje": hoje,
            "data_minima": data_minima
        }).fetchall()

        contas_atrasadas = [dict(row._mapping) for row in contas_result]

        # Buscar faturas vencidas
        sql_faturas = text("""
            SELECT
                c.nome_conta as cartao,
                f.data_vencimento,
                f.status,
                COALESCE(SUM(CASE WHEN t.valor < 0 THEN ABS(t.valor) ELSE 0 END), 0) as valor_fatura
            FROM Faturas f
            JOIN Contas c ON f.conta_id = c.id
            LEFT JOIN Transacoes t ON f.id = t.fatura_id
            WHERE c.usuario_id = :uid
              AND f.status = 'Aberta'
              AND f.data_vencimento < :hoje
              AND f.data_vencimento >= :limite_inferior
            GROUP BY c.nome_conta, f.data_vencimento, f.status
            ORDER BY f.data_vencimento DESC
        """)

        limite_inferior = hoje - timedelta(days=30)

        faturas_result = self.conn.execute(sql_faturas, {
            "uid": self.usuario_id,
            "hoje": hoje,
            "limite_inferior": limite_inferior
        }).fetchall()

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

        # Se não há nada atrasado
        if not contas and not faturas:
            return "✅ Você não tem contas atrasadas! Tudo em dia! 🎉"

        msg = "🔴 *CONTAS ATRASADAS*\n\n"

        # Agrupar contas por dia de vencimento
        contas_por_dia = {}
        for conta in contas:
            dia = conta['dia_execucao']
            if dia not in contas_por_dia:
                contas_por_dia[dia] = []
            contas_por_dia[dia].append(conta)

        # Listar contas agrupadas
        total_contas = 0
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
                total_contas += valor

                # Emoji baseado no tipo/grupo
                tipo_emoji = "💰"
                if conta.get('nome_grupo'):
                    if 'Renda' in conta['nome_grupo']:
                        tipo_emoji = "💵"
                    elif 'Despesa' in conta['nome_grupo']:
                        tipo_emoji = "💸"

                msg += f"{tipo_emoji} {conta['descricao']} - {formatar_moeda(valor)}\n"

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

        # Totais
        msg += "━━━━━━━━━━━━━━\n"
        total_geral = total_contas + total_faturas
        msg += f"💰 *Total:* {formatar_moeda(total_geral)}\n"

        num_contas = len(contas)
        num_faturas = len(faturas)
        total_items = num_contas + num_faturas

        msg += f"⚠️ *{total_items} {'conta' if total_items == 1 else 'contas'} pendente{'s' if total_items != 1 else ''}*"

        return msg


__all__ = [
    'ConfigurarNotificacoesIntent',
    'VencimentosHojeIntent',
    'VencimentosAmanhaIntent',
    'VencimentosSemanaIntent',
    'ContasAtrasadasIntent',
]
