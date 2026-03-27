import re
from datetime import datetime, date, timedelta

class GoogleCalendarService:
    """Serviço para formatação de eventos do Google Calendar"""
    
    def __init__(self):
        """
        NOTA: Este serviço agora é apenas para formatação.
        O acesso à API é feito via GoogleCalendarOAuthService.
        """
        pass
    
    @staticmethod
    def strip_html(text):
        """Remove tags HTML e normaliza espaços em branco."""
        if not text:
            return text
        text = re.sub(r'<[^>]+>', ' ', text)
        text = re.sub(r'&nbsp;', ' ', text)
        text = re.sub(r'&amp;', '&', text)
        text = re.sub(r'&lt;', '<', text)
        text = re.sub(r'&gt;', '>', text)
        text = re.sub(r'[ \t]+', ' ', text)
        text = re.sub(r'\n{3,}', '\n\n', text)
        return text.strip()

    @staticmethod
    def format_time(time_str):
        """
        Formata hora para exibição (HH:MM).
        SEM COMPARAÇÕES DE DATETIME - apenas parsing de string.
        """
        if not time_str:
            return ""
        
        try:
            # Se tem 'T', é datetime ISO
            if 'T' in time_str:
                # Extrair apenas a parte de hora (HH:MM)
                time_part = time_str.split('T')[1]
                
                # Remover timezone (Z ou +XX:XX)
                if 'Z' in time_part:
                    time_part = time_part.split('Z')[0]
                elif '+' in time_part:
                    time_part = time_part.split('+')[0]
                elif '-' in time_part and ':' in time_part.split('-')[-1]:
                    # Timezone negativo (ex: -03:00)
                    time_part = '-'.join(time_part.split('-')[:-1])
                
                # Pegar apenas HH:MM
                return time_part[:5]
            
            return ""
        except Exception as e:
            print(f"[CALENDAR-FORMAT] Erro ao formatar hora '{time_str}': {e}")
            return ""
    
    @staticmethod
    def format_events_for_whatsapp(events, target_date):
        """
        Formata eventos de UM DIA para mensagem do WhatsApp.
        SEM COMPARAÇÕES DE DATETIME.
        
        Args:
            events: Lista de eventos (dicts)
            target_date: date object
        
        Returns:
            Mensagem formatada
        """
        if not events:
            # Verificar se é hoje, amanhã, etc (comparação de DATE, não datetime)
            hoje = date.today()
            
            if target_date == hoje:
                desc = "hoje"
            elif target_date == hoje + timedelta(days=1):
                desc = "amanhã"
            else:
                desc = f"no dia {target_date.strftime('%d/%m/%Y')}"
            
            return f"📅 Você não tem compromissos agendados {desc}! 🎉"
        
        # Header - usar apenas date.strftime (sem datetime)
        dia_semana_map = {
            0: 'Segunda-feira',
            1: 'Terça-feira',
            2: 'Quarta-feira',
            3: 'Quinta-feira',
            4: 'Sexta-feira',
            5: 'Sábado',
            6: 'Domingo'
        }
        
        dia_semana = dia_semana_map.get(target_date.weekday(), '')
        date_str = target_date.strftime('%d/%m/%Y')
        
        mensagem = f"📅 *AGENDA - {date_str} ({dia_semana.upper()})* 📅\n\n"
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
                
                if start_time and end_time:
                    mensagem += f"   ⏰ {start_time} - {end_time}\n"
                elif start_time:
                    mensagem += f"   ⏰ {start_time}\n"
            
            if event['location']:
                mensagem += f"   📍 {event['location']}\n"

            if event['description']:
                desc = GoogleCalendarService.strip_html(event['description'])
                if desc:
                    mensagem += f"   📝 {desc}\n"
            
            mensagem += "\n"
        
        return mensagem.strip()
    
    @staticmethod
    def format_period_events_for_whatsapp(events_by_date, start_date, end_date):
        """
        Formata eventos de MÚLTIPLOS DIAS para WhatsApp.
        SEM COMPARAÇÕES DE DATETIME.
        
        Args:
            events_by_date: Dict {date: [eventos]}
            start_date, end_date: date objects
        
        Returns:
            Mensagem formatada
        """
        if not events_by_date:
            return f"📅 Você não tem compromissos agendados neste período! 🎉"
        
        total_events = sum(len(events) for events in events_by_date.values())
        
        mensagem = f"📅 *AGENDA - {start_date.strftime('%d/%m')} a {end_date.strftime('%d/%m/%Y')}* 📅\n\n"
        mensagem += f"📊 Total: *{total_events} compromisso(s)* em *{len(events_by_date)} dia(s)*\n\n"
        mensagem += "━━━━━━━━━━━━━━━━━━━━\n\n"
        
        # Mapa de dias da semana
        dia_semana_map = {
            0: 'Seg', 1: 'Ter', 2: 'Qua',
            3: 'Qui', 4: 'Sex', 5: 'Sáb', 6: 'Dom'
        }
        
        # Ordenar datas (comparação de date, não datetime)
        sorted_dates = sorted(events_by_date.keys())
        
        for event_date in sorted_dates:
            events = events_by_date[event_date]
            
            # Header do dia
            dia_semana = dia_semana_map.get(event_date.weekday(), '')
            day_str = f"{event_date.strftime('%d/%m')} ({dia_semana})"
            
            mensagem += f"📆 *{day_str}* - {len(events)} compromisso(s)\n"
            
            for event in events:
                if event['all_day']:
                    mensagem += f"  • {event['summary']} (dia inteiro)\n"
                else:
                    start_time = GoogleCalendarService.format_time(event['start'])
                    if start_time:
                        mensagem += f"  • {start_time} - {event['summary']}\n"
                    else:
                        mensagem += f"  • {event['summary']}\n"
            
            mensagem += "\n"
        
        return mensagem.strip()