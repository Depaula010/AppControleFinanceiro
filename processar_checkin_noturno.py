#!/usr/bin/env python3
"""
Processador de Check-in Noturno
Executado via cron job (Ofelia) para enviar confirmações de contas pendentes
"""

import os
import sys
from datetime import datetime, time, date

# Adicionar diretório raiz ao path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))


def processar_checkin_noturno():
    """
    Função principal executada pelo cron job.
    Envia check-in noturno para usuários configurados no horário atual.
    """
    print(f"[CHECKIN-NOTURNO] Início do processamento - {datetime.now()}")

    try:
        # Criar instância da aplicação
        from app import create_app
        app = create_app()

        # Entrar no contexto da aplicação
        with app.app_context():
            # Importar serviços
            from app.services.notification_config_service import NotificationConfigService
            from app.services.nightly_checkin_service import NightlyCheckinService
            from app.services.notification_service import enviar_notificacao_whatsapp
            from app.services.redis_service import redis_service
            from app import db_engine

            # Verificar se Redis está disponível
            if not redis_service.is_connected():
                print("[CHECKIN-NOTURNO] ❌ Redis indisponível - abortando")
                print("[CHECKIN-NOTURNO] Check-in requer Redis para gerenciar sessões")
                return

            # Obter hora atual (zerando segundos)
            hora_atual = datetime.now().time().replace(second=0, microsecond=0)

            print(f"[CHECKIN-NOTURNO] Buscando usuários para {hora_atual.strftime('%H:%M')}")

            # Buscar usuários com check-in ativo para esta hora
            usuarios = NotificationConfigService.get_users_with_checkin_noturno_active(hora_atual)

            if not usuarios:
                print(f"[CHECKIN-NOTURNO] Nenhum usuário configurado para {hora_atual}")
                return

            print(f"[CHECKIN-NOTURNO] {len(usuarios)} usuário(s) encontrado(s)")

            # Processar cada usuário
            for usuario_id, numero_whatsapp in usuarios:
                try:
                    print(f"[CHECKIN-NOTURNO] Processando usuário {usuario_id}...")

                    # Buscar contas pendentes
                    with db_engine.connect() as conn:
                        pending_bills = NightlyCheckinService.get_pending_bills(
                            conn, usuario_id, date.today()
                        )

                    if not pending_bills:
                        print(f"[CHECKIN-NOTURNO] Sem contas pendentes - usuário {usuario_id}")
                        continue

                    print(f"[CHECKIN-NOTURNO] {len(pending_bills)} conta(s) pendente(s)")

                    # Criar sessão de check-in no Redis
                    checkin_id = NightlyCheckinService.create_checkin_session(
                        numero_whatsapp, pending_bills
                    )

                    if not checkin_id:
                        print(f"[CHECKIN-NOTURNO] ❌ Erro ao criar sessão - usuário {usuario_id}")
                        continue

                    # Formatar mensagem
                    mensagem = NightlyCheckinService.format_checkin_message(
                        pending_bills, checkin_id
                    )

                    if not mensagem:
                        print(f"[CHECKIN-NOTURNO] Sem mensagem para enviar - usuário {usuario_id}")
                        continue

                    # Enviar via WhatsApp
                    enviar_notificacao_whatsapp(
                        numero_whatsapp,
                        mensagem,
                        app.config.get('BOT_WHATSAPP_URL'),
                        app.config.get('API_SECRET_KEY')
                    )

                    print(f"[CHECKIN-NOTURNO] ✅ Mensagem enviada para usuário {usuario_id}")

                except Exception as e_user:
                    print(f"[CHECKIN-NOTURNO] ❌ Erro ao processar usuário {usuario_id}: {e_user}")
                    import traceback
                    traceback.print_exc()
                    continue

            print(f"[CHECKIN-NOTURNO] Processamento finalizado - {datetime.now()}")

    except Exception as e:
        print(f"[CHECKIN-NOTURNO] ❌ ERRO CRÍTICO: {e}")
        import traceback
        traceback.print_exc()


if __name__ == "__main__":
    processar_checkin_noturno()
