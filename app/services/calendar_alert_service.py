# app/services/calendar_alert_service.py
"""
Serviço para processar e enviar alertas de tarefas do Google Calendar
"""

from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.notification_service import enviar_notificacao_whatsapp
from app.services.redis_service import redis_service
from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY
import hashlib

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

class CalendarAlertService:
    """Processa alertas de tarefas do Google Calendar"""

    @staticmethod
    def _generate_alert_key(usuario_id, event_id, event_start):
        """
        Gera chave única para rastrear alertas enviados (anti-duplicação).

        Args:
            usuario_id: ID do usuário
            event_id: ID do evento no Google Calendar
            event_start: Horário de início do evento (ISO format)

        Returns:
            str: Chave única para o Redis
        """
        # Criar hash único: usuario + evento + data/hora
        # Isso garante que o mesmo evento no mesmo horário só será alertado uma vez
        data = f"{usuario_id}:{event_id}:{event_start}"
        hash_id = hashlib.md5(data.encode()).hexdigest()
        return f"calendar_alert:{hash_id}"

    @staticmethod
    def _try_claim_alert(usuario_id, event_id, event_start) -> bool:
        """
        Tenta reivindicar o alerta atomicamente usando SET NX.

        Este método resolve a race condition TOCTOU (Time-of-Check-Time-of-Use)
        ao usar uma operação atômica do Redis para "reservar" o alerta ANTES de enviá-lo.

        Args:
            usuario_id: ID do usuário
            event_id: ID do evento no Google Calendar
            event_start: Horário de início do evento (ISO format)

        Returns:
            bool: True se conseguiu claim (pode enviar), False se outro processo já tem
        """
        if not redis_service.is_connected():
            # Se Redis não disponível, prosseguir (fail-open) mas logar warning
            print("[CALENDAR-ALERT] ⚠️ Redis indisponível, prosseguindo sem lock (fail-open)")
            return True

        key = CalendarAlertService._generate_alert_key(usuario_id, event_id, event_start)

        # SET NX EX: Define a chave APENAS se não existir (atômico)
        # TTL de 2 horas (7200 segundos) - mesmo valor anterior
        claimed = redis_service.set_if_not_exists(key, "processing", ttl_seconds=7200)

        if not claimed:
            print(f"[CALENDAR-ALERT] 🔄 Alerta já em processamento por outro processo (key: {key})")
        else:
            print(f"[CALENDAR-ALERT] ✅ Claim adquirido (key: {key}, TTL: 2h)")

        return claimed

    @staticmethod
    def _release_claim(usuario_id, event_id, event_start):
        """
        Libera claim em caso de falha no envio (permite retry no próximo ciclo).

        Chamado quando:
        - O envio falhou (WhatsApp retornou erro)
        - Ocorreu uma exceção durante o envio

        Args:
            usuario_id: ID do usuário
            event_id: ID do evento no Google Calendar
            event_start: Horário de início do evento (ISO format)
        """
        if not redis_service.is_connected():
            return

        key = CalendarAlertService._generate_alert_key(usuario_id, event_id, event_start)
        redis_service.delete(key)
        print(f"[CALENDAR-ALERT] 🔓 Claim liberado para retry (key: {key})")

    @staticmethod
    def get_upcoming_events(usuario_id, minutos_antes):
        """
        Busca eventos que começarão em X minutos.

        Args:
            usuario_id: ID do usuário
            minutos_antes: Quantos minutos antes do evento buscar

        Returns:
            list: Lista de eventos que devem gerar alerta
        """
        try:
            print(f"[CALENDAR-ALERT] Buscando eventos para usuário {usuario_id} ({minutos_antes} min antes)")

            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)

            # Se não conseguiu obter service (token inválido), retornar lista vazia
            if service is None:
                print(f"[CALENDAR-ALERT] ⚠️ Usuário {usuario_id} não conectou Google Calendar ou token inválido")
                return []

            # Horário atual no Brasil
            agora = datetime.now(TIMEZONE_BR)

            print(f"[CALENDAR-ALERT] Horário atual: {agora.strftime('%H:%M:%S')}")

            # LÓGICA CORRETA: Se quero alerta X minutos ANTES, buscar eventos que começam em (agora + X minutos)
            # Exemplo: Se agora é 17:59 e minutos_antes=1, buscar eventos que começam às 18:00
            # Janela de ±30 segundos para garantir que o cronjob (que roda a cada minuto) pega o evento apenas UMA vez
            horario_alvo = agora + timedelta(minutes=minutos_antes)

            inicio_janela = horario_alvo - timedelta(seconds=30)
            fim_janela = horario_alvo + timedelta(seconds=30)

            inicio_iso = inicio_janela.isoformat()
            fim_iso = fim_janela.isoformat()

            print(f"[CALENDAR-ALERT] Buscando eventos que começam às {horario_alvo.strftime('%H:%M')} (±30s)")
            print(f"[CALENDAR-ALERT] Janela: {inicio_janela.strftime('%H:%M:%S')} até {fim_janela.strftime('%H:%M:%S')}")

            # Buscar todos os calendários
            calendars_result = service.calendarList().list().execute()
            calendars = calendars_result.get('items', [])

            # Filtrar apenas calendários selecionados
            selected_calendars = [cal for cal in calendars if cal.get('selected', False)]

            print(f"[CALENDAR-ALERT] Buscando em {len(selected_calendars)} calendários")

            # Buscar eventos em todos os calendários
            all_events = []
            for calendar in selected_calendars:
                cal_id = calendar['id']
                cal_name = calendar.get('summary', 'Sem nome')

                try:
                    events_result = service.events().list(
                        calendarId=cal_id,
                        timeMin=inicio_iso,
                        timeMax=fim_iso,
                        singleEvents=True,
                        orderBy='startTime'
                    ).execute()

                    events = events_result.get('items', [])

                    if events:
                        print(f"[CALENDAR-ALERT] Calendário '{cal_name}': {len(events)} eventos")

                    for event in events:
                        event['_calendar_name'] = cal_name
                        all_events.append(event)

                except Exception as e:
                    print(f"[CALENDAR-ALERT] Erro ao buscar em '{cal_name}': {e}")
                    continue

            print(f"[CALENDAR-ALERT] Total de {len(all_events)} eventos encontrados")

            # Filtrar apenas eventos com horário específico (não eventos de dia inteiro) e que são FUTUROS
            eventos_com_hora = []
            for event in all_events:
                start = event.get('start', {})

                # Apenas eventos com dateTime (não date)
                if 'dateTime' in start:
                    start_datetime_str = start.get('dateTime')
                    start_datetime = datetime.fromisoformat(start_datetime_str)

                    # IMPORTANTE: Verificar se o evento é FUTURO (ainda não começou)
                    if start_datetime > agora:
                        eventos_com_hora.append({
                            'id': event.get('id'),
                            'summary': event.get('summary', 'Sem título'),
                            'start': start_datetime_str,
                            'location': event.get('location', ''),
                            'description': event.get('description', ''),
                            'calendar_name': event.get('_calendar_name', '')
                        })
                        print(f"[CALENDAR-ALERT] ✅ Evento válido: '{event.get('summary')}' às {start_datetime.strftime('%H:%M')}")
                    else:
                        print(f"[CALENDAR-ALERT] ⏭️ Evento já passou, ignorando: '{event.get('summary')}' às {start_datetime.strftime('%H:%M')}")

            return eventos_com_hora

        except Exception as e:
            error_message = str(e)

            # Erro específico: Usuário não conectou Google Calendar (credentials=None)
            if "não conectou Google Calendar" in error_message or "credentials" in error_message.lower():
                print(f"[CALENDAR-ALERT] ⚠️ Google Calendar não conectado ou token inválido para usuário {usuario_id}")
                # Retornar lista vazia - não é um erro fatal
                return []

            # Outros erros: logar e retornar lista vazia para não quebrar o job
            print(f"[CALENDAR-ALERT] ❌ Erro ao buscar eventos para usuário {usuario_id}: {e}")
            import traceback
            traceback.print_exc()
            return []

    @staticmethod
    def format_event_time(datetime_str):
        """
        Formata datetime ISO para exibição amigável.

        Args:
            datetime_str: String ISO datetime (ex: '2024-01-15T14:30:00-03:00')

        Returns:
            str: Horário formatado (ex: '14:30')
        """
        try:
            dt = datetime.fromisoformat(datetime_str)
            return dt.strftime('%H:%M')
        except:
            return datetime_str

    @staticmethod
    def send_event_alert(numero_whatsapp, event, minutos_antes):
        """
        Envia alerta de evento via WhatsApp.

        Args:
            numero_whatsapp: Número do WhatsApp do usuário
            event: Dicionário com dados do evento
            minutos_antes: Quantos minutos antes está sendo alertado

        Returns:
            bool: True se enviou com sucesso
        """
        try:
            # Formatar mensagem
            titulo = event.get('summary', 'Sem título')
            horario = CalendarAlertService.format_event_time(event.get('start'))
            localizacao = event.get('location', '')
            descricao = event.get('description', '')
            calendario = event.get('calendar_name', '')

            mensagem = f"⏰ *Alerta de Tarefa*\n\n"
            mensagem += f"📅 *{titulo}*\n"
            mensagem += f"🕐 Horário: *{horario}*\n"

            if minutos_antes == 1:
                mensagem += f"⚠️ Começa em *1 minuto*!\n"
            else:
                mensagem += f"⚠️ Começa em *{minutos_antes} minutos*!\n"

            if localizacao:
                mensagem += f"📍 Local: {localizacao}\n"

            if descricao:
                # Limitar descrição a 200 caracteres
                desc_resumida = descricao[:200] + "..." if len(descricao) > 200 else descricao
                mensagem += f"\n📝 {desc_resumida}\n"

            if calendario:
                mensagem += f"\n📆 Calendário: {calendario}"

            # Enviar notificação
            sucesso = enviar_notificacao_whatsapp(
                numero=numero_whatsapp,
                mensagem=mensagem,
                bot_url=BOT_WHATSAPP_URL,
                api_key=API_SECRET_KEY
            )

            if sucesso:
                print(f"[CALENDAR-ALERT] ✅ Alerta enviado para {numero_whatsapp}: {titulo}")
            else:
                print(f"[CALENDAR-ALERT] ❌ Falha ao enviar alerta para {numero_whatsapp}")

            return sucesso

        except Exception as e:
            print(f"[CALENDAR-ALERT] ❌ Erro ao enviar alerta: {e}")
            return False

    @staticmethod
    def process_alerts_for_user(usuario_id, numero_whatsapp, minutos_antes):
        """
        Processa alertas para um usuário específico.

        Args:
            usuario_id: ID do usuário
            numero_whatsapp: Número do WhatsApp
            minutos_antes: Quantos minutos antes alertar

        Returns:
            int: Número de alertas enviados
        """
        try:
            print(f"[CALENDAR-ALERT] Processando alertas para usuário {usuario_id}")

            # Buscar eventos próximos
            eventos = CalendarAlertService.get_upcoming_events(usuario_id, minutos_antes)

            if not eventos:
                print(f"[CALENDAR-ALERT] Nenhum evento próximo para usuário {usuario_id}")
                return 0

            # Enviar alerta para cada evento (com proteção anti-duplicação via CLAIM ATÔMICO)
            alertas_enviados = 0
            for evento in eventos:
                event_id = evento.get('id')
                event_start = evento.get('start')

                # CLAIM ATÔMICO: Reserva o alerta ANTES de enviar (resolve race condition TOCTOU)
                # Se outro processo já tem o claim, pula este evento
                if not CalendarAlertService._try_claim_alert(usuario_id, event_id, event_start):
                    print(f"[CALENDAR-ALERT] ⏭️ Pulando evento '{evento.get('summary')}' - outro processo já processando")
                    continue

                # Enviar alerta com proteção de rollback
                try:
                    sucesso = CalendarAlertService.send_event_alert(
                        numero_whatsapp=numero_whatsapp,
                        event=evento,
                        minutos_antes=minutos_antes
                    )

                    if sucesso:
                        # Claim já está no Redis, alerta enviado com sucesso
                        alertas_enviados += 1
                    else:
                        # Envio falhou - liberar claim para permitir retry no próximo ciclo
                        CalendarAlertService._release_claim(usuario_id, event_id, event_start)

                except Exception as e:
                    # Erro durante envio - liberar claim para retry
                    CalendarAlertService._release_claim(usuario_id, event_id, event_start)
                    print(f"[CALENDAR-ALERT] ❌ Erro ao enviar alerta para evento '{evento.get('summary')}': {e}")
                    # Continua para o próximo evento ao invés de parar tudo

            print(f"[CALENDAR-ALERT] ✅ {alertas_enviados} alertas enviados para usuário {usuario_id}")
            return alertas_enviados

        except Exception as e:
            # Erros já são tratados em get_upcoming_events - este catch é safety net
            print(f"[CALENDAR-ALERT] ❌ Erro inesperado ao processar usuário {usuario_id}: {e}")
            import traceback
            traceback.print_exc()
            return 0

    @staticmethod
    def _notify_calendar_disconnected(usuario_id, numero_whatsapp):
        """
        Notifica usuário que o Google Calendar está desconectado.
        Usa Redis para enviar apenas 1x por semana.

        Args:
            usuario_id: ID do usuário
            numero_whatsapp: Número do WhatsApp
        """
        from app.services.redis_service import redis_service
        from app.services.notification_service import enviar_notificacao_whatsapp
        from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

        # Verificar se já foi notificado recentemente (Redis)
        redis_key = f"calendar_disconnected_alert:{usuario_id}"

        if redis_service.exists(redis_key):
            print(f"[CALENDAR-ALERT] ℹ️ Usuário {usuario_id} já foi notificado esta semana sobre Calendar desconectado")
            return

        # Preparar mensagem
        mensagem = """⚠️ *Google Calendar Desconectado*

Seus alertas de tarefas estão ativos, mas o Google Calendar não está conectado.

Para continuar recebendo alertas:
1. Acesse as configurações do app
2. Reconecte sua conta do Google Calendar

_Esta notificação é enviada 1x por semana enquanto o Calendar estiver desconectado._"""

        # Enviar notificação
        sucesso = enviar_notificacao_whatsapp(
            numero=numero_whatsapp,
            mensagem=mensagem,
            bot_url=BOT_WHATSAPP_URL,
            api_key=API_SECRET_KEY
        )

        if sucesso:
            # Marcar como notificado (TTL: 7 dias = 1 semana)
            redis_service.set_with_ttl(redis_key, "1", ttl_seconds=7*24*60*60)
            print(f"[CALENDAR-ALERT] ✅ Notificação de Calendar desconectado enviada para usuário {usuario_id}")
        else:
            print(f"[CALENDAR-ALERT] ❌ Falha ao enviar notificação para usuário {usuario_id}")
