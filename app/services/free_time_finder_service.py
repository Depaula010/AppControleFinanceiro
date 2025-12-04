# app/services/free_time_finder_service.py
from datetime import datetime, timedelta, time
from zoneinfo import ZoneInfo
from app.services.google_calendar_oauth_service import GoogleOAuthService
from app.services.user_schedule_pattern_service import UserSchedulePatternService
from app import gemini_model

TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


class FreeTimeFinderService:
    '''
    Encontra horários livres na agenda do usuário e analisa qualidade
    dos slots baseado em padrões de comportamento.
    '''

    @staticmethod
    def find_free_slots(db_engine, usuario_id, period_type, duracao_minutos=60):
        '''
        Encontra horários livres no período especificado.

        Args:
            usuario_id: ID do usuário
            period_type: "hoje", "amanha", "esta_semana", "proxima_semana"
            duracao_minutos: Duração desejada do compromisso

        Returns:
            {
                "slots_livres": [
                    {
                        "data": "2025-11-21",
                        "hora_inicio": "14:00",
                        "hora_fim": "17:00",
                        "duracao_minutos": 180,
                        "qualidade": "otimo",  # otimo, bom, regular
                        "motivo": "Sem eventos adjacentes, período preferido"
                    }
                ],
                "insights_usuario": "Você costuma marcar eventos entre 9h e 18h..."
            }
        '''
        # Obter padrões do usuário
        patterns = UserSchedulePatternService.analyze_user_patterns(db_engine, usuario_id)

        # Calcular range de datas
        date_range = FreeTimeFinderService._calculate_date_range(period_type)
        if not date_range:
            return {"error": "Período inválido", "slots_livres": []}

        # Buscar eventos existentes
        existing_events = FreeTimeFinderService._fetch_events(
            usuario_id, date_range['start'], date_range['end']
        )

        # Gerar slots livres
        free_slots = FreeTimeFinderService._generate_free_slots(
            date_range, existing_events, duracao_minutos, patterns
        )

        # Ordenar por qualidade (ótimo -> bom -> regular)
        quality_order = {"otimo": 0, "bom": 1, "regular": 2}
        free_slots.sort(key=lambda x: (
            quality_order.get(x["qualidade"], 3),
            x["data"],
            x["hora_inicio"]
        ))

        insights = UserSchedulePatternService.get_insights_text(patterns)

        return {
            "slots_livres": free_slots,
            "insights_usuario": insights,
            "total_analisado": patterns["total_eventos_analisados"]
        }

    @staticmethod
    def _calculate_date_range(period_type):
        '''
        Calcula o range de datas baseado no período solicitado.
        '''
        now = datetime.now(TIMEZONE_BR)
        today = now.date()

        if period_type == "hoje":
            return {"start": today, "end": today}

        elif period_type == "amanha":
            tomorrow = today + timedelta(days=1)
            return {"start": tomorrow, "end": tomorrow}

        elif period_type == "esta_semana":
            # Hoje até domingo da semana atual
            days_until_sunday = 6 - today.weekday()  # 0=Monday, 6=Sunday
            end = today + timedelta(days=days_until_sunday)
            return {"start": today, "end": end}

        elif period_type == "proxima_semana":
            # Próxima segunda até próximo domingo
            days_until_monday = (7 - today.weekday()) % 7
            if days_until_monday == 0:
                days_until_monday = 7
            start = today + timedelta(days=days_until_monday)
            end = start + timedelta(days=6)
            return {"start": start, "end": end}

        return None

    @staticmethod
    def _fetch_events(usuario_id, start_date, end_date):
        '''
        Busca eventos existentes no período via Google Calendar.
        '''
        try:
            service = GoogleOAuthService.get_calendar_service(usuario_id)
        except Exception as e:
            print(f"[FREE-TIME] Erro ao obter Calendar service: {e}")
            return []

        # Converter datas para ISO com timezone
        start_dt = datetime.combine(start_date, time.min).replace(tzinfo=TIMEZONE_BR)
        end_dt = datetime.combine(end_date, time.max).replace(tzinfo=TIMEZONE_BR)

        start_iso = start_dt.isoformat()
        end_iso = end_dt.isoformat()

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
                    orderBy='startTime'
                ).execute()

                events = events_result.get('items', [])
                for event in events:
                    event['_calendar_id'] = cal['id']
                    all_events.append(event)

        except Exception as e:
            print(f"[FREE-TIME] Erro ao buscar eventos: {e}")
            return []

        return all_events

    @staticmethod
    def _generate_free_slots(date_range, existing_events, duracao_minutos, patterns):
        '''
        Gera slots livres analisando os gaps entre eventos.
        '''
        free_slots = []
        current_date = date_range['start']
        end_date = date_range['end']

        # Horário de trabalho padrão (pode ser ajustado pelos padrões do usuário)
        trabalho_inicio = time(hour=8, minute=0)
        trabalho_fim = time(hour=20, minute=0)

        # Ajustar baseado nos padrões do usuário
        if patterns["total_eventos_analisados"] > 5:
            try:
                trabalho_inicio = datetime.strptime(patterns["horario_mais_cedo"], "%H:%M").time()
                trabalho_fim = datetime.strptime(patterns["horario_mais_tarde"], "%H:%M").time()
                # Adicionar margem de 1h antes e depois
                trabalho_inicio = (datetime.combine(datetime.today(), trabalho_inicio) - timedelta(hours=1)).time()
                trabalho_fim = (datetime.combine(datetime.today(), trabalho_fim) + timedelta(hours=1)).time()
            except Exception as e:
                print(f"[FREE-TIME] Erro ao ajustar horários: {e}")

        while current_date <= end_date:
            # Filtrar eventos deste dia
            day_events = FreeTimeFinderService._get_events_for_day(existing_events, current_date)

            # Gerar slots para o dia
            day_slots = FreeTimeFinderService._generate_slots_for_day(
                current_date, day_events, trabalho_inicio, trabalho_fim,
                duracao_minutos, patterns
            )

            free_slots.extend(day_slots)
            current_date += timedelta(days=1)

        return free_slots

    @staticmethod
    def _get_events_for_day(events, target_date):
        '''
        Filtra eventos de um dia específico e ordena por horário.
        '''
        day_events = []

        for event in events:
            start = event.get('start', {})
            end = event.get('end', {})

            # Ignorar eventos de dia inteiro (não bloqueiam horários específicos)
            # Verifica tanto pelo campo 'date' quanto por eventos que duram 24h ou mais
            if 'date' in start:
                print(f"[FREE-TIME] Ignorando evento de dia inteiro (date): {event.get('summary', 'Sem título')}")
                continue

            if 'dateTime' in start:
                try:
                    start_dt = datetime.fromisoformat(start['dateTime'].replace('Z', '+00:00'))
                    if start_dt.tzinfo is None:
                        start_dt = start_dt.replace(tzinfo=TIMEZONE_BR)
                    else:
                        start_dt = start_dt.astimezone(TIMEZONE_BR)

                    if start_dt.date() == target_date:
                        end_dt = datetime.fromisoformat(end['dateTime'].replace('Z', '+00:00'))
                        if end_dt.tzinfo is None:
                            end_dt = end_dt.replace(tzinfo=TIMEZONE_BR)
                        else:
                            end_dt = end_dt.astimezone(TIMEZONE_BR)

                        # CORREÇÃO: Ignorar eventos que duram 18+ horas (provavelmente dia inteiro)
                        duracao_horas = (end_dt - start_dt).total_seconds() / 3600
                        if duracao_horas >= 18:
                            print(f"[FREE-TIME] Ignorando evento longo ({duracao_horas:.1f}h): {event.get('summary', 'Sem título')}")
                            continue

                        day_events.append({
                            "start": start_dt,
                            "end": end_dt,
                            "summary": event.get('summary', 'Sem título')
                        })
                except Exception as e:
                    print(f"[FREE-TIME] Erro ao processar evento: {e}")

        # Ordenar por horário de início
        day_events.sort(key=lambda x: x["start"])
        return day_events

    @staticmethod
    def _generate_slots_for_day(current_date, day_events, trabalho_inicio, trabalho_fim,
                                duracao_minutos, patterns):
        '''
        Gera slots livres para um dia específico.
        '''
        slots = []

        # Se é dia passado ou já passou o horário de trabalho hoje, pular
        now = datetime.now(TIMEZONE_BR)
        if current_date < now.date():
            return slots

        # Definir início e fim do dia de trabalho
        dia_inicio = datetime.combine(current_date, trabalho_inicio).replace(tzinfo=TIMEZONE_BR)
        dia_fim = datetime.combine(current_date, trabalho_fim).replace(tzinfo=TIMEZONE_BR)

        # Se é hoje, ajustar início para agora (se ainda dentro do horário de trabalho)
        if current_date == now.date():
            # Arredondar para próxima meia hora
            minutos_arredondados = (now.minute // 30 + 1) * 30
            if minutos_arredondados == 60:
                proximo_slot = now.replace(minute=0, second=0, microsecond=0) + timedelta(hours=1)
            else:
                proximo_slot = now.replace(minute=minutos_arredondados, second=0, microsecond=0)

            dia_inicio = max(dia_inicio, proximo_slot)

        if dia_inicio >= dia_fim:
            return slots

        # Se não há eventos, o dia todo está livre
        if not day_events:
            duracao_total = int((dia_fim - dia_inicio).total_seconds() / 60)
            if duracao_total >= duracao_minutos:
                qualidade, motivo = FreeTimeFinderService._analyze_slot_quality(
                    dia_inicio, dia_fim, None, None, patterns, current_date
                )
                slots.append({
                    "data": current_date.strftime("%Y-%m-%d"),
                    "hora_inicio": dia_inicio.strftime("%H:%M"),
                    "hora_fim": dia_fim.strftime("%H:%M"),
                    "duracao_minutos": duracao_total,
                    "qualidade": qualidade,
                    "motivo": motivo
                })
            return slots

        # Verificar slot antes do primeiro evento
        primeiro_evento = day_events[0]
        if dia_inicio < primeiro_evento["start"]:
            duracao_slot = int((primeiro_evento["start"] - dia_inicio).total_seconds() / 60)
            if duracao_slot >= duracao_minutos:
                qualidade, motivo = FreeTimeFinderService._analyze_slot_quality(
                    dia_inicio, primeiro_evento["start"], None, primeiro_evento, patterns, current_date
                )
                slots.append({
                    "data": current_date.strftime("%Y-%m-%d"),
                    "hora_inicio": dia_inicio.strftime("%H:%M"),
                    "hora_fim": primeiro_evento["start"].strftime("%H:%M"),
                    "duracao_minutos": duracao_slot,
                    "qualidade": qualidade,
                    "motivo": motivo
                })

        # Verificar gaps entre eventos
        for i in range(len(day_events) - 1):
            evento_atual = day_events[i]
            proximo_evento = day_events[i + 1]

            gap_inicio = evento_atual["end"]
            gap_fim = proximo_evento["start"]

            duracao_gap = int((gap_fim - gap_inicio).total_seconds() / 60)
            if duracao_gap >= duracao_minutos:
                qualidade, motivo = FreeTimeFinderService._analyze_slot_quality(
                    gap_inicio, gap_fim, evento_atual, proximo_evento, patterns, current_date
                )
                slots.append({
                    "data": current_date.strftime("%Y-%m-%d"),
                    "hora_inicio": gap_inicio.strftime("%H:%M"),
                    "hora_fim": gap_fim.strftime("%H:%M"),
                    "duracao_minutos": duracao_gap,
                    "qualidade": qualidade,
                    "motivo": motivo
                })

        # Verificar slot depois do último evento
        ultimo_evento = day_events[-1]
        if ultimo_evento["end"] < dia_fim:
            duracao_slot = int((dia_fim - ultimo_evento["end"]).total_seconds() / 60)
            if duracao_slot >= duracao_minutos:
                qualidade, motivo = FreeTimeFinderService._analyze_slot_quality(
                    ultimo_evento["end"], dia_fim, ultimo_evento, None, patterns, current_date
                )
                slots.append({
                    "data": current_date.strftime("%Y-%m-%d"),
                    "hora_inicio": ultimo_evento["end"].strftime("%H:%M"),
                    "hora_fim": dia_fim.strftime("%H:%M"),
                    "duracao_minutos": duracao_slot,
                    "qualidade": qualidade,
                    "motivo": motivo
                })

        return slots

    @staticmethod
    def _analyze_slot_quality(slot_inicio, slot_fim, evento_anterior, evento_posterior,
                              patterns, current_date):
        '''
        Analisa a qualidade de um slot livre baseado em diversos fatores.

        Returns:
            ("otimo"/"bom"/"regular", "motivo descritivo")
        '''
        pontos = 0
        motivos = []

        # Fator 1: Sem eventos adjacentes (melhor qualidade)
        if evento_anterior is None and evento_posterior is None:
            pontos += 3
            motivos.append("dia livre")
        elif evento_anterior is None or evento_posterior is None:
            pontos += 2
            motivos.append("sem eventos adjacentes")
        else:
            pontos += 1

        # Fator 2: Período do dia (baseado nos padrões do usuário)
        hora_inicio = slot_inicio.hour
        periodo_slot = None
        if 6 <= hora_inicio < 12:
            periodo_slot = "manha"
        elif 12 <= hora_inicio < 18:
            periodo_slot = "tarde"
        else:
            periodo_slot = "noite"

        if periodo_slot in patterns.get("periodos_preferidos", []):
            pontos += 2
            periodo_texto = {"manha": "manhã", "tarde": "tarde", "noite": "noite"}
            motivos.append(f"período preferido ({periodo_texto[periodo_slot]})")

        # Fator 3: Dia da semana
        dia_semana = current_date.strftime('%A').lower()
        if dia_semana not in patterns.get("dias_mais_ocupados", []):
            pontos += 1
            motivos.append("dia menos ocupado")

        # Fator 4: Duração do slot (slots maiores são melhores para flexibilidade)
        duracao_minutos = int((slot_fim - slot_inicio).total_seconds() / 60)
        if duracao_minutos >= 180:  # 3+ horas
            pontos += 2
            motivos.append("longo período disponível")
        elif duracao_minutos >= 120:  # 2+ horas
            pontos += 1

        # Classificar qualidade
        if pontos >= 6:
            qualidade = "otimo"
        elif pontos >= 4:
            qualidade = "bom"
        else:
            qualidade = "regular"

        motivo_texto = ", ".join(motivos).capitalize() if motivos else "Horário disponível"

        return qualidade, motivo_texto

    @staticmethod
    def format_free_slots_message(result, contexto=None):
        '''
        Formata a mensagem de resposta com os horários livres.

        Args:
            result: Resultado do find_free_slots
            contexto: Contexto opcional (ex: "dentista")

        Returns:
            String formatada para enviar ao usuário
        '''
        slots = result.get("slots_livres", [])
        insights = result.get("insights_usuario", "")

        if not slots:
            msg = "🗓️ *Horários Livres*\n\n"
            msg += "❌ Nenhum horário livre encontrado no período solicitado.\n\n"
            if insights:
                msg += f"💡 {insights}"
            return msg

        msg = "🗓️ *Horários Livres"
        if contexto:
            msg += f" para {contexto}"
        msg += "*\n\n"

        # Agrupar por data
        slots_por_data = {}
        for slot in slots:
            data = slot["data"]
            if data not in slots_por_data:
                slots_por_data[data] = []
            slots_por_data[data].append(slot)

        # Formatar cada dia
        for data, day_slots in slots_por_data.items():
            # Formatar data (ex: "Quinta, 21/11")
            data_dt = datetime.strptime(data, "%Y-%m-%d")
            dia_semana = data_dt.strftime("%A")
            dia_semana_pt = {
                "Monday": "Segunda", "Tuesday": "Terça", "Wednesday": "Quarta",
                "Thursday": "Quinta", "Friday": "Sexta", "Saturday": "Sábado", "Sunday": "Domingo"
            }.get(dia_semana, dia_semana)
            data_formatada = data_dt.strftime("%d/%m")

            msg += f"📅 *{dia_semana_pt}, {data_formatada}*\n"

            # Mostrar apenas os 3 melhores slots do dia
            for slot in day_slots[:3]:
                emoji_qualidade = {
                    "otimo": "⭐",
                    "bom": "✓",
                    "regular": "•"
                }.get(slot["qualidade"], "•")

                msg += f"{emoji_qualidade} {slot['hora_inicio']}-{slot['hora_fim']}"

                # Adicionar motivo se for ótimo ou bom
                if slot["qualidade"] in ["otimo", "bom"]:
                    msg += f" _{slot['motivo']}_"

                msg += "\n"

            msg += "\n"

        # Adicionar insights
        if insights and result.get("total_analisado", 0) > 0:
            msg += f"💡 {insights}"

        return msg.strip()

    @staticmethod
    def suggest_best_slot_with_ai(result, contexto, usuario_preferences):
        '''
        Usa Gemini para sugerir o MELHOR horário baseado no contexto.

        Args:
            result: Resultado do find_free_slots
            contexto: O que o usuário quer marcar (ex: "dentista")
            usuario_preferences: Padrões do usuário

        Returns:
            String com sugestão personalizada da IA
        '''
        if not gemini_model:
            return None

        slots = result.get("slots_livres", [])
        if not slots:
            return None

        # Preparar dados para o Gemini
        slots_resumo = []
        for slot in slots[:5]:  # Top 5
            slots_resumo.append({
                "data": slot["data"],
                "horario": f"{slot['hora_inicio']}-{slot['hora_fim']}",
                "qualidade": slot["qualidade"],
                "motivo": slot["motivo"]
            })

        prompt = f'''Você é um assistente de agenda inteligente.

O usuário quer marcar: "{contexto}"

Horários disponíveis:
{slots_resumo}

Padrões do usuário:
{usuario_preferences}

Sugira o MELHOR horário considerando:
1. O tipo de compromisso ("{contexto}")
2. Os padrões do usuário
3. Boas práticas (ex: dentista de manhã, reunião após café)

Responda em até 2 linhas, de forma amigável e direta.
Formato: "Sugiro [dia] às [hora] porque [motivo breve]"
'''

        try:
            response = gemini_model.generate_content(prompt)
            sugestao = response.text.strip()
            return f"🤖 *Sugestão IA:* {sugestao}"
        except Exception as e:
            print(f"[FREE-TIME-AI] Erro ao gerar sugestão: {e}")
            return None
