# app/presentation/admin/notification_triggers.py
"""
Módulo para triggers de notificações automáticas (cron jobs).

⚠️ CRÍTICO: Este módulo contém rotas chamadas por serviços externos (UptimeRobot, Ofelia).
Qualquer alteração nas URLs ou autenticação pode quebrar automações.

Este módulo contém endpoints de trigger para:
- Notificações de agenda diária
- Notificações de contas a vencer
- Resumos matinais (Daily Briefing)
- Relatórios mensais (início e fim do mês)
"""

from flask import Blueprint, request
from sqlalchemy import text
from datetime import date, time

from app.shared.decorators import require_api_key, handle_errors
from app.shared.responses import ApiResponse
from ._common import (
    db_engine,
    API_SECRET_KEY,
    BOT_WHATSAPP_URL
)

notification_triggers_bp = Blueprint('admin_notification_triggers', __name__)


@notification_triggers_bp.route('/trigger-agenda-notifications', methods=['POST'])
@require_api_key
@handle_errors(tag="AGENDA-NOTIF")
def trigger_agenda_notifications():
    """
    Endpoint para UptimeRobot disparar notificações de agenda diária.

    ⚠️ CRÍTICO: Esta rota é chamada por cron job externo.

    UptimeRobot deve chamar a cada hora:
    POST https://seu-backend.onrender.com/admin/trigger-agenda-notifications
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.notification_processor_service import NotificationProcessorService

    resultado = NotificationProcessorService.processar_agenda_diaria(
        BOT_WHATSAPP_URL,
        API_SECRET_KEY
    )

    return ApiResponse.success(
        "Processamento de agenda diária concluído",
        **resultado
    )


@notification_triggers_bp.route('/trigger-bills-notifications', methods=['POST'])
@require_api_key
@handle_errors(tag="BILLS-NOTIF")
def trigger_bills_notifications():
    """
    Endpoint para UptimeRobot disparar notificações de contas a vencer.

    ⚠️ CRÍTICO: Esta rota é chamada por cron job externo.

    UptimeRobot deve chamar a cada hora:
    POST https://seu-backend.onrender.com/admin/trigger-bills-notifications
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.notification_processor_service import NotificationProcessorService

    resultado = NotificationProcessorService.processar_contas_vencer(
        BOT_WHATSAPP_URL,
        API_SECRET_KEY
    )

    return ApiResponse.success(
        "Processamento de contas a vencer concluído",
        **resultado
    )


@notification_triggers_bp.route('/trigger-daily-briefing', methods=['POST'])
@require_api_key
@handle_errors(tag="DAILY-BRIEFING")
def trigger_daily_briefing():
    """
    Endpoint para cron job disparar resumos matinais.

    ⚠️ CRÍTICO: Esta rota é chamada por cron job externo.

    Deve ser chamado a cada hora pelo UptimeRobot/cron:
    POST https://seu-backend.onrender.com/admin/trigger-daily-briefing
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.notification_config_service import NotificationConfigService
    from app.services.daily_briefing_service import DailyBriefingService
    from app.services.gemini_service import generate_daily_briefing
    from app.services.notification_service import enviar_notificacao_whatsapp
    from datetime import datetime

    hora_atual = datetime.now().time().replace(second=0, microsecond=0)

    print(f"[DAILY-BRIEFING] Processando resumos matinais para horario {hora_atual.strftime('%H:%M')}...")

    # Buscar usuários que devem receber neste horário (resumo matinal OU alertas financeiros)
    usuarios = NotificationConfigService.get_users_with_notifications_active(hora_atual)

    if not usuarios:
        return ApiResponse.success(
            f"Nenhum usuario configurado para {hora_atual.strftime('%H:%M')}",
            usuarios_processados=0,
            enviados_sucesso=0,
            horario=hora_atual.strftime('%H:%M')
        )

    briefing_service = DailyBriefingService()
    enviados = 0
    erros = 0

    for usuario_id, numero_whatsapp in usuarios:
        try:
            # Preparar dados
            briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

            if not briefing_data:
                print(f"[DAILY-BRIEFING] Erro ao preparar dados para usuario {usuario_id}")
                erros += 1
                continue

            # Gerar mensagem
            if briefing_data['total_eventos'] == 0:
                mensagem = briefing_service.generate_briefing_message(usuario_id, date.today())
            else:
                mensagem = generate_daily_briefing(briefing_data)

            if not mensagem:
                print(f"[DAILY-BRIEFING] Falha ao gerar mensagem para usuario {usuario_id}")
                erros += 1
                continue

            # Enviar
            sucesso = enviar_notificacao_whatsapp(
                numero_whatsapp,
                mensagem,
                BOT_WHATSAPP_URL,
                API_SECRET_KEY
            )

            if sucesso:
                enviados += 1
                print(f"[DAILY-BRIEFING] Resumo enviado para usuario {usuario_id}")
            else:
                erros += 1
                print(f"[DAILY-BRIEFING] Falha ao enviar para usuario {usuario_id}")

        except Exception as e:
            erros += 1
            print(f"[DAILY-BRIEFING] Erro ao processar usuario {usuario_id}: {e}")

    return ApiResponse.success(
        "Processamento de resumos matinais concluído",
        usuarios_processados=len(usuarios),
        enviados_sucesso=enviados,
        erros=erros,
        horario=hora_atual.strftime('%H:%M')
    )


@notification_triggers_bp.route('/trigger-monthly-reports-inicio', methods=['POST'])
@require_api_key
@handle_errors(tag="MONTHLY-REPORT-INICIO")
def trigger_monthly_reports_inicio():
    """
    Endpoint para cron job disparar relatórios mensais no início do mês.
    Relatório refere-se ao MÊS ANTERIOR.

    ⚠️ CRÍTICO: Esta rota é chamada por cron job externo.

    Cron job deve chamar a cada hora no dia 1:
    POST https://seu-backend.onrender.com/admin/trigger-monthly-reports-inicio
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.monthly_report_processor_service import processar_relatorios_mensais

    print("[MONTHLY-REPORT] 📊 Processando relatórios de INICIO DO MES...")

    resultado = processar_relatorios_mensais(
        momento_envio='INICIO_MES',
        janela_minutos=5
    )

    print(f"[MONTHLY-REPORT] ✅ Processamento concluído: {resultado['enviados_sucesso']} enviados")

    return ApiResponse.success(
        "Processamento de relatórios mensais (INICIO_MES) concluído",
        **resultado
    )


@notification_triggers_bp.route('/trigger-monthly-reports-fim', methods=['POST'])
@require_api_key
@handle_errors(tag="MONTHLY-REPORT-FIM")
def trigger_monthly_reports_fim():
    """
    Endpoint para cron job disparar relatórios mensais no fim do mês.
    Relatório refere-se ao MÊS ATUAL.

    ⚠️ CRÍTICO: Esta rota é chamada por cron job externo.

    Cron job deve chamar a cada hora no último dia do mês:
    POST https://seu-backend.onrender.com/admin/trigger-monthly-reports-fim
    Header: x-api-key: SUA_SECRET_KEY
    """
    from app.services.monthly_report_processor_service import processar_relatorios_mensais

    print("[MONTHLY-REPORT] 📊 Processando relatórios de FIM DO MES...")

    resultado = processar_relatorios_mensais(
        momento_envio='FIM_MES',
        janela_minutos=5
    )

    print(f"[MONTHLY-REPORT] ✅ Processamento concluído: {resultado['enviados_sucesso']} enviados")

    return ApiResponse.success(
        "Processamento de relatórios mensais (FIM_MES) concluído",
        **resultado
    )
