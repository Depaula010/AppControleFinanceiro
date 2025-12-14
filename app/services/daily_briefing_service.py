# app/services/daily_briefing_service.py
"""
Serviço de Resumo Inteligente de Compromissos (Daily Briefing)
Gera resumo matinal humanizado da agenda com IA
"""

from datetime import datetime, date, timedelta
from typing import Dict, List, Optional
from sqlalchemy import text

from app import db_engine
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.weather_service import WeatherService


class DailyBriefingService:
    """Serviço para gerar resumo inteligente da agenda diária"""

    def __init__(self):
        self.calendar_service = GoogleCalendarOAuthService()
        self.weather_service = WeatherService()

    def get_user_location(self, usuario_id: int) -> tuple:
        """
        Obtém a localização configurada do usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            tuple: (cidade, estado) ou (None, None)
        """
        if not db_engine:
            return None, None

        sql = text("""
            SELECT cidade, estado
            FROM Usuarios
            WHERE id = :uid
        """)

        try:
            with db_engine.connect() as conn:
                result = conn.execute(sql, {"uid": usuario_id}).fetchone()

                if result:
                    return result.cidade, result.estado
                return None, None

        except Exception as e:
            print(f"[BRIEFING] Erro ao buscar localização: {e}")
            return None, None

    def extract_event_locations(self, events: List[Dict]) -> List[tuple]:
        """
        Extrai localizações únicas dos eventos do dia.

        Args:
            events: Lista de eventos do Google Calendar

        Returns:
            list: Lista de tuplas [(cidade, estado), ...]
        """
        locations = set()

        for event in events:
            location = event.get('location', '').strip()

            if not location:
                continue

            # Tentar extrair cidade de formatos comuns
            # Ex: "Campinas, SP", "Shopping Iguatemi, Campinas"
            # Simplificado: pegar última cidade mencionada antes de vírgula

            parts = [p.strip() for p in location.split(',')]

            # Procurar por sigla de estado brasileiro (2 letras maiúsculas)
            estado = None
            for part in reversed(parts):
                if len(part) == 2 and part.isupper():
                    estado = part
                    break

            # Cidade: parte antes do estado ou última parte
            if estado and len(parts) >= 2:
                # Pegar parte antes do estado
                idx = parts.index(estado)
                if idx > 0:
                    cidade = parts[idx - 1]
                    locations.add((cidade, estado))
            else:
                # Tentar pegar cidade do texto (básico)
                # Para simplificar, vamos apenas registrar que há localização
                # mas não conseguimos extrair cidade específica
                pass

        return list(locations)

    def calculate_time_gaps(self, events: List[Dict]) -> List[Dict]:
        """
        Calcula intervalos de tempo livre entre eventos.

        Args:
            events: Lista de eventos ordenados por horário

        Returns:
            list: [{'inicio': '10:00', 'fim': '14:00', 'duracao_minutos': 240}, ...]
        """
        gaps = []

        # Filtrar eventos com horário (ignorar dia inteiro)
        timed_events = [e for e in events if not e.get('all_day', False)]

        if len(timed_events) < 2:
            return gaps

        for i in range(len(timed_events) - 1):
            current_event = timed_events[i]
            next_event = timed_events[i + 1]

            # Pegar horário de término do evento atual
            current_end_str = current_event.get('end', '')
            next_start_str = next_event.get('start', '')

            if not current_end_str or not next_start_str:
                continue

            try:
                # Parsear horários (formato ISO: 2025-11-22T14:00:00-03:00)
                current_end = datetime.fromisoformat(current_end_str.replace('Z', '+00:00'))
                next_start = datetime.fromisoformat(next_start_str.replace('Z', '+00:00'))

                # Calcular diferença
                gap_duration = (next_start - current_end).total_seconds() / 60

                # Considerar apenas intervalos >= 30 minutos
                if gap_duration >= 30:
                    gaps.append({
                        'inicio': current_end.strftime('%H:%M'),
                        'fim': next_start.strftime('%H:%M'),
                        'duracao_minutos': int(gap_duration)
                    })

            except Exception as e:
                print(f"[BRIEFING] Erro ao calcular gap: {e}")
                continue

        return gaps

    def detect_event_type(self, event: Dict) -> str:
        """
        Detecta tipo do evento (remoto, presencial, etc).

        Args:
            event: Dados do evento

        Returns:
            str: 'remoto', 'presencial', ou ''
        """
        title = event.get('summary', '').lower()
        description = event.get('description', '').lower()
        location = event.get('location', '').lower()

        # Indicadores de evento remoto
        remote_keywords = [
            'meet', 'zoom', 'teams', 'online', 'remoto', 'virtual',
            'video', 'chamada', 'call', 'reunião online'
        ]

        # Verificar em título, descrição e localização
        all_text = f"{title} {description} {location}"

        for keyword in remote_keywords:
            if keyword in all_text:
                return 'remoto'

        # Se tem localização física, presumir presencial
        if event.get('location'):
            return 'presencial'

        return ''

    def get_financial_alerts(self, usuario_id: int, target_date: date) -> Dict:
        """
        Busca contas fixas e faturas que vencem hoje ou amanhã.

        Args:
            usuario_id: ID do usuário
            target_date: Data de referência

        Returns:
            dict: Dicionário com alertas financeiros
        """
        from app.services.finance_service import get_upcoming_bills_and_invoices

        try:
            with db_engine.connect() as conn:
                return get_upcoming_bills_and_invoices(conn, usuario_id, target_date)
        except Exception as e:
            print(f"[BRIEFING] Erro ao buscar alertas financeiros: {e}")
            return {
                'contas_hoje': [],
                'contas_amanha': [],
                'faturas_hoje': [],
                'faturas_amanha': []
            }

    def prepare_briefing_data(self, usuario_id: int, target_date: date = None) -> Dict:
        """
        Prepara dados para o resumo matinal.

        Args:
            usuario_id: ID do usuário
            target_date: Data alvo (padrão: hoje)

        Returns:
            dict: {
                'eventos': [...],
                'clima_principal': {...},
                'climas_adicionais': [...],
                'gaps': [...],
                'total_eventos': int,
                'eventos_remotos': int,
                'eventos_presenciais': int,
                'alertas_financeiros': {...}
            }
        """
        if target_date is None:
            target_date = date.today()

        # 1. Buscar eventos do dia
        eventos_result = self.calendar_service.get_events_for_date(usuario_id, target_date)

        if not eventos_result['success']:
            print(f"[BRIEFING] Erro ao buscar eventos: {eventos_result.get('error')}")
            return None

        eventos = eventos_result.get('events', [])

        # 2. Buscar clima da localização principal
        cidade, estado = self.get_user_location(usuario_id)
        clima_principal = None

        if cidade:
            clima_principal = self.weather_service.get_weather(cidade, estado)

        # 3. Detectar localizações adicionais nos eventos
        event_locations = self.extract_event_locations(eventos)
        climas_adicionais = []

        for loc_cidade, loc_estado in event_locations:
            # Evitar duplicar clima da cidade principal
            if loc_cidade == cidade and loc_estado == estado:
                continue

            clima = self.weather_service.get_weather(loc_cidade, loc_estado)
            if clima:
                climas_adicionais.append({
                    'cidade': loc_cidade,
                    'estado': loc_estado,
                    'clima': clima
                })

        # 4. Calcular intervalos de tempo livre
        gaps = self.calculate_time_gaps(eventos)

        # 5. Analisar tipos de eventos
        eventos_remotos = 0
        eventos_presenciais = 0

        for event in eventos:
            tipo = self.detect_event_type(event)
            if tipo == 'remoto':
                eventos_remotos += 1
            elif tipo == 'presencial':
                eventos_presenciais += 1

        # 6. Buscar alertas financeiros (contas e faturas próximas ao vencimento)
        alertas_financeiros = self.get_financial_alerts(usuario_id, target_date)

        return {
            'eventos': eventos,
            'clima_principal': clima_principal,
            'climas_adicionais': climas_adicionais,
            'gaps': gaps,
            'total_eventos': len(eventos),
            'eventos_remotos': eventos_remotos,
            'eventos_presenciais': eventos_presenciais,
            'alertas_financeiros': alertas_financeiros,
            'data': target_date
        }

    def format_event_for_gemini(self, event: Dict) -> str:
        """
        Formata um evento para enviar ao Gemini.

        Args:
            event: Dados do evento

        Returns:
            str: Evento formatado
        """
        lines = []

        # Título
        lines.append(f"Evento: {event['summary']}")

        # Horário
        if event.get('all_day'):
            lines.append("Horário: Dia inteiro")
        else:
            start = event.get('start', '')
            end = event.get('end', '')

            if start:
                try:
                    start_dt = datetime.fromisoformat(start.replace('Z', '+00:00'))
                    start_time = start_dt.strftime('%H:%M')
                    lines.append(f"Início: {start_time}")

                    if end:
                        end_dt = datetime.fromisoformat(end.replace('Z', '+00:00'))
                        end_time = end_dt.strftime('%H:%M')
                        duration_min = int((end_dt - start_dt).total_seconds() / 60)
                        lines.append(f"Fim: {end_time} (duração: {duration_min} min)")
                except:
                    pass

        # Localização
        if event.get('location'):
            tipo = self.detect_event_type(event)
            tipo_str = f" [{tipo}]" if tipo else ""
            lines.append(f"Local: {event['location']}{tipo_str}")

        # Descrição
        if event.get('description'):
            desc = event['description'][:100]  # Limitar tamanho
            lines.append(f"Descrição: {desc}")

        return "\n".join(lines)

    def generate_briefing_message(self, usuario_id: int, target_date: date = None) -> Optional[str]:
        """
        Gera mensagem completa do resumo matinal (será processada pelo Gemini).

        Args:
            usuario_id: ID do usuário
            target_date: Data alvo

        Returns:
            str: Mensagem formatada ou None se erro
        """
        # Preparar dados
        briefing_data = self.prepare_briefing_data(usuario_id, target_date)

        if not briefing_data:
            return None

        # Se não há eventos, retornar mensagem simples (mas incluir alertas financeiros se houver)
        if briefing_data['total_eventos'] == 0:
            cidade, estado = self.get_user_location(usuario_id)
            clima = briefing_data.get('clima_principal')

            msg = "☀️ Bom dia! Você não tem compromissos agendados para hoje! 🎉\n\n"

            if clima:
                msg += self.weather_service.format_weather_for_briefing(clima)

            # Adicionar alertas financeiros se houver
            alertas = briefing_data.get('alertas_financeiros', {})
            alertas_msg = self._format_financial_alerts(alertas)
            if alertas_msg:
                msg += "\n\n" + alertas_msg

            return msg

        # Caso contrário, os dados serão enviados ao Gemini
        # (implementaremos isso no próximo passo)
        return briefing_data

    def _format_financial_alerts(self, alertas: Dict) -> str:
        """
        Formata alertas financeiros para exibição.

        Args:
            alertas: Dicionário com alertas financeiros

        Returns:
            str: Mensagem formatada ou string vazia
        """
        contas_hoje = alertas.get('contas_hoje', [])
        contas_amanha = alertas.get('contas_amanha', [])
        faturas_hoje = alertas.get('faturas_hoje', [])
        faturas_amanha = alertas.get('faturas_amanha', [])

        tem_alertas = any([contas_hoje, contas_amanha, faturas_hoje, faturas_amanha])

        if not tem_alertas:
            return ""

        msg_parts = ["💰 *ALERTAS FINANCEIROS*"]

        # Vencimentos de hoje
        if contas_hoje or faturas_hoje:
            # Separar receitas e despesas
            despesas_hoje = [c for c in contas_hoje if c.get('tipo') == 'Despesa']
            receitas_hoje = [c for c in contas_hoje if c.get('tipo') == 'Receita']

            if despesas_hoje or faturas_hoje:
                msg_parts.append("\n⚠️ *VENCE HOJE (Despesas):*")
                for conta in despesas_hoje:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")
                for fatura in faturas_hoje:
                    valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

            if receitas_hoje:
                msg_parts.append("\n💵 *VENCE HOJE (Receitas):*")
                for conta in receitas_hoje:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        # Vencimentos de amanhã
        if contas_amanha or faturas_amanha:
            # Separar receitas e despesas
            despesas_amanha = [c for c in contas_amanha if c.get('tipo') == 'Despesa']
            receitas_amanha = [c for c in contas_amanha if c.get('tipo') == 'Receita']

            if despesas_amanha or faturas_amanha:
                msg_parts.append("\n🔔 *VENCE AMANHÃ (Despesas):*")
                for conta in despesas_amanha:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")
                for fatura in faturas_amanha:
                    valor_formatado = f"{fatura['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• Fatura {fatura['cartao']} - R$ {valor_formatado}")

            if receitas_amanha:
                msg_parts.append("\n💰 *VENCE AMANHÃ (Receitas):*")
                for conta in receitas_amanha:
                    valor_formatado = f"{conta['valor']:.2f}".replace('.', ',')
                    msg_parts.append(f"• {conta['descricao']} - R$ {valor_formatado}")

        return "\n".join(msg_parts)
