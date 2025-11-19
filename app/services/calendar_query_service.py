# app/services/calendar_query_service.py (VERSÃO FINAL CORRIGIDA)

from datetime import date, datetime, timedelta, time
from zoneinfo import ZoneInfo
from app.services.google_calendar_service import GoogleCalendarService
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.config import GOOGLE_REDIRECT_URI

# Singleton
google_calendar_service = GoogleCalendarService()

class CalendarQueryService:
    """Processa consultas de agenda do usuário"""
    
    # Timezone do Brasil (GMT-03)
    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
    
    @staticmethod
    def get_period_dates_for_calendar(period_type):
        """
        Similar ao PeriodQueryService, mas para agenda.
        CORREÇÃO: Adicionado suporte para 'este_mes' e 'mes_passado'.
        
        Returns:
            (start_date, end_date, description)
        """
        print(f"[CALENDAR] Calculando datas para período '{period_type}'")
        
        hoje = date.today()
        
        if period_type == 'hoje':
            return hoje, hoje, "hoje"
        
        elif period_type == 'amanha':
            amanha = hoje + timedelta(days=1)
            return amanha, amanha, "amanhã"
        
        elif period_type == 'final_de_semana':
            # Próximo final de semana
            dias_ate_sabado = (5 - hoje.weekday()) % 7
            if dias_ate_sabado == 0 and hoje.weekday() == 5:  # Hoje é sábado
                sabado = hoje
            else:
                sabado = hoje + timedelta(days=dias_ate_sabado if dias_ate_sabado > 0 else 7)
            domingo = sabado + timedelta(days=1)
            return sabado, domingo, "no final de semana"
        
        elif period_type == 'esta_semana':
            # Hoje até domingo
            dias_ate_domingo = (6 - hoje.weekday()) % 7
            domingo = hoje + timedelta(days=dias_ate_domingo) if dias_ate_domingo > 0 else hoje
            return hoje, domingo, "esta semana"
        
        elif period_type == 'proxima_semana':
            # Segunda a domingo da próxima semana
            dias_ate_segunda = (7 - hoje.weekday()) % 7
            segunda = hoje + timedelta(days=dias_ate_segunda if dias_ate_segunda > 0 else 7)
            domingo = segunda + timedelta(days=6)
            return segunda, domingo, "na próxima semana"
        
        elif period_type == 'este_mes':
            # CORREÇÃO: Primeiro dia do mês até hoje
            primeiro_dia = hoje.replace(day=1)
            # Último dia do mês
            if hoje.month == 12:
                ultimo_dia = date(hoje.year + 1, 1, 1) - timedelta(days=1)
            else:
                ultimo_dia = date(hoje.year, hoje.month + 1, 1) - timedelta(days=1)
            
            return primeiro_dia, ultimo_dia, f"este mês ({hoje.strftime('%B/%Y')})"
        
        elif period_type == 'mes_passado':
            # CORREÇÃO: Mês passado completo
            primeiro_dia_este_mes = hoje.replace(day=1)
            ultimo_dia_mes_passado = primeiro_dia_este_mes - timedelta(days=1)
            primeiro_dia_mes_passado = ultimo_dia_mes_passado.replace(day=1)
            
            return primeiro_dia_mes_passado, ultimo_dia_mes_passado, \
                   f"no mês passado ({ultimo_dia_mes_passado.strftime('%B/%Y')})"
        
        elif period_type == 'proximo_mes':
            # Primeiro dia do próximo mês
            if hoje.month == 12:
                primeiro_dia = date(hoje.year + 1, 1, 1)
            else:
                primeiro_dia = date(hoje.year, hoje.month + 1, 1)
            
            # Último dia do próximo mês
            if primeiro_dia.month == 12:
                ultimo_dia = date(primeiro_dia.year + 1, 1, 1) - timedelta(days=1)
            else:
                ultimo_dia = date(primeiro_dia.year, primeiro_dia.month + 1, 1) - timedelta(days=1)
            
            return primeiro_dia, ultimo_dia, f"no próximo mês ({primeiro_dia.strftime('%B/%Y')})"
        
        else:
            # Padrão: hoje
            return hoje, hoje, "hoje"
    
    @staticmethod
    def query_agenda(usuario_id, period_type):
        """
        Consulta agenda usando OAuth2 (credenciais do próprio usuário).
        
        Returns:
            str: Mensagem formatada para WhatsApp
        """
        print(f"[CALENDAR] Consultando agenda do usuário {usuario_id} para período '{period_type}'")
        
        # Verificar se usuário conectou
        if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
            if not GOOGLE_REDIRECT_URI:
                return (
                    "⚠️ *Google Calendar não configurado*\n\n"
                    "O administrador precisa configurar as credenciais OAuth2."
                )
            
            # Gerar link de conexão
            base_url = GOOGLE_REDIRECT_URI.rsplit('/', 1)[0]
            connect_url = f"{base_url}/connect-calendar/{usuario_id}"
            
            print(f"[CALENDAR] Usuário NÃO conectado. Link de conexão: {connect_url}")
            
            return (
                f"📅 *Google Calendar não conectado*\n\n"
                f"Para consultar sua agenda, conecte primeiro:\n"
                f"👉 {connect_url}\n\n"
                f"✅ É rápido e seguro!\n"
                f"✅ Só precisa fazer 1 vez\n"
                f"✅ Padrão OAuth2 oficial do Google"
            )
        
        try:
            print(f"[CALENDAR] Usuário CONECTADO. Buscando eventos...")
            
            # Obter serviço do Calendar
            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
            print(f"[CALENDAR] Serviço obtido com sucesso")
            
            # Calcular período
            start_date, end_date, description = CalendarQueryService.get_period_dates_for_calendar(period_type)
            print(f"[CALENDAR] Período: {start_date} a {end_date} ({description})")
            
            # Buscar eventos
            if start_date == end_date:
                # Um dia
                print(f"[CALENDAR] Buscando eventos de UM dia")
                events = CalendarQueryService._get_events_for_date(service, start_date)
                print(f"[CALENDAR] Encontrados {len(events)} eventos")
                return GoogleCalendarService.format_events_for_whatsapp(events, start_date)
            else:
                # Múltiplos dias
                print(f"[CALENDAR] Buscando eventos de MÚLTIPLOS dias")
                events_by_date = CalendarQueryService._get_events_for_period(service, start_date, end_date)
                total_events = sum(len(events) for events in events_by_date.values())
                print(f"[CALENDAR] Encontrados {total_events} eventos em {len(events_by_date)} dias")
                return GoogleCalendarService.format_period_events_for_whatsapp(events_by_date, start_date, end_date)
        
        except Exception as e:
            print(f"[CALENDAR] ❌ Erro geral ao consultar: {e}")
            print(f"[CALENDAR] Tipo do erro: {type(e).__name__}")
            import traceback
            traceback.print_exc()
            
            return (
                f"❌ Erro ao consultar agenda: {str(e)}\n\n"
                f"Tente reconectar seu Google Calendar."
            )
    
    @staticmethod
    def _get_events_for_date(service, target_date):
        """
        Busca eventos de um dia usando serviço OAuth.
        CORREÇÃO: Usa timezone do Brasil (GMT-03) para buscar corretamente.
        """
        try:
            # Criar datetime no timezone do Brasil
            start_datetime = datetime.combine(
                target_date, 
                time.min
            ).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            end_datetime = datetime.combine(
                target_date,
                time.max
            ).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            # Converter para ISO com timezone
            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()

            print(f"[CALENDAR] Buscando de {start_iso} até {end_iso}")
            print(f"[CALENDAR] (Timezone: America/Sao_Paulo)")

            events_result = service.events().list(
                calendarId='primary',
                timeMin=start_iso,
                timeMax=end_iso,
                singleEvents=True,
                orderBy='startTime'
            ).execute()

            events = events_result.get('items', [])
            print(f"[CALENDAR] API retornou {len(events)} eventos")

            formatted_events = []
            for event in events:
                try:
                    start = event['start'].get('dateTime', event['start'].get('date'))
                    end = event['end'].get('dateTime', event['end'].get('date'))

                    formatted_events.append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start,
                        'end': end,
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start']
                    })
                except Exception as e:
                    print(f"[CALENDAR] Erro ao processar evento: {e}")
                    continue
                
            return formatted_events

        except Exception as e:
            print(f"[CALENDAR] ❌ Erro em _get_events_for_date: {e}")
            import traceback
            traceback.print_exc()
            raise
    
    @staticmethod
    def _get_events_for_period(service, start_date, end_date):
        """
        Busca eventos de um período usando serviço OAuth.
        CORREÇÃO: Usa timezone do Brasil (GMT-03).
        """
        try:
            # Início do período no timezone do Brasil
            start_datetime = datetime.combine(
                start_date,
                time.min
            ).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            # Fim do período no timezone do Brasil
            end_datetime = datetime.combine(
                end_date,
                time.max
            ).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            # Converter para ISO
            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()
            
            print(f"[CALENDAR] Buscando período de {start_iso} até {end_iso}")
            print(f"[CALENDAR] (Timezone: America/Sao_Paulo)")
            
            events_result = service.events().list(
                calendarId='primary',
                timeMin=start_iso,
                timeMax=end_iso,
                singleEvents=True,
                orderBy='startTime'
            ).execute()
            
            events = events_result.get('items', [])
            print(f"[CALENDAR] API retornou {len(events)} eventos")
            
            events_by_date = {}
            
            for event in events:
                try:
                    start_str = event['start'].get('dateTime', event['start'].get('date'))
                    
                    # Parse da data do evento
                    if 'T' in start_str:
                        # DateTime com hora
                        event_dt = datetime.fromisoformat(start_str)
                        
                        # Converter para timezone do Brasil
                        if event_dt.tzinfo is None:
                            event_dt = event_dt.replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
                        else:
                            event_dt = event_dt.astimezone(CalendarQueryService.TIMEZONE_BR)
                        
                        # Extrair data
                        event_date = event_dt.date()
                    else:
                        # Date sem hora (evento de dia inteiro)
                        event_date = date.fromisoformat(start_str)
                    
                    if event_date not in events_by_date:
                        events_by_date[event_date] = []
                    
                    events_by_date[event_date].append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start_str,
                        'end': event['end'].get('dateTime', event['end'].get('date')),
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start']
                    })
                    
                except Exception as e:
                    print(f"[CALENDAR] Erro ao processar evento: {e}")
                    continue
                
            return events_by_date
            
        except Exception as e:
            print(f"[CALENDAR] ❌ Erro em _get_events_for_period: {e}")
            import traceback
            traceback.print_exc()
            raise