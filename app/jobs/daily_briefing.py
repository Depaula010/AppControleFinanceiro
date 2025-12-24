#!/usr/bin/env python3
"""
Processador de Resumo Matinal (Daily Briefing) + Alertas Financeiros
Executado via cron job para enviar resumo inteligente da agenda e alertas de contas/faturas
"""

import os
import sys
from datetime import datetime, time, date

# Adicionar diretório raiz ao path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from app.jobs.base_job import BaseJob


def format_financial_alerts_standalone(alertas_data):
    """
    Formata alertas financeiros para mensagem independente (quando resumo está desativado).
    Inclui saudação e contexto completo.

    Args:
        alertas_data: dict com contas_hoje, contas_amanha, faturas_hoje, faturas_amanha

    Returns:
        str: Mensagem formatada ou None
    """
    from app.shared.formatters.financial_alert_formatter import FinancialAlertFormatter
    return FinancialAlertFormatter.format(alertas_data, include_greeting=True)


def montar_mensagem_unificada(resumo_componente, alertas_componente, config):
    """
    Monta mensagem final baseado nos componentes disponíveis.

    Args:
        resumo_componente: Mensagem do resumo matinal (ou None)
        alertas_componente: Mensagem dos alertas financeiros (ou None)
        config: Configurações do usuário

    Returns:
        str: Mensagem final ou None
    """
    resumo_ativo = config['resumo_matinal_ativo']
    alertas_ativos = config['alertas_financeiros_ativos']

    if resumo_ativo and alertas_ativos:
        # CASO 1: Ambos ativos
        if resumo_componente:
            # Alertas já estão incluídos no resumo (prepare_briefing_data)
            return resumo_componente
        elif alertas_componente:
            # Não há eventos, mas há alertas
            return alertas_componente

    elif resumo_ativo and resumo_componente:
        # CASO 2: Apenas resumo ativo
        return resumo_componente

    elif alertas_ativos and alertas_componente:
        # CASO 3: Apenas alertas ativos
        return alertas_componente

    return None


class DailyBriefingJob(BaseJob):
    """Job para processar resumo matinal e alertas financeiros."""

    def get_job_name(self) -> str:
        return "RESUMO-MATINAL"

    def execute(self):
        """
        Envia resumo matinal e/ou alertas financeiros para usuários configurados.
        Executado dentro do Flask app context.
        """
        from app.services.notification_config_service import NotificationConfigService
        from app.services.daily_briefing_service import DailyBriefingService
        from app.services.gemini_service import generate_daily_briefing
        from app.services.finance_service import get_upcoming_bills_and_invoices
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app import db_engine
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        # Obter hora atual (zerando segundos para bater com o banco)
        hora_atual = datetime.now().time().replace(second=0, microsecond=0)

        self._log(f"Buscando usuários para notificar às {hora_atual.strftime('%H:%M')}")

        # Buscar usuários com resumo matinal OU alertas financeiros ativos
        usuarios = NotificationConfigService.get_users_with_notifications_active(hora_atual)

        if not usuarios:
            self._log(f"Nenhum usuário configurado para este horário ({hora_atual})")
            return

        self._log(f"{len(usuarios)} usuário(s) encontrado(s)")

        # Inicializar serviço
        briefing_service = DailyBriefingService()

        # Processar cada usuário
        for usuario_id, numero_whatsapp in usuarios:
            try:
                self._log(f"Processando usuário {usuario_id}...")

                # Obter configurações do usuário
                config = NotificationConfigService.get_or_create_config(usuario_id)

                # Preparar componentes da mensagem
                resumo_componente = None
                alertas_componente = None

                # 1. Buscar resumo matinal (se ativo)
                if config['resumo_matinal_ativo']:
                    self._log(f"Preparando resumo matinal para usuário {usuario_id}...")
                    briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

                    if not briefing_data:
                        self._log(f"Erro ao preparar dados para usuário {usuario_id}", level="ERROR")
                    elif briefing_data.get('total_eventos', 0) > 0:
                        # Gerar resumo completo com IA
                        self._log(f"Gerando resumo com IA para usuário {usuario_id}...")
                        resumo_componente = generate_daily_briefing(briefing_data)
                    else:
                        # Mensagem simples sem eventos (mas pode incluir alertas se config ativa)
                        self._log(f"Sem eventos para usuário {usuario_id}. Gerando mensagem básica.")
                        resumo_componente = briefing_service.generate_briefing_message(usuario_id, date.today())

                # 2. Buscar alertas financeiros (se ativo E resumo não está ativo)
                # Se resumo está ativo, alertas já estão incluídos no resumo
                if config['alertas_financeiros_ativos'] and not config['resumo_matinal_ativo']:
                    self._log(f"Buscando alertas financeiros para usuário {usuario_id}...")

                    with db_engine.connect() as conn:
                        alertas_data = get_upcoming_bills_and_invoices(conn, usuario_id, date.today())

                    # Verificar se há alertas (hoje ou amanhã)
                    tem_alertas = any([
                        alertas_data['contas_hoje'],
                        alertas_data['contas_amanha'],
                        alertas_data['faturas_hoje'],
                        alertas_data['faturas_amanha']
                    ])

                    if tem_alertas:
                        alertas_componente = format_financial_alerts_standalone(alertas_data)
                    else:
                        self._log(f"Sem alertas financeiros para usuário {usuario_id}")

                # 3. Montar mensagem final
                mensagem = montar_mensagem_unificada(
                    resumo_componente,
                    alertas_componente,
                    config
                )

                if not mensagem:
                    self._log(f"Nenhuma mensagem para enviar ao usuário {usuario_id}")
                    continue

                # Enviar via WhatsApp
                enviar_notificacao_whatsapp(
                    numero_whatsapp,
                    mensagem,
                    BOT_WHATSAPP_URL,
                    API_SECRET_KEY
                )

                self._log(f"Mensagem enviada para usuário {usuario_id}")

            except Exception as e_user:
                self._log(f"Erro ao processar usuário {usuario_id}: {e_user}", level="ERROR")
                import traceback
                traceback.print_exc()
                continue

        self._log("Processamento finalizado")


if __name__ == "__main__":
    job = DailyBriefingJob()
    exit_code = job.run()
    sys.exit(exit_code)
