# app/services/calendar_alert_service.py
"""
Serviço para processar e enviar alertas de tarefas do Google Calendar
"""

from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.notification_service import enviar_notificacao_whatsapp
from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")

class CalendarAlertService:
    """Processa alertas de tarefas do Google Calendar"""

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
            print(f"[CALENDAR-ALERT] ❌ Erro ao buscar eventos: {e}")
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

            # Enviar alerta para cada evento
            alertas_enviados = 0
            for evento in eventos:
                sucesso = CalendarAlertService.send_event_alert(
                    numero_whatsapp=numero_whatsapp,
                    event=evento,
                    minutos_antes=minutos_antes
                )

                if sucesso:
                    alertas_enviados += 1

            print(f"[CALENDAR-ALERT] ✅ {alertas_enviados} alertas enviados para usuário {usuario_id}")
            return alertas_enviados

        except Exception as e:
            print(f"[CALENDAR-ALERT] ❌ Erro ao processar alertas para usuário {usuario_id}: {e}")
            import traceback
            traceback.print_exc()
            return 0
