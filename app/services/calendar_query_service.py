# app/services/calendar_query_service.py (VERSÃO CORRIGIDA)

from datetime import date, datetime, timedelta, timezone
from app.services.google_calendar_service import GoogleCalendarService
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.config import GOOGLE_REDIRECT_URI

# Singleton
google_calendar_service = GoogleCalendarService()

class CalendarQueryService:
    """Processa consultas de agenda do usuário"""
    
    @staticmethod
    def get_period_dates_for_calendar(period_type):
        """
        Similar ao PeriodQueryService, mas para agenda.
        
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
            domingo = hoje + timedelta(days=dias_ate_domingo)
            return hoje, domingo, "esta semana"
        
        elif period_type == 'proxima_semana':
            # Segunda a domingo da próxima semana
            dias_ate_segunda = (7 - hoje.weekday()) % 7
            segunda = hoje + timedelta(days=dias_ate_segunda if dias_ate_segunda > 0 else 7)
            domingo = segunda + timedelta(days=6)
            return segunda, domingo, "na próxima semana"
        
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
            
            return primeiro_dia, ultimo_dia, "no próximo mês"
        
        else:
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
            # CORREÇÃO: Verificar se GOOGLE_REDIRECT_URI existe
            if not GOOGLE_REDIRECT_URI:
                return (
                    "⚠️ *Google Calendar não configurado*\n\n"
                    "O administrador precisa configurar as credenciais OAuth2."
                )
            
            # Gerar link de conexão
            base_url = GOOGLE_REDIRECT_URI.rsplit('/', 1)[0]  # Remove /oauth2callback
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
            
            # Calcular período
            start_date, end_date, description = CalendarQueryService.get_period_dates_for_calendar(period_type)
            
            # Buscar eventos
            print(f"[CALENDAR] Buscando eventos de {start_date} a {end_date}")
            
            if start_date == end_date:
                # Um dia
                events = CalendarQueryService._get_events_for_date(service, start_date)
                print(f"[CALENDAR] Encontrados {len(events)} eventos")
                return GoogleCalendarService.format_events_for_whatsapp(events, start_date)
            else:
                # Múltiplos dias
                events_by_date = CalendarQueryService._get_events_for_period(service, start_date, end_date)
                total_events = sum(len(events) for events in events_by_date.values())
                print(f"[CALENDAR] Encontrados {total_events} eventos em {len(events_by_date)} dias")
                return GoogleCalendarService.format_period_events_for_whatsapp(events_by_date, start_date, end_date)
        
        except Exception as e:
            print(f"[CALENDAR] ❌ Erro ao consultar: {e}")
            import traceback
            traceback.print_exc()
            
            return (
                f"❌ Erro ao consultar agenda: {str(e)}\n\n"
                f"Tente reconectar seu Google Calendar."
            )
    
    @staticmethod
    def _get_events_for_date(service, target_date):
        """Busca eventos de um dia usando serviço OAuth"""
        # CORREÇÃO: Usar timezone-aware datetime
        start_of_day = datetime.combine(target_date, datetime.min.time()).replace(tzinfo=timezone.utc).isoformat()
        end_of_day = datetime.combine(target_date, datetime.max.time()).replace(tzinfo=timezone.utc).isoformat()
        
        print(f"[CALENDAR] Buscando de {start_of_day} até {end_of_day}")
        
        events_result = service.events().list(
            calendarId='primary',
            timeMin=start_of_day,
            timeMax=end_of_day,
            singleEvents=True,
            orderBy='startTime'
        ).execute()
        
        events = events_result.get('items', [])
        print(f"[CALENDAR] API retornou {len(events)} eventos")
        
        formatted_events = []
        for event in events:
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
        
        return formatted_events
    
    @staticmethod
    def _get_events_for_period(service, start_date, end_date):
        """Busca eventos de um período usando serviço OAuth"""
        # CORREÇÃO: Usar timezone-aware datetime
        start_datetime = datetime.combine(start_date, datetime.min.time()).replace(tzinfo=timezone.utc).isoformat()
        end_datetime = datetime.combine(end_date, datetime.max.time()).replace(tzinfo=timezone.utc).isoformat()
        
        print(f"[CALENDAR] Buscando período de {start_datetime} até {end_datetime}")
        
        events_result = service.events().list(
            calendarId='primary',
            timeMin=start_datetime,
            timeMax=end_datetime,
            singleEvents=True,
            orderBy='startTime'
        ).execute()
        
        events = events_result.get('items', [])
        print(f"[CALENDAR] API retornou {len(events)} eventos")
        
        events_by_date = {}
        
        for event in events:
            start = event['start'].get('dateTime', event['start'].get('date'))
            
            # Parse da data
            if 'T' in start:
                # DateTime com hora
                event_date = datetime.fromisoformat(start.replace('Z', '+00:00')).date()
            else:
                # Date sem hora (evento de dia inteiro)
                event_date = date.fromisoformat(start)
            
            if event_date not in events_by_date:
                events_by_date[event_date] = []
            
            events_by_date[event_date].append({
                'summary': event.get('summary', 'Sem título'),
                'start': start,
                'end': event['end'].get('dateTime', event['end'].get('date')),
                'location': event.get('location', ''),
                'description': event.get('description', ''),
                'all_day': 'date' in event['start']
            })
        
        return events_by_date