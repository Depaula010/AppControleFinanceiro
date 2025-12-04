# app/services/user_schedule_pattern_service.py
from datetime import datetime, timedelta
from zoneinfo import ZoneInfo
from collections import defaultdict
from sqlalchemy import text

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


class UserSchedulePatternService:
    '''
    Analisa padrões de agendamento do usuário baseado no histórico
    para sugerir melhores horários.
    '''

    @staticmethod
    def analyze_user_patterns(db_engine, usuario_id, lookback_days=90):
        '''
        Analisa o histórico de eventos do usuário para identificar padrões.

        Returns:
            {
                "horario_mais_cedo": "09:00",  # Horário mais cedo que costuma marcar
                "horario_mais_tarde": "20:00",  # Horário mais tarde
                "dias_mais_ocupados": ["segunda", "quarta"],
                "periodos_preferidos": ["manha", "tarde"],  # manhã, tarde, noite
                "duracao_media_eventos": 60,  # minutos
                "total_eventos_analisados": 45
            }
        '''
        with db_engine.connect() as conn:
            # Buscar tokens do usuário para verificar se tem Calendar conectado
            token_query = text("""
                SELECT usuario_id FROM GoogleCalendarTokens
                WHERE usuario_id = :usuario_id
            """)
            token_result = conn.execute(token_query, {"usuario_id": usuario_id}).fetchone()

            if not token_result:
                print(f"[PATTERN] Usuário {usuario_id} não tem Calendar conectado")
                return UserSchedulePatternService._get_default_patterns()

        # Buscar eventos dos últimos N dias via Google Calendar API
        from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService

        try:
            service = GoogleCalendarOAuthService.get_calendar_service(usuario_id)
        except Exception as e:
            print(f"[PATTERN] Erro ao obter Calendar service: {e}")
            return UserSchedulePatternService._get_default_patterns()

        # Definir range de busca
        now = datetime.now(TIMEZONE_BR)
        start_date = now - timedelta(days=lookback_days)

        start_iso = start_date.isoformat()
        end_iso = now.isoformat()

        # Buscar eventos de todos os calendários
        all_events = []
        try:
            calendar_list = service.calendarList().list().execute()
            calendars = calendar_list.get('items', [])

            for cal in calendars:
                if not cal.get('selected', False):
                    continue

                events_result = service.events().list(
                    calendarId=cal['id'],
                    timeMin=start_iso,
                    timeMax=end_iso,
                    singleEvents=True,
                    orderBy='startTime',
                    maxResults=500
                ).execute()

                all_events.extend(events_result.get('items', []))

        except Exception as e:
            print(f"[PATTERN] Erro ao buscar eventos: {e}")
            return UserSchedulePatternService._get_default_patterns()

        if not all_events:
            print(f"[PATTERN] Nenhum evento encontrado nos últimos {lookback_days} dias")
            return UserSchedulePatternService._get_default_patterns()

        # Analisar padrões
        return UserSchedulePatternService._extract_patterns_from_events(all_events)

    @staticmethod
    def _extract_patterns_from_events(events):
        '''
        Extrai padrões dos eventos do usuário.
        '''
        horarios_inicio = []
        horarios_fim = []
        dias_semana = defaultdict(int)
        periodos = defaultdict(int)  # manha, tarde, noite
        duracoes = []

        for event in events:
            start = event.get('start', {})
            end = event.get('end', {})

            # Ignorar eventos de dia inteiro
            if 'date' in start:
                continue

            # Processar eventos com horário
            if 'dateTime' in start:
                try:
                    start_dt = datetime.fromisoformat(start['dateTime'].replace('Z', '+00:00'))
                    end_dt = datetime.fromisoformat(end['dateTime'].replace('Z', '+00:00'))

                    # Converter para timezone Brasil
                    if start_dt.tzinfo is None:
                        start_dt = start_dt.replace(tzinfo=TIMEZONE_BR)
                    else:
                        start_dt = start_dt.astimezone(TIMEZONE_BR)

                    if end_dt.tzinfo is None:
                        end_dt = end_dt.replace(tzinfo=TIMEZONE_BR)
                    else:
                        end_dt = end_dt.astimezone(TIMEZONE_BR)

                    # Coletar horários
                    hora_inicio = start_dt.time()
                    hora_fim = end_dt.time()
                    horarios_inicio.append(hora_inicio)
                    horarios_fim.append(hora_fim)

                    # Coletar dia da semana
                    dia_semana = start_dt.strftime('%A').lower()
                    dias_semana[dia_semana] += 1

                    # Classificar período (manhã, tarde, noite)
                    hora = start_dt.hour
                    if 6 <= hora < 12:
                        periodos['manha'] += 1
                    elif 12 <= hora < 18:
                        periodos['tarde'] += 1
                    else:
                        periodos['noite'] += 1

                    # Calcular duração
                    duracao = (end_dt - start_dt).total_seconds() / 60  # minutos
                    if duracao > 0 and duracao < 480:  # Máximo 8 horas
                        duracoes.append(duracao)

                except Exception as e:
                    print(f"[PATTERN] Erro ao processar evento: {e}")
                    continue

        # Calcular estatísticas
        if not horarios_inicio:
            return UserSchedulePatternService._get_default_patterns()

        horario_mais_cedo = min(horarios_inicio).strftime('%H:%M')
        horario_mais_tarde = max(horarios_fim).strftime('%H:%M')

        # Top 2 dias mais ocupados
        dias_ordenados = sorted(dias_semana.items(), key=lambda x: x[1], reverse=True)
        dias_mais_ocupados = [dia for dia, _ in dias_ordenados[:2]]

        # Períodos preferidos (ordenados por frequência)
        periodos_ordenados = sorted(periodos.items(), key=lambda x: x[1], reverse=True)
        periodos_preferidos = [periodo for periodo, _ in periodos_ordenados if _ > 0]

        # Duração média
        duracao_media = int(sum(duracoes) / len(duracoes)) if duracoes else 60

        return {
            "horario_mais_cedo": horario_mais_cedo,
            "horario_mais_tarde": horario_mais_tarde,
            "dias_mais_ocupados": dias_mais_ocupados,
            "periodos_preferidos": periodos_preferidos,
            "duracao_media_eventos": duracao_media,
            "total_eventos_analisados": len(horarios_inicio)
        }

    @staticmethod
    def _get_default_patterns():
        '''
        Retorna padrões padrão quando não há histórico suficiente.
        '''
        return {
            "horario_mais_cedo": "09:00",
            "horario_mais_tarde": "18:00",
            "dias_mais_ocupados": [],
            "periodos_preferidos": ["manha", "tarde"],
            "duracao_media_eventos": 60,
            "total_eventos_analisados": 0
        }

    @staticmethod
    def get_insights_text(patterns):
        '''
        Gera texto humanizado dos padrões do usuário.

        Returns:
            "Você costuma marcar eventos entre 9h e 18h, preferindo manhãs."
        '''
        if patterns["total_eventos_analisados"] == 0:
            return "Sem histórico suficiente para análise de padrões."

        insights = []

        # Horários
        insights.append(
            f"Você costuma ter compromissos entre {patterns['horario_mais_cedo']} e {patterns['horario_mais_tarde']}"
        )

        # Períodos preferidos
        if patterns["periodos_preferidos"]:
            periodo_texto = {
                "manha": "manhãs",
                "tarde": "tardes",
                "noite": "noites"
            }
            periodos_str = ", ".join([periodo_texto.get(p, p) for p in patterns["periodos_preferidos"][:2]])
            insights.append(f"com preferência por {periodos_str}")

        # Dias mais ocupados
        if patterns["dias_mais_ocupados"]:
            dias_texto = {
                "monday": "segunda",
                "tuesday": "terça",
                "wednesday": "quarta",
                "thursday": "quinta",
                "friday": "sexta",
                "saturday": "sábado",
                "sunday": "domingo"
            }
            dias_str = " e ".join([dias_texto.get(d, d) for d in patterns["dias_mais_ocupados"]])
            insights.append(f"Seus dias mais ocupados: {dias_str}")

        return ". ".join(insights) + "."
