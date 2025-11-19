# app/services/calendar_query_service.py (VERSÃO FINAL - TODOS OS CALENDÁRIOS)

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
    def filter_events_by_time_period(events, period='tarde'):
        """
        Filtra eventos por período do dia.

        Args:
            events: Lista de eventos
            period: 'manha' (6h-12h), 'tarde' (12h-18h), 'noite' (18h-23h59),
                    'madrugada' (0h-6h), 'agora' (próximas 4 horas)

        Returns:
            Lista de eventos filtrados
        """
        from datetime import datetime, time
        from zoneinfo import ZoneInfo

        TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
        now = datetime.now(TIMEZONE_BR)

        # Definir ranges de horário
        time_ranges = {
            'madrugada': (time(0, 0), time(5, 59)),
            'manha': (time(6, 0), time(11, 59)),
            'tarde': (time(12, 0), time(17, 59)),
            'noite': (time(18, 0), time(23, 59))
        }
        
        if period == 'agora':
            # Próximas 4 horas a partir de agora
            cutoff_time = now + timedelta(hours=4)
            
            filtered = []
            for event in events:
                if event['all_day']:
                    continue  # Ignorar eventos de dia inteiro
                
                try:
                    # Parse start time
                    start_str = event['start']
                    if 'T' in start_str:
                        event_dt = datetime.fromisoformat(start_str)
                        if event_dt.tzinfo is None:
                            event_dt = event_dt.replace(tzinfo=TIMEZONE_BR)
                        else:
                            event_dt = event_dt.astimezone(TIMEZONE_BR)
                        
                        # Evento deve começar entre now e cutoff_time
                        if now <= event_dt <= cutoff_time:
                            filtered.append(event)
                except:
                    continue
                
            return filtered
        
        elif period in time_ranges:
            start_range, end_range = time_ranges[period]

            filtered = []
            for event in events:
                if event['all_day']:
                    # Eventos de dia inteiro são incluídos em todos os períodos
                    filtered.append(event)
                    continue

                try:
                    start_str = event['start']
                    if 'T' in start_str:
                        event_dt = datetime.fromisoformat(start_str)
                        if event_dt.tzinfo is None:
                            event_dt = event_dt.replace(tzinfo=TIMEZONE_BR)
                        else:
                            event_dt = event_dt.astimezone(TIMEZONE_BR)

                        event_time = event_dt.time()

                        # Verificar se está no range E ainda não aconteceu
                        if start_range <= event_time <= end_range and event_dt > now:
                            filtered.append(event)
                except:
                    continue

            return filtered

        else:
            # Período desconhecido, retornar todos
            return events

    @staticmethod
    def query_agenda_with_time_filter(usuario_id, period_type='hoje', time_filter=None):
        """
        Consulta agenda com filtro de horário.

        Args:
            usuario_id: ID do usuário
            period_type: 'hoje', 'amanha', etc
            time_filter: 'manha', 'tarde', 'noite', 'agora', ou None

        Returns:
            str: Mensagem formatada
        """
        print(f"[CALENDAR] Consultando com filtro de horário: {time_filter}")

        # Obter eventos normalmente
        service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
        start_date, end_date, description = CalendarQueryService.get_period_dates_for_calendar(period_type)

        if start_date == end_date:
            events = CalendarQueryService._get_events_for_date(service, start_date)

            # Aplicar filtro de horário se fornecido
            if time_filter:
                events = CalendarQueryService.filter_events_by_time_period(events, time_filter)

                # Ajustar descrição
                time_desc = {
                    'manha': 'da manhã',
                    'tarde': 'da tarde',
                    'noite': 'da noite',
                    'madrugada': 'da madrugada',
                    'agora': 'nas próximas horas'
                }.get(time_filter, '')

                description = f"{description} {time_desc}"

            return GoogleCalendarService.format_events_for_whatsapp(events, start_date)
        else:
            # Para múltiplos dias, filtro de horário não faz muito sentido
            events_by_date = CalendarQueryService._get_events_for_period(service, start_date, end_date)

            if time_filter:
                # Aplicar filtro em cada dia
                for event_date in events_by_date:
                    events_by_date[event_date] = CalendarQueryService.filter_events_by_time_period(
                        events_by_date[event_date],
                        time_filter
                    )

                # Remover dias sem eventos
                events_by_date = {d: e for d, e in events_by_date.items() if e}

            return GoogleCalendarService.format_period_events_for_whatsapp(events_by_date, start_date, end_date)
    
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
            domingo = hoje + timedelta(days=dias_ate_domingo) if dias_ate_domingo > 0 else hoje
            return hoje, domingo, "esta semana"
        
        elif period_type == 'proxima_semana':
            # Segunda a domingo da próxima semana
            dias_ate_segunda = (7 - hoje.weekday()) % 7
            segunda = hoje + timedelta(days=dias_ate_segunda if dias_ate_segunda > 0 else 7)
            domingo = segunda + timedelta(days=6)
            return segunda, domingo, "na próxima semana"
        
        elif period_type == 'este_mes':
            # Primeiro dia do mês até último
            primeiro_dia = hoje.replace(day=1)
            if hoje.month == 12:
                ultimo_dia = date(hoje.year + 1, 1, 1) - timedelta(days=1)
            else:
                ultimo_dia = date(hoje.year, hoje.month + 1, 1) - timedelta(days=1)
            
            return primeiro_dia, ultimo_dia, f"este mês ({hoje.strftime('%B/%Y')})"
        
        elif period_type == 'mes_passado':
            primeiro_dia_este_mes = hoje.replace(day=1)
            ultimo_dia_mes_passado = primeiro_dia_este_mes - timedelta(days=1)
            primeiro_dia_mes_passado = ultimo_dia_mes_passado.replace(day=1)
            
            return primeiro_dia_mes_passado, ultimo_dia_mes_passado, \
                   f"no mês passado ({ultimo_dia_mes_passado.strftime('%B/%Y')})"
        
        elif period_type == 'proximo_mes':
            if hoje.month == 12:
                primeiro_dia = date(hoje.year + 1, 1, 1)
            else:
                primeiro_dia = date(hoje.year, hoje.month + 1, 1)
            
            if primeiro_dia.month == 12:
                ultimo_dia = date(primeiro_dia.year + 1, 1, 1) - timedelta(days=1)
            else:
                ultimo_dia = date(primeiro_dia.year, primeiro_dia.month + 1, 1) - timedelta(days=1)
            
            return primeiro_dia, ultimo_dia, f"no próximo mês ({primeiro_dia.strftime('%B/%Y')})"
        
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
            if not GOOGLE_REDIRECT_URI:
                return (
                    "⚠️ *Google Calendar não configurado*\n\n"
                    "O administrador precisa configurar as credenciais OAuth2."
                )
            
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
            
            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
            print(f"[CALENDAR] Serviço obtido com sucesso")
            
            # Calcular período
            start_date, end_date, description = CalendarQueryService.get_period_dates_for_calendar(period_type)
            print(f"[CALENDAR] Período: {start_date} a {end_date} ({description})")
            
            # Buscar eventos
            if start_date == end_date:
                print(f"[CALENDAR] Buscando eventos de UM dia")
                events = CalendarQueryService._get_events_for_date(service, start_date)
                print(f"[CALENDAR] Encontrados {len(events)} eventos")
                return GoogleCalendarService.format_events_for_whatsapp(events, start_date)
            else:
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
    def _get_all_calendar_ids(service):
        """
        NOVO: Busca IDs de TODOS os calendários do usuário.
        
        Returns:
            list: Lista de dicts com {id, summary, selected}
        """
        try:
            calendars_result = service.calendarList().list().execute()
            calendars = calendars_result.get('items', [])
            
            # Filtrar apenas calendários selecionados (visíveis)
            calendar_ids = []
            for cal in calendars:
                # Pegar apenas calendários selecionados
                if cal.get('selected', False):
                    calendar_ids.append({
                        'id': cal['id'],
                        'summary': cal.get('summary', 'Sem nome'),
                        'primary': cal.get('primary', False)
                    })
            
            print(f"[CALENDAR] {len(calendar_ids)} calendários selecionados encontrados")
            for cal in calendar_ids:
                primary_tag = " (PRIMARY)" if cal['primary'] else ""
                print(f"[CALENDAR]   - {cal['summary']}{primary_tag}")
            
            return calendar_ids
            
        except Exception as e:
            print(f"[CALENDAR] Erro ao listar calendários: {e}")
            # Fallback: retornar apenas primary
            return [{'id': 'primary', 'summary': 'Primary', 'primary': True}]
    
    @staticmethod
    def _get_events_for_date(service, target_date):
        """
        Busca eventos de um dia em TODOS os calendários.
        CORREÇÃO: Busca em múltiplos calendários.
        """
        try:
            # Obter todos os calendários
            calendar_ids = CalendarQueryService._get_all_calendar_ids(service)
            
            # Criar datetime no timezone do Brasil
            start_datetime = datetime.combine(target_date, time.min).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            end_datetime = datetime.combine(target_date, time.max).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()

            print(f"[CALENDAR] Buscando de {start_iso} até {end_iso}")
            print(f"[CALENDAR] (Timezone: America/Sao_Paulo)")

            # Buscar em TODOS os calendários
            all_events = []
            for calendar in calendar_ids:
                cal_id = calendar['id']
                cal_name = calendar['summary']
                
                try:
                    events_result = service.events().list(
                        calendarId=cal_id,
                        timeMin=start_iso,
                        timeMax=end_iso,
                        singleEvents=True,
                        orderBy='startTime'
                    ).execute()

                    events = events_result.get('items', [])
                    
                    if events:
                        print(f"[CALENDAR]   📅 {cal_name}: {len(events)} eventos")
                    
                    # Adicionar info do calendário em cada evento
                    for event in events:
                        event['_calendar_name'] = cal_name
                        event['_calendar_id'] = cal_id
                        all_events.append(event)
                        
                except Exception as e:
                    print(f"[CALENDAR] Erro ao buscar em '{cal_name}': {e}")
                    continue

            print(f"[CALENDAR] API retornou {len(all_events)} eventos no total")

            # Formatar eventos
            formatted_events = []
            for event in all_events:
                try:
                    start = event['start'].get('dateTime', event['start'].get('date'))
                    end = event['end'].get('dateTime', event['end'].get('date'))

                    formatted_events.append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start,
                        'end': end,
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start'],
                        'calendar_name': event.get('_calendar_name', '')
                    })
                except Exception as e:
                    print(f"[CALENDAR] Erro ao processar evento: {e}")
                    continue
            
            # Ordenar por horário
            formatted_events.sort(key=lambda x: x['start'])
                
            return formatted_events

        except Exception as e:
            print(f"[CALENDAR] ❌ Erro em _get_events_for_date: {e}")
            import traceback
            traceback.print_exc()
            raise
    
    @staticmethod
    def _get_events_for_period(service, start_date, end_date):
        """
        Busca eventos de um período em TODOS os calendários.
        CORREÇÃO: Busca em múltiplos calendários.
        """
        try:
            # Obter todos os calendários
            calendar_ids = CalendarQueryService._get_all_calendar_ids(service)
            
            # Criar datetime no timezone do Brasil
            start_datetime = datetime.combine(start_date, time.min).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            end_datetime = datetime.combine(end_date, time.max).replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
            
            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()
            
            print(f"[CALENDAR] Buscando período de {start_iso} até {end_iso}")
            print(f"[CALENDAR] (Timezone: America/Sao_Paulo)")
            
            # Buscar em TODOS os calendários
            all_events = []
            for calendar in calendar_ids:
                cal_id = calendar['id']
                cal_name = calendar['summary']
                
                try:
                    events_result = service.events().list(
                        calendarId=cal_id,
                        timeMin=start_iso,
                        timeMax=end_iso,
                        singleEvents=True,
                        orderBy='startTime'
                    ).execute()
                    
                    events = events_result.get('items', [])
                    
                    if events:
                        print(f"[CALENDAR]   📅 {cal_name}: {len(events)} eventos")
                    
                    for event in events:
                        event['_calendar_name'] = cal_name
                        event['_calendar_id'] = cal_id
                        all_events.append(event)
                        
                except Exception as e:
                    print(f"[CALENDAR] Erro ao buscar em '{cal_name}': {e}")
                    continue
            
            print(f"[CALENDAR] API retornou {len(all_events)} eventos no total")
            
            # Agrupar por data
            events_by_date = {}
            
            for event in all_events:
                try:
                    start_str = event['start'].get('dateTime', event['start'].get('date'))
                    
                    if 'T' in start_str:
                        event_dt = datetime.fromisoformat(start_str)
                        if event_dt.tzinfo is None:
                            event_dt = event_dt.replace(tzinfo=CalendarQueryService.TIMEZONE_BR)
                        else:
                            event_dt = event_dt.astimezone(CalendarQueryService.TIMEZONE_BR)
                        event_date = event_dt.date()
                    else:
                        event_date = date.fromisoformat(start_str)
                    
                    if event_date not in events_by_date:
                        events_by_date[event_date] = []
                    
                    events_by_date[event_date].append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start_str,
                        'end': event['end'].get('dateTime', event['end'].get('date')),
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start'],
                        'calendar_name': event.get('_calendar_name', '')
                    })
                    
                except Exception as e:
                    print(f"[CALENDAR] Erro ao processar evento: {e}")
                    continue
            
            # Ordenar eventos de cada dia por horário
            for event_date in events_by_date:
                events_by_date[event_date].sort(key=lambda x: x['start'])
                
            return events_by_date
            
        except Exception as e:
            print(f"[CALENDAR] ❌ Erro em _get_events_for_period: {e}")
            import traceback
            traceback.print_exc()
            raise