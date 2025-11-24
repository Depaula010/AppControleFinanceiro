#!/usr/bin/env python3
"""
Processador de Resumo Matinal (Daily Briefing)
Executado via cron job para enviar resumo inteligente da agenda
"""

import os
import sys
from datetime import datetime, time, date

# Adicionar diretório raiz ao path para encontrar o módulo 'app'
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

def processar_resumo_matinal():
    """
    Função principal que o Cron Job vai rodar.
    Envia resumo matinal para usuários com a notificação ativada.
    """
    print(f"[RESUMO-MATINAL] Início do processamento - {datetime.now()}")

    try:
        # 1. IMPORTANTE: Criar instância da aplicação para acessar o Banco
        from app import create_app
        app = create_app()
        
        # 2. Entrar no contexto da aplicação
        with app.app_context():
            
            # Importar serviços (agora com acesso ao DB garantido)
            from app.services.notification_config_service import NotificationConfigService
            from app.services.daily_briefing_service import DailyBriefingService
            from app.services.gemini_service import generate_daily_briefing
            from app.services.notification_service import enviar_notificacao_whatsapp
            from app.config import Config

            # Obter hora atual (zerando segundos para bater com o banco)
            hora_atual = datetime.now().time().replace(second=0, microsecond=0)

            print(f"[RESUMO-MATINAL] Buscando usuários para notificar às {hora_atual.strftime('%H:%M')}")

            # Buscar usuários que devem receber notificação nesta hora
            usuarios = NotificationConfigService.get_users_with_resumo_matinal_active(hora_atual)

            if not usuarios:
                print(f"[RESUMO-MATINAL] Nenhum usuário configurado para este horário ({hora_atual})")
                return

            print(f"[RESUMO-MATINAL] {len(usuarios)} usuário(s) encontrado(s)")

            # Inicializar serviço
            briefing_service = DailyBriefingService()

            # Processar cada usuário
            for usuario_id, numero_whatsapp in usuarios:
                try:
                    print(f"[RESUMO-MATINAL] Processando usuário {usuario_id}...")

                    # Preparar dados do resumo
                    briefing_data = briefing_service.prepare_briefing_data(usuario_id, date.today())

                    if not briefing_data:
                        print(f"[RESUMO-MATINAL] Erro ao preparar dados para usuário {usuario_id}")
                        continue

                    mensagem = ""
                    # Se não há eventos, verificar se deve enviar mensagem simples ou nada
                    if briefing_data.get('total_eventos', 0) == 0:
                        # Opcional: Você pode querer pular o envio se não tiver nada
                        print(f"[RESUMO-MATINAL] Sem eventos para usuário {usuario_id}. Gerando mensagem básica.")
                        mensagem = briefing_service.generate_briefing_message(usuario_id, date.today())
                    else:
                        # Gerar resumo com IA
                        print(f"[RESUMO-MATINAL] Gerando resumo com IA para usuário {usuario_id}...")
                        mensagem = generate_daily_briefing(briefing_data)

                    if not mensagem:
                        print(f"[RESUMO-MATINAL] Falha ao gerar mensagem para usuário {usuario_id}")
                        continue

                    # Enviar via WhatsApp
                    enviar_notificacao_whatsapp(
                        numero_whatsapp,
                        mensagem,
                        Config.BOT_WHATSAPP_URL,
                        Config.API_SECRET_KEY
                    )

                    print(f"[RESUMO-MATINAL] ✅ Resumo enviado para usuário {usuario_id}")

                except Exception as e_user:
                    print(f"[RESUMO-MATINAL] ❌ Erro ao processar usuário {usuario_id}: {e_user}")
                    continue

            print(f"[RESUMO-MATINAL] Processamento finalizado - {datetime.now()}")

    except Exception as e:
        print(f"[RESUMO-MATINAL] ❌ ERRO CRÍTICO: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    processar_resumo_matinal()