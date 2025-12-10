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

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from datetime import datetime
from zoneinfo import ZoneInfo

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

def processar_alertas_tarefas():
    """
    Processa alertas de tarefas do Google Calendar para todos os usuários ativos.
    """
    print("\n" + "="*60)
    print("INICIANDO PROCESSAMENTO DE ALERTAS DE TAREFAS")
    print(f"Data/Hora: {datetime.now(TIMEZONE_BR).strftime('%d/%m/%Y %H:%M:%S')}")
    print("="*60)

    try:
        # IMPORTANTE: Criar instância da aplicação para acessar o Banco
        from app import create_app
        app = create_app()

        # Entrar no contexto da aplicação
        with app.app_context():
            # Importar serviços (agora com acesso ao DB garantido)
            from app.services.calendar_alert_config_service import CalendarAlertConfigService
            from app.services.calendar_alert_service import CalendarAlertService

            # Buscar usuários com alertas ativos
            usuarios = CalendarAlertConfigService.get_users_with_alerts_active()

            if not usuarios:
                print("[ALERTAS-TAREFAS] ℹ️  Nenhum usuário com alertas ativos")
                return

            print(f"[ALERTAS-TAREFAS] Processando {len(usuarios)} usuário(s)")

            total_alertas = 0

            for usuario_id, numero_whatsapp, minutos_antes in usuarios:
                print(f"\n[ALERTAS-TAREFAS] Processando usuário {usuario_id} ({numero_whatsapp})")
                print(f"[ALERTAS-TAREFAS] Configuração: {minutos_antes} minuto(s) antes")

                # Processar alertas para o usuário
                alertas_enviados = CalendarAlertService.process_alerts_for_user(
                    usuario_id=usuario_id,
                    numero_whatsapp=numero_whatsapp,
                    minutos_antes=minutos_antes
                )

                total_alertas += alertas_enviados

            print("\n" + "="*60)
            print(f"[ALERTAS-TAREFAS] ✅ Processamento concluído")
            print(f"[ALERTAS-TAREFAS] Total de alertas enviados: {total_alertas}")
            print("="*60 + "\n")

    except Exception as e:
        print(f"\n[ALERTAS-TAREFAS] ❌ Erro no processamento: {e}")
        import traceback
        traceback.print_exc()
        sys.exit(1)

if __name__ == "__main__":
    processar_alertas_tarefas()
