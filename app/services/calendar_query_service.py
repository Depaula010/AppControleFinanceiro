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
            print(f"[CALENDAR] Serviço obtido com sucesso")
            
            # Calcular período
            start_date, end_date, description = CalendarQueryService.get_period_dates_for_calendar(period_type)
            print(f"[CALENDAR] Período: {start_date} a {end_date} ({description})")
            
            # Buscar eventos COM PROTEÇÃO EXTRA
            if start_date == end_date:
                # Um dia
                print(f"[CALENDAR] Buscando eventos de UM dia")
                try:
                    events = CalendarQueryService._get_events_for_date(service, start_date)
                    print(f"[CALENDAR] Encontrados {len(events)} eventos")
                    return GoogleCalendarService.format_events_for_whatsapp(events, start_date)
                except Exception as e_day:
                    print(f"[CALENDAR] ❌ Erro específico em _get_events_for_date: {e_day}")
                    import traceback
                    traceback.print_exc()
                    raise
            else:
                # Múltiplos dias
                print(f"[CALENDAR] Buscando eventos de MÚLTIPLOS dias")
                try:
                    events_by_date = CalendarQueryService._get_events_for_period(service, start_date, end_date)
                    total_events = sum(len(events) for events in events_by_date.values())
                    print(f"[CALENDAR] Encontrados {total_events} eventos em {len(events_by_date)} dias")
                    return GoogleCalendarService.format_period_events_for_whatsapp(events_by_date, start_date, end_date)
                except Exception as e_period:
                    print(f"[CALENDAR] ❌ Erro específico em _get_events_for_period: {e_period}")
                    import traceback
                    traceback.print_exc()
                    raise
        
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
        """Busca eventos de um dia usando serviço OAuth"""
        try:
            # A API do Google espera strings RFC3339.
            # Estas strings já estão no formato correto, definidas na rota admin/debug,
            # portanto, a chamada à API está correta.
            date_str = target_date.strftime('%Y-%m-%d')
            start_iso = f"{date_str}T00:00:00Z"
            end_iso = f"{date_str}T23:59:59Z"

            print(f"[CALENDAR] Buscando de {start_iso} até {end_iso}")

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
                    # --- CORREÇÃO DE PROCESSAMENTO DE START/END TIME ---
                    # Pega a string. Pode ser dateTime (aware) ou date (naive)
                    start = event['start'].get('dateTime', event['start'].get('date'))
                    end = event['end'].get('dateTime', event['end'].get('date'))

                    formatted_events.append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start, # Mantemos a string bruta para formatação
                        'end': end,
                        'location': event.get('location', ''),
                        'description': event.get('description', ''),
                        'all_day': 'date' in event['start']
                    })
                except Exception as e:
                    # O erro de timezone não deve mais acontecer aqui se a correção #1 estiver OK,
                    # mas mantemos o try/except para eventos malformados.
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
        """Busca eventos de um período usando serviço OAuth"""
        try:
            from datetime import time
            
            # Início do período (00:00:00 UTC)
            start_dt = datetime.combine(start_date, time.min)
            start_datetime = start_dt.replace(tzinfo=timezone.utc)
            
            # Fim do período (23:59:59 UTC)
            end_dt = datetime.combine(end_date, time.max)
            end_datetime = end_dt.replace(tzinfo=timezone.utc)
            
            # Converter para ISO string
            start_iso = start_datetime.isoformat()
            end_iso = end_datetime.isoformat()
            
            print(f"[CALENDAR] Buscando período de {start_iso} até {end_iso}")
            
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
                    
                    # --- CORREÇÃO DE PROCESSAMENTO DE START TIME PARA AGRUPAMENTO ---
                    if 'T' in start_str:
                        # Se tem T, é dateTime (aware ou naive). Usamos fromisoformat para analisar
                        event_dt = datetime.fromisoformat(start_str)
                        
                        # Se for naive, o que é comum se o fuso horário for removido na persistência,
                        # o marcamos como UTC para fins de processamento interno/comparação.
                        if event_dt.tzinfo is None:
                             event_dt = event_dt.replace(tzinfo=timezone.utc)
    
                        # Extraímos a data (date object, que é sempre naive, bom para chaves de dicionário)
                        event_date = event_dt.date()
                    else:
                        # Date sem hora (evento de dia inteiro)
                        event_date = date.fromisoformat(start_str)
                    
                    # --- FIM DA CORREÇÃO ---
    
                    if event_date not in events_by_date:
                        events_by_date[event_date] = []
                    
                    events_by_date[event_date].append({
                        'summary': event.get('summary', 'Sem título'),
                        'start': start_str, # Mantemos a string bruta
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