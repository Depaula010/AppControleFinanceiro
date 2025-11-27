# app/services/travel_time_service.py
import requests
from app import config
from app.services.redis_service import redis_service
from app.services.calendar_management_service import CalendarManagementService
from datetime import datetime, timedelta, date as date_type
import pytz

class TravelTimeService:
    """Calcula tempo de deslocamento usando OpenRouteService"""

    ORS_API_KEY = config.OPENROUTE_API_KEY
    ORS_BASE_URL = config.OPENROUTE_BASE_URL
    TIMEOUT = 15  # segundos (aumentado para evitar timeout)
    MAX_RETRIES = 2  # número de tentativas
    BRAZIL_TZ = pytz.timezone('America/Sao_Paulo')

    @staticmethod
    def geocode_address(address: str):
        """
        Converte endereço para lat/lon usando ORS Geocoding.

        Args:
            address: Endereço completo

        Returns:
            tuple: (latitude, longitude, endereco_formatado) ou (None, None, None)
        """
        if not TravelTimeService.ORS_API_KEY:
            print("[TRAVEL-TIME] ERRO: OPENROUTE_API_KEY não configurada")
            return None, None, None

        url = f"{TravelTimeService.ORS_BASE_URL}/geocode/search"
        params = {
            'api_key': TravelTimeService.ORS_API_KEY,
            'text': address,
            'boundary.country': 'BR',  # Limitar ao Brasil
            'size': 1  # Apenas o melhor resultado
        }

        print(f"[TRAVEL-TIME] Geocoding: {address}")

        # Retry logic
        for attempt in range(TravelTimeService.MAX_RETRIES):
            try:
                response = requests.get(url, params=params, timeout=TravelTimeService.TIMEOUT)
                response.raise_for_status()

                data = response.json()

                if not data.get('features'):
                    print(f"[TRAVEL-TIME] Nenhum resultado encontrado para: {address}")
                    return None, None, None

                feature = data['features'][0]
                coords = feature['geometry']['coordinates']
                lon, lat = coords[0], coords[1]

                # Endereço formatado
                properties = feature.get('properties', {})
                endereco_formatado = properties.get('label', address)

                # Validar coordenadas (Brasil: -33 < lat < 5, -74 < lon < -34)
                if not (-33 <= lat <= 5 and -74 <= lon <= -34):
                    print(f"[TRAVEL-TIME] Coordenadas fora do Brasil: {lat}, {lon}")
                    return None, None, None

                print(f"[TRAVEL-TIME] Geocoding sucesso: {lat}, {lon}")
                return lat, lon, endereco_formatado

            except requests.Timeout:
                if attempt < TravelTimeService.MAX_RETRIES - 1:
                    print(f"[TRAVEL-TIME] Timeout na tentativa {attempt + 1}, tentando novamente...")
                    continue
                else:
                    print("[TRAVEL-TIME] ERRO: Timeout ao geocodificar endereço após múltiplas tentativas")
                    return None, None, None
            except requests.RequestException as e:
                if attempt < TravelTimeService.MAX_RETRIES - 1:
                    print(f"[TRAVEL-TIME] Erro na tentativa {attempt + 1}: {e}, tentando novamente...")
                    continue
                else:
                    print(f"[TRAVEL-TIME] ERRO ao geocodificar após múltiplas tentativas: {e}")
                    return None, None, None
            except Exception as e:
                print(f"[TRAVEL-TIME] ERRO inesperado: {e}")
                return None, None, None

        return None, None, None

    @staticmethod
    def calculate_travel_time(origin_lat, origin_lon, dest_lat, dest_lon):
        """
        Calcula tempo de viagem de carro.

        Args:
            origin_lat, origin_lon: Coordenadas de origem
            dest_lat, dest_lon: Coordenadas de destino

        Returns:
            dict: {
                'duration_minutes': 45,
                'distance_km': 12.5,
                'route_summary': 'Via Av. Paulista'
            } ou None em caso de erro
        """
        if not TravelTimeService.ORS_API_KEY:
            print("[TRAVEL-TIME] ERRO: OPENROUTE_API_KEY não configurada")
            return None

        url = f"{TravelTimeService.ORS_BASE_URL}/v2/directions/driving-car"
        headers = {
            'Authorization': TravelTimeService.ORS_API_KEY,
            'Content-Type': 'application/json'
        }
        body = {
            'coordinates': [
                [origin_lon, origin_lat],  # ORS usa [lon, lat]
                [dest_lon, dest_lat]
            ]
        }

        print(f"[TRAVEL-TIME] Calculando rota: ({origin_lat},{origin_lon}) → ({dest_lat},{dest_lon})")

        # Retry logic
        for attempt in range(TravelTimeService.MAX_RETRIES):
            try:
                start_time = datetime.now()

                response = requests.post(url, json=body, headers=headers, timeout=TravelTimeService.TIMEOUT)
                response.raise_for_status()

                elapsed_ms = int((datetime.now() - start_time).total_seconds() * 1000)

                data = response.json()

                if not data.get('routes'):
                    print("[TRAVEL-TIME] Nenhuma rota encontrada")
                    return None

                route = data['routes'][0]
                summary = route['summary']

                duration_seconds = summary['duration']
                distance_meters = summary['distance']

                duration_minutes = int(duration_seconds / 60)
                distance_km = round(distance_meters / 1000, 1)

                # Tentar extrair resumo da rota
                route_summary = None
                if route.get('segments') and route['segments'][0].get('steps'):
                    first_step = route['segments'][0]['steps'][0]
                    route_summary = first_step.get('name', '')

                print(f"[TRAVEL-TIME] Rota calculada: {duration_minutes} min, {distance_km} km ({elapsed_ms}ms)")

                return {
                    'duration_minutes': duration_minutes,
                    'distance_km': distance_km,
                    'route_summary': route_summary
                }

            except requests.Timeout:
                if attempt < TravelTimeService.MAX_RETRIES - 1:
                    print(f"[TRAVEL-TIME] Timeout na tentativa {attempt + 1}, tentando novamente...")
                    continue
                else:
                    print("[TRAVEL-TIME] ERRO: Timeout ao calcular rota após múltiplas tentativas")
                    return None
            except requests.RequestException as e:
                if attempt < TravelTimeService.MAX_RETRIES - 1:
                    print(f"[TRAVEL-TIME] Erro na tentativa {attempt + 1}: {e}, tentando novamente...")
                    continue
                else:
                    print(f"[TRAVEL-TIME] ERRO ao calcular rota após múltiplas tentativas: {e}")
                    return None
            except Exception as e:
                print(f"[TRAVEL-TIME] ERRO inesperado: {e}")
                return None

        return None

    @staticmethod
    def get_next_event_after(usuario_id, event_start_datetime):
        """
        Busca próximo evento após horário especificado.

        Args:
            usuario_id: ID do usuário
            event_start_datetime: datetime do início do evento atual

        Returns:
            dict: {
                'id': 'abc123',
                'title': 'Reunião',
                'start': datetime(...),
                'location': '...'
            } ou None
        """
        try:
            # Buscar eventos do dia seguinte (margem de segurança)
            if isinstance(event_start_datetime, datetime):
                data_inicio = event_start_datetime.date()
            else:
                data_inicio = event_start_datetime

            data_fim = data_inicio + timedelta(days=1)

            print(f"[TRAVEL-TIME] Buscando próximo evento após {event_start_datetime}")

            # Usar CalendarManagementService para buscar eventos
            eventos = CalendarManagementService.get_events_by_date_range(
                usuario_id=usuario_id,
                data_inicio=data_inicio,
                data_fim=data_fim
            )

            if not eventos:
                print("[TRAVEL-TIME] Nenhum evento posterior encontrado")
                return None

            # Filtrar eventos que começam DEPOIS do evento atual
            eventos_posteriores = []
            for evento in eventos:
                start_str = evento.get('start', {}).get('dateTime') or evento.get('start', {}).get('date')

                if not start_str:
                    continue

                try:
                    # Parse da data/hora do evento
                    if 'T' in start_str:
                        # Evento com horário
                        evento_start = datetime.fromisoformat(start_str.replace('Z', '+00:00'))
                        # Converter para timezone do Brasil
                        evento_start = evento_start.astimezone(TravelTimeService.BRAZIL_TZ)
                    else:
                        # Evento de dia inteiro
                        evento_date = datetime.fromisoformat(start_str).date()
                        evento_start = datetime.combine(evento_date, datetime.min.time())
                        evento_start = TravelTimeService.BRAZIL_TZ.localize(evento_start)

                    # Comparar
                    if isinstance(event_start_datetime, datetime):
                        comparacao_start = event_start_datetime
                    else:
                        comparacao_start = datetime.combine(event_start_datetime, datetime.min.time())
                        comparacao_start = TravelTimeService.BRAZIL_TZ.localize(comparacao_start)

                    if evento_start > comparacao_start:
                        eventos_posteriores.append({
                            'id': evento.get('id'),
                            'title': evento.get('summary', 'Sem título'),
                            'start': evento_start,
                            'location': evento.get('location', '')
                        })

                except Exception as e:
                    print(f"[TRAVEL-TIME] Erro ao processar evento: {e}")
                    continue

            if not eventos_posteriores:
                print("[TRAVEL-TIME] Nenhum evento posterior encontrado")
                return None

            # Ordenar por data e retornar o primeiro
            eventos_posteriores.sort(key=lambda x: x['start'])
            proximo = eventos_posteriores[0]

            print(f"[TRAVEL-TIME] Próximo evento: {proximo['title']} às {proximo['start'].strftime('%H:%M')}")
            return proximo

        except Exception as e:
            print(f"[TRAVEL-TIME] ERRO ao buscar próximo evento: {e}")
            return None

    @staticmethod
    def check_conflict(event_end_time, travel_minutes, next_event_start):
        """
        Detecta conflito: evento + viagem > tempo até próximo.

        Args:
            event_end_time: datetime de fim do evento atual
            travel_minutes: int, tempo de viagem em minutos
            next_event_start: datetime de início do próximo evento (ou None)

        Returns:
            dict: {
                'has_conflict': True,
                'gap_minutes': -15,  # Negativo = conflito
                'message': 'Você chegaria 15 min atrasado'
            }
        """
        if not next_event_start:
            return {
                'has_conflict': False,
                'gap_minutes': None,
                'message': 'Sem eventos subsequentes'
            }

        try:
            # Calcular quando o usuário chegaria ao próximo evento
            chegada = event_end_time + timedelta(minutes=travel_minutes)

            # Calcular diferença
            gap = (next_event_start - chegada).total_seconds() / 60  # minutos

            if gap < 0:
                # Conflito: chegaria atrasado
                atraso = abs(int(gap))
                return {
                    'has_conflict': True,
                    'gap_minutes': int(gap),
                    'message': f"Você chegaria {atraso} min atrasado"
                }
            elif gap < 15:
                # Margem muito apertada
                return {
                    'has_conflict': True,
                    'gap_minutes': int(gap),
                    'message': f"Margem de apenas {int(gap)} min (muito apertado)"
                }
            else:
                # Sem conflito
                return {
                    'has_conflict': False,
                    'gap_minutes': int(gap),
                    'message': f"Tempo suficiente ({int(gap)} min de margem)"
                }

        except Exception as e:
            print(f"[TRAVEL-TIME] ERRO ao verificar conflito: {e}")
            return {
                'has_conflict': False,
                'gap_minutes': None,
                'message': 'Erro ao verificar conflito'
            }

    @staticmethod
    def suggest_alternative_time(original_start, duration_minutes, usuario_id):
        """
        Sugere horários alternativos baseados em slots livres.

        Args:
            original_start: datetime original do evento
            duration_minutes: duração do evento em minutos
            usuario_id: ID do usuário

        Returns:
            list: ['08:00', '14:30', '16:00'] (até 3 sugestões)
        """
        try:
            # Buscar eventos do dia
            if isinstance(original_start, datetime):
                data = original_start.date()
            else:
                data = original_start

            print(f"[TRAVEL-TIME] Buscando horários alternativos para {data}")

            eventos = CalendarManagementService.get_events_by_date_range(
                usuario_id=usuario_id,
                data_inicio=data,
                data_fim=data
            )

            # Criar lista de horários ocupados
            horarios_ocupados = []
            for evento in eventos:
                start_str = evento.get('start', {}).get('dateTime')
                end_str = evento.get('end', {}).get('dateTime')

                if start_str and end_str:
                    try:
                        start_dt = datetime.fromisoformat(start_str.replace('Z', '+00:00'))
                        end_dt = datetime.fromisoformat(end_str.replace('Z', '+00:00'))

                        start_dt = start_dt.astimezone(TravelTimeService.BRAZIL_TZ)
                        end_dt = end_dt.astimezone(TravelTimeService.BRAZIL_TZ)

                        horarios_ocupados.append((start_dt, end_dt))
                    except:
                        pass

            # Ordenar por horário
            horarios_ocupados.sort(key=lambda x: x[0])

            # Gerar sugestões: antes do primeiro evento, entre eventos, após último
            sugestoes = []

            # Horário comercial: 8h às 19h
            dia_inicio = datetime.combine(data, datetime.min.time().replace(hour=8))
            dia_fim = datetime.combine(data, datetime.min.time().replace(hour=19))

            dia_inicio = TravelTimeService.BRAZIL_TZ.localize(dia_inicio)
            dia_fim = TravelTimeService.BRAZIL_TZ.localize(dia_fim)

            # Sugestão 1: Antes do primeiro evento (se houver espaço)
            if horarios_ocupados:
                primeiro_evento = horarios_ocupados[0][0]
                if (primeiro_evento - dia_inicio).total_seconds() / 60 >= duration_minutes + 30:
                    # 30 min de margem
                    horario_sugerido = dia_inicio
                    sugestoes.append(horario_sugerido.strftime('%H:%M'))

            # Sugestão 2 e 3: Entre eventos
            for i in range(len(horarios_ocupados) - 1):
                fim_atual = horarios_ocupados[i][1]
                inicio_proximo = horarios_ocupados[i + 1][0]

                gap_minutos = (inicio_proximo - fim_atual).total_seconds() / 60

                if gap_minutos >= duration_minutes + 30:
                    horario_sugerido = fim_atual + timedelta(minutes=15)  # 15 min após término
                    sugestoes.append(horario_sugerido.strftime('%H:%M'))

                if len(sugestoes) >= 3:
                    break

            # Sugestão: Após último evento (se ainda houver tempo no dia)
            if len(sugestoes) < 3 and horarios_ocupados:
                ultimo_evento = horarios_ocupados[-1][1]
                if (dia_fim - ultimo_evento).total_seconds() / 60 >= duration_minutes + 30:
                    horario_sugerido = ultimo_evento + timedelta(minutes=15)
                    sugestoes.append(horario_sugerido.strftime('%H:%M'))

            # Se não há eventos, sugerir 3 horários espaçados
            if not sugestoes:
                sugestoes = ['09:00', '14:00', '16:30']

            print(f"[TRAVEL-TIME] Sugestões de horário: {sugestoes}")
            return sugestoes[:3]  # Máximo 3 sugestões

        except Exception as e:
            print(f"[TRAVEL-TIME] ERRO ao sugerir horários: {e}")
            return ['09:00', '14:00', '16:30']  # Fallback

    @staticmethod
    def check_rate_limit(usuario_id):
        """
        Verifica limite diário de cálculos.

        Args:
            usuario_id: ID do usuário

        Returns:
            tuple: (allowed: bool, remaining: int, used: int)
        """
        try:
            hoje = datetime.now().strftime('%Y-%m-%d')
            redis_key = f"travel_time_limit:{usuario_id}:{hoje}"

            # Buscar contador atual
            count_str = redis_service.get(redis_key)
            used = int(count_str) if count_str else 0

            limit = config.TRAVEL_TIME_DAILY_LIMIT
            remaining = limit - used

            allowed = remaining > 0

            print(f"[TRAVEL-TIME] User {usuario_id}: {used}/{limit} calculations today")

            return allowed, remaining, used

        except Exception as e:
            print(f"[TRAVEL-TIME] ERRO ao verificar rate limit: {e}")
            return True, 10, 0  # Em caso de erro, permitir

    @staticmethod
    def increment_rate_limit(usuario_id):
        """
        Incrementa contador de rate limit.

        Args:
            usuario_id: ID do usuário

        Returns:
            bool: True se sucesso
        """
        try:
            hoje = datetime.now().strftime('%Y-%m-%d')
            redis_key = f"travel_time_limit:{usuario_id}:{hoje}"

            # Incrementar contador
            count_str = redis_service.get(redis_key)
            current_count = int(count_str) if count_str else 0
            new_count = current_count + 1

            # Salvar com TTL de 24 horas
            success = redis_service.set_with_ttl(redis_key, str(new_count), ttl_seconds=86400)

            if success:
                print(f"[TRAVEL-TIME] Rate limit incrementado: {new_count}/{config.TRAVEL_TIME_DAILY_LIMIT}")

            return success

        except Exception as e:
            print(f"[TRAVEL-TIME] ERRO ao incrementar rate limit: {e}")
            return False
