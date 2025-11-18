import json
import os
from datetime import datetime, date, timedelta
from google.oauth2.credentials import Credentials
from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.errors import HttpError

class GoogleCalendarService:
    """Serviço para integração com Google Calendar"""
    
    def __init__(self):
        self.service = None
        self._initialize_service()
    
    def _initialize_service(self):
        """Inicializa o serviço do Google Calendar"""
        try:
            # Opção 1: Service Account (recomendado para servidor)
            # Você precisa criar uma Service Account no Google Cloud Console
            credentials_json = os.environ.get('GOOGLE_CALENDAR_CREDENTIALS_JSON')
            
            if not credentials_json:
                print("[CALENDAR] ⚠️ Credenciais do Google Calendar não configuradas")
                return
            
            # Parse das credenciais
            credentials_dict = json.loads(credentials_json)
            
            # Criar credenciais de Service Account
            credentials = service_account.Credentials.from_service_account_info(
                credentials_dict,
                scopes=['https://www.googleapis.com/auth/calendar.readonly']
            )
            
            # Criar serviço
            self.service = build('calendar', 'v3', credentials=credentials)
            print("[CALENDAR] ✅ Google Calendar conectado com sucesso!")
            
        except Exception as e:
            print(f"[CALENDAR] ❌ Erro ao conectar Google Calendar: {e}")
            self.service = None
    
    def is_connected(self):
        """Verifica se está conectado"""
        return self.service is not None
    
    def get_events_for_date(self, target_date, calendar_id='primary'):
        """
        Busca eventos de uma data específica.
        
        Args:
            target_date: date object
            calendar_id: ID do calendário (padrão: 'primary')
        
        Returns:
            Lista de eventos: [{'summary': '...', 'start': '...', 'end': '...', 'location': '...'}, ...]
        """
        if not self.is_connected():
            raise Exception("Google Calendar não está conectado")
        
        try:
            # Definir início e fim do dia
            start_of_day = datetime.combine(target_date, datetime.min.time()).isoformat() + 'Z'
            end_of_day = datetime.combine(target_date, datetime.max.time()).isoformat() + 'Z'
            
            # Buscar eventos
            events_result = self.service.events().list(
                calendarId=calendar_id,
                timeMin=start_of_day,
                timeMax=end_of_day,
                singleEvents=True,
                orderBy='startTime'
            ).execute()
            
            events = events_result.get('items', [])
            
            # Formatar eventos
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
                    'all_day': 'date' in event['start']  # Evento de dia inteiro
                })
            
            return formatted_events
            
        except HttpError as error:
            print(f"[CALENDAR] Erro ao buscar eventos: {error}")
            raise Exception(f"Erro ao acessar Google Calendar: {error}")
    
    def get_events_for_period(self, start_date, end_date, calendar_id='primary'):
        """
        Busca eventos de um período (múltiplos dias).
        
        Args:
            start_date: date object (início)
            end_date: date object (fim, inclusivo)
            calendar_id: ID do calendário
        
        Returns:
            Lista de eventos agrupados por data
        """
        if not self.is_connected():
            raise Exception("Google Calendar não está conectado")
        
        try:
            start_datetime = datetime.combine(start_date, datetime.min.time()).isoformat() + 'Z'
            end_datetime = datetime.combine(end_date, datetime.max.time()).isoformat() + 'Z'
            
            events_result = self.service.events().list(
                calendarId=calendar_id,
                timeMin=start_datetime,
                timeMax=end_datetime,
                singleEvents=True,
                orderBy='startTime'
            ).execute()
            
            events = events_result.get('items', [])
            
            # Agrupar por data
            events_by_date = {}
            
            for event in events:
                start = event['start'].get('dateTime', event['start'].get('date'))
                
                # Extrair data
                if 'T' in start:
                    event_date = datetime.fromisoformat(start.replace('Z', '')).date()
                else:
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
            
        except HttpError as error:
            print(f"[CALENDAR] Erro ao buscar eventos: {error}")
            raise Exception(f"Erro ao acessar Google Calendar: {error}")
    
    @staticmethod
    def format_time(time_str):
        """Formata hora para exibição (HH:MM)"""
        if not time_str:
            return ""
        
        try:
            if 'T' in time_str:
                dt = datetime.fromisoformat(time_str.replace('Z', ''))
                return dt.strftime('%H:%M')
            return ""
        except:
            return ""
    
    @staticmethod
    def format_events_for_whatsapp(events, target_date):
        """
        Formata eventos para mensagem do WhatsApp.
        
        Args:
            events: Lista de eventos
            target_date: date object (para contexto)
        
        Returns:
            Mensagem formatada
        """
        if not events:
            date_str = target_date.strftime('%d/%m/%Y')
            
            # Verificar se é hoje, amanhã, etc
            hoje = date.today()
            if target_date == hoje:
                desc = "hoje"
            elif target_date == hoje + timedelta(days=1):
                desc = "amanhã"
            else:
                desc = f"no dia {date_str}"
            
            return f"📅 Você não tem compromissos agendados {desc}! 🎉"
        
        # Header
        date_str = target_date.strftime('%d/%m/%Y (%A)')
        mensagem = f"📅 *AGENDA - {date_str.upper()}* 📅\n\n"
        mensagem += f"📊 Total de compromissos: *{len(events)}*\n\n"
        mensagem += "━━━━━━━━━━━━━━━━━━━━\n\n"
        
        # Listar eventos
        for idx, event in enumerate(events, 1):
            emoji = "🔵" if not event['all_day'] else "📆"
            
            mensagem += f"{emoji} *{event['summary']}*\n"
            
            if event['all_day']:
                mensagem += "   ⏰ Dia inteiro\n"
            else:
                start_time = GoogleCalendarService.format_time(event['start'])
                end_time = GoogleCalendarService.format_time(event['end'])
                mensagem += f"   ⏰ {start_time} - {end_time}\n"
            
            if event['location']:
                mensagem += f"   📍 {event['location']}\n"
            
            if event['description']:
                # Limitar descrição a 50 caracteres
                desc = event['description'][:50]
                if len(event['description']) > 50:
                    desc += "..."
                mensagem += f"   📝 {desc}\n"
            
            mensagem += "\n"
        
        return mensagem.strip()
    
    @staticmethod
    def format_period_events_for_whatsapp(events_by_date, start_date, end_date):
        """
        Formata eventos de um período para WhatsApp.
        
        Args:
            events_by_date: Dict {date: [eventos]}
            start_date, end_date: Período
        
        Returns:
            Mensagem formatada
        """
        if not events_by_date:
            return f"📅 Você não tem compromissos agendados neste período! 🎉"
        
        total_events = sum(len(events) for events in events_by_date.values())
        
        mensagem = f"📅 *AGENDA - {start_date.strftime('%d/%m')} a {end_date.strftime('%d/%m/%Y')}* 📅\n\n"
        mensagem += f"📊 Total: *{total_events} compromisso(s)* em *{len(events_by_date)} dia(s)*\n\n"
        mensagem += "━━━━━━━━━━━━━━━━━━━━\n\n"
        
        # Ordenar datas
        sorted_dates = sorted(events_by_date.keys())
        
        for event_date in sorted_dates:
            events = events_by_date[event_date]
            
            # Header do dia
            day_str = event_date.strftime('%d/%m (%a)')
            mensagem += f"📆 *{day_str}* - {len(events)} compromisso(s)\n"
            
            for event in events:
                if event['all_day']:
                    mensagem += f"  • {event['summary']} (dia inteiro)\n"
                else:
                    start_time = GoogleCalendarService.format_time(event['start'])
                    mensagem += f"  • {start_time} - {event['summary']}\n"
            
            mensagem += "\n"
        
        return mensagem.strip()