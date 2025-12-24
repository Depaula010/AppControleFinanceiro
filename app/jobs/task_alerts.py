#!/usr/bin/env python3
"""
Script de cronjob para processar alertas de tarefas do Google Calendar.
Executado a cada minuto pelo Ofelia.

Fluxo:
1. Busca todos os usuários com alertas ativos
2. Para cada usuário, verifica eventos próximos (conforme minutos_antes configurado)
3. Envia alertas via WhatsApp
"""

import sys
import os
from datetime import datetime
from zoneinfo import ZoneInfo

# Adicionar diretório raiz ao path
current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from app.jobs.base_job import BaseJob

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


class TaskAlertsJob(BaseJob):
    """Job para processar alertas de tarefas do Google Calendar."""

    def get_job_name(self) -> str:
        return "ALERTAS-TAREFAS"

    def execute(self):
        """
        Processa alertas de tarefas do Google Calendar para todos os usuários ativos.
        Executado dentro do Flask app context.
        """
        from app.services.calendar_alert_config_service import CalendarAlertConfigService
        from app.services.calendar_alert_service import CalendarAlertService

        # Buscar usuários com alertas ativos
        usuarios = CalendarAlertConfigService.get_users_with_alerts_active()

        if not usuarios:
            self._log("Nenhum usuário com alertas ativos")
            return

        self._log(f"Processando {len(usuarios)} usuário(s)")

        total_alertas = 0

        for usuario_id, numero_whatsapp, minutos_antes in usuarios:
            self._log(f"Processando usuário {usuario_id} ({numero_whatsapp})")
            self._log(f"Configuração: {minutos_antes} minuto(s) antes")

            # Processar alertas para o usuário
            alertas_enviados = CalendarAlertService.process_alerts_for_user(
                usuario_id=usuario_id,
                numero_whatsapp=numero_whatsapp,
                minutos_antes=minutos_antes
            )

            total_alertas += alertas_enviados

        self._log(f"Total de alertas enviados: {total_alertas}")


if __name__ == "__main__":
    job = TaskAlertsJob()
    exit_code = job.run()
    sys.exit(exit_code)
