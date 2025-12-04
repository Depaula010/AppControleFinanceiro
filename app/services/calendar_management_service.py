# app/services/calendar_management_service.py
"""
Serviço para GERENCIAR eventos do Google Calendar (criar, editar, excluir)
"""

from datetime import datetime, date, time, timedelta
from zoneinfo import ZoneInfo
from app.services.google_calendar_oauth_service import GoogleOAuthService

class CalendarManagementService:
    """Gerencia criação, edição e exclusão de eventos"""
    
    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
    
    @staticmethod
    def create_event(usuario_id, titulo, data_evento, hora_inicio=None, hora_fim=None, 
                     descricao=None, localizacao=None, calendario_id='primary'):
        """
        Cria um novo evento no Google Calendar.
        
        Args:
            usuario_id: ID do usuário
            titulo: Título do evento
            data_evento: date object ou string 'YYYY-MM-DD'
            hora_inicio: time object, string 'HH:MM', ou None (dia inteiro)
            hora_fim: time object, string 'HH:MM', ou None
            descricao: Descrição opcional
            localizacao: Local opcional
            calendario_id: ID do calendário (default: 'primary')
        
        Returns:
            (sucesso: bool, mensagem: str, event_id: str)
        """
        print(f"[CALENDAR-CREATE] Criando evento para usuário {usuario_id}")
        
        try:
            # Obter serviço
            service = GoogleOAuthService.get_calendar_service(usuario_id)
            
            # Processar data
            if isinstance(data_evento, str):
                data_evento = date.fromisoformat(data_evento)
            
            # Montar evento
            event_body = {
                'summary': titulo,
            }
            
            # Adicionar descrição e localização se fornecidos
            if descricao:
                event_body['description'] = descricao
            if localizacao:
                event_body['location'] = localizacao
            
            # Determinar se é evento de dia inteiro ou com horário
            if hora_inicio is None:
                # Evento de dia inteiro
                event_body['start'] = {
                    'date': data_evento.isoformat(),
                    'timeZone': 'America/Sao_Paulo'
                }
                event_body['end'] = {
                    'date': (data_evento + timedelta(days=1)).isoformat(),
                    'timeZone': 'America/Sao_Paulo'
                }
                print(f"[CALENDAR-CREATE] Evento de dia inteiro: {data_evento}")
            else:
                # Evento com horário
                # Processar hora_inicio
                if isinstance(hora_inicio, str):
                    hora_inicio = datetime.strptime(hora_inicio, '%H:%M').time()
                
                # Processar hora_fim
                if hora_fim is None:
                    # Se não forneceu fim, adicionar 1 hora
                    hora_fim = (datetime.combine(date.today(), hora_inicio) + timedelta(hours=1)).time()
                elif isinstance(hora_fim, str):
                    hora_fim = datetime.strptime(hora_fim, '%H:%M').time()
                
                # Criar datetime com timezone
                start_dt = datetime.combine(data_evento, hora_inicio).replace(tzinfo=CalendarManagementService.TIMEZONE_BR)
                end_dt = datetime.combine(data_evento, hora_fim).replace(tzinfo=CalendarManagementService.TIMEZONE_BR)
                
                event_body['start'] = {
                    'dateTime': start_dt.isoformat(),
                    'timeZone': 'America/Sao_Paulo'
                }
                event_body['end'] = {
                    'dateTime': end_dt.isoformat(),
                    'timeZone': 'America/Sao_Paulo'
                }
                
                print(f"[CALENDAR-CREATE] Evento com horário: {start_dt} até {end_dt}")
            
            # Criar evento via API
            created_event = service.events().insert(
                calendarId=calendario_id,
                body=event_body
            ).execute()
            
            event_id = created_event.get('id')
            event_link = created_event.get('htmlLink')
            
            print(f"[CALENDAR-CREATE] ✅ Evento criado: {event_id}")
            
            return True, f"Evento '{titulo}' criado com sucesso!", event_id
            
        except Exception as e:
            print(f"[CALENDAR-CREATE] ❌ Erro: {e}")
            import traceback
            traceback.print_exc()
            return False, f"Erro ao criar evento: {str(e)}", None
    
    @staticmethod
    def delete_event(usuario_id, event_id, calendario_id='primary'):
        """
        Deleta um evento do Google Calendar.
        
        Args:
            usuario_id: ID do usuário
            event_id: ID do evento a deletar
            calendario_id: ID do calendário
        
        Returns:
            (sucesso: bool, mensagem: str)
        """
        print(f"[CALENDAR-DELETE] Deletando evento {event_id} do usuário {usuario_id}")
        
        try:
            service = GoogleOAuthService.get_calendar_service(usuario_id)
            
            # Primeiro, buscar o evento para pegar o título (para mensagem)
            try:
                event = service.events().get(
                    calendarId=calendario_id,
                    eventId=event_id
                ).execute()
                
                event_title = event.get('summary', 'Sem título')
            except:
                event_title = "Evento"
            
            # Deletar evento
            service.events().delete(
                calendarId=calendario_id,
                eventId=event_id
            ).execute()
            
            print(f"[CALENDAR-DELETE] ✅ Evento deletado: {event_id}")
            
            return True, f"Evento '{event_title}' deletado com sucesso!"
            
        except Exception as e:
            print(f"[CALENDAR-DELETE] ❌ Erro: {e}")
            
            if 'not found' in str(e).lower():
                return False, "Evento não encontrado ou já foi deletado."
            else:
                return False, f"Erro ao deletar evento: {str(e)}"
    
    @staticmethod
    def update_event(usuario_id, event_id, titulo=None, data_evento=None, 
                     hora_inicio=None, hora_fim=None, descricao=None, 
                     localizacao=None, calendario_id='primary'):
        """
        Atualiza um evento existente.
        
        Args:
            usuario_id: ID do usuário
            event_id: ID do evento a atualizar
            titulo: Novo título (None = manter atual)
            data_evento: Nova data (None = manter atual)
            hora_inicio: Nova hora início (None = manter atual)
            hora_fim: Nova hora fim (None = manter atual)
            descricao: Nova descrição (None = manter atual)
            localizacao: Novo local (None = manter atual)
            calendario_id: ID do calendário
        
        Returns:
            (sucesso: bool, mensagem: str)
        """
        print(f"[CALENDAR-UPDATE] Atualizando evento {event_id}")
        
        try:
            service = GoogleOAuthService.get_calendar_service(usuario_id)
            
            # Buscar evento atual
            event = service.events().get(
                calendarId=calendario_id,
                eventId=event_id
            ).execute()
            
            # Atualizar campos fornecidos
            if titulo:
                event['summary'] = titulo
            if descricao is not None:  # Permite limpar descrição com ""
                event['description'] = descricao
            if localizacao is not None:
                event['location'] = localizacao
            
            # Atualizar data/hora se fornecido
            if data_evento or hora_inicio or hora_fim:
                # Processar data
                if data_evento:
                    if isinstance(data_evento, str):
                        data_evento = date.fromisoformat(data_evento)
                else:
                    # Pegar data atual do evento
                    if 'date' in event['start']:
                        data_evento = date.fromisoformat(event['start']['date'])
                    else:
                        data_evento = datetime.fromisoformat(event['start']['dateTime']).date()
                
                # Se forneceu hora, criar datetime
                if hora_inicio:
                    if isinstance(hora_inicio, str):
                        hora_inicio = datetime.strptime(hora_inicio, '%H:%M').time()
                    
                    if hora_fim:
                        if isinstance(hora_fim, str):
                            hora_fim = datetime.strptime(hora_fim, '%H:%M').time()
                    else:
                        # Manter duração original ou adicionar 1h
                        hora_fim = (datetime.combine(date.today(), hora_inicio) + timedelta(hours=1)).time()
                    
                    start_dt = datetime.combine(data_evento, hora_inicio).replace(tzinfo=CalendarManagementService.TIMEZONE_BR)
                    end_dt = datetime.combine(data_evento, hora_fim).replace(tzinfo=CalendarManagementService.TIMEZONE_BR)
                    
                    event['start'] = {
                        'dateTime': start_dt.isoformat(),
                        'timeZone': 'America/Sao_Paulo'
                    }
                    event['end'] = {
                        'dateTime': end_dt.isoformat(),
                        'timeZone': 'America/Sao_Paulo'
                    }
            
            # Atualizar via API
            updated_event = service.events().update(
                calendarId=calendario_id,
                eventId=event_id,
                body=event
            ).execute()
            
            print(f"[CALENDAR-UPDATE] ✅ Evento atualizado: {event_id}")
            
            return True, f"Evento '{event['summary']}' atualizado com sucesso!"
            
        except Exception as e:
            print(f"[CALENDAR-UPDATE] ❌ Erro: {e}")
            return False, f"Erro ao atualizar evento: {str(e)}"
    
    @staticmethod
    def find_events_by_title(usuario_id, titulo_busca, max_results=10):
        """
        Busca eventos por título (para identificar qual deletar).
        
        Args:
            usuario_id: ID do usuário
            titulo_busca: Texto para buscar no título
            max_results: Máximo de resultados
        
        Returns:
            list: Lista de eventos encontrados com {id, summary, start, calendar}
        """
        print(f"[CALENDAR-SEARCH] Buscando eventos com título '{titulo_busca}'")
        
        try:
            service = GoogleOAuthService.get_calendar_service(usuario_id)
            
            # Buscar em todos os calendários
            from app.services.calendar_query_service import CalendarQueryService
            calendar_ids = CalendarQueryService._get_all_calendar_ids(service)
            
            found_events = []
            
            for calendar in calendar_ids:
                try:
                    # Buscar nos próximos 90 dias
                    now = datetime.now(CalendarManagementService.TIMEZONE_BR)
                    future = now + timedelta(days=90)
                    
                    events_result = service.events().list(
                        calendarId=calendar['id'],
                        timeMin=now.isoformat(),
                        timeMax=future.isoformat(),
                        q=titulo_busca,  # Query de busca
                        maxResults=max_results,
                        singleEvents=True,
                        orderBy='startTime'
                    ).execute()
                    
                    for event in events_result.get('items', []):
                        found_events.append({
                            'id': event['id'],
                            'summary': event.get('summary', 'Sem título'),
                            'start': event['start'].get('dateTime', event['start'].get('date')),
                            'calendar_id': calendar['id'],
                            'calendar_name': calendar['summary']
                        })
                
                except Exception as e:
                    print(f"[CALENDAR-SEARCH] Erro em {calendar['summary']}: {e}")
                    continue
            
            print(f"[CALENDAR-SEARCH] Encontrados {len(found_events)} eventos")
            return found_events
            
        except Exception as e:
            print(f"[CALENDAR-SEARCH] ❌ Erro: {e}")
            return []