# app/services/weather_service.py
"""
Serviço para integração com WeatherAPI
Fornece informações meteorológicas para o resumo matinal
"""

import requests
import os
from typing import Optional, Dict

class WeatherService:
    """Serviço para obter informações de clima"""

    # Mapeamento de condições meteorológicas para português e emojis
    WEATHER_TRANSLATIONS = {
        # Condições ensolaradas
        'Sunny': ('Ensolarado', '☀️'),
        'Clear': ('Céu limpo', '🌙'),

        # Nublado
        'Partly cloudy': ('Parcialmente nublado', '⛅'),
        'Cloudy': ('Nublado', '☁️'),
        'Overcast': ('Encoberto', '☁️'),

        # Chuva
        'Patchy rain possible': ('Possível chuva', '🌦️'),
        'Light rain': ('Chuva leve', '🌧️'),
        'Moderate rain': ('Chuva moderada', '🌧️'),
        'Heavy rain': ('Chuva forte', '🌧️'),
        'Torrential rain shower': ('Chuva torrencial', '⛈️'),

        # Trovoada
        'Thundery outbreaks possible': ('Possível trovoada', '⛈️'),
        'Patchy light rain with thunder': ('Chuva leve com trovoada', '⛈️'),
        'Moderate or heavy rain with thunder': ('Chuva forte com trovoada', '⛈️'),

        # Neblina/Névoa
        'Mist': ('Névoa', '🌫️'),
        'Fog': ('Neblina', '🌫️'),
        'Freezing fog': ('Neblina congelante', '🌫️'),

        # Neve (menos comum no Brasil, mas incluído)
        'Patchy snow possible': ('Possível neve', '🌨️'),
        'Light snow': ('Neve leve', '❄️'),
        'Heavy snow': ('Neve forte', '❄️'),

        # Granizo
        'Ice pellets': ('Granizo', '🧊'),
        'Light sleet': ('Granizo leve', '🧊'),
        'Moderate or heavy sleet': ('Granizo forte', '🧊'),
    }

    def __init__(self):
        """Inicializa o serviço de clima"""
        self.api_key = os.environ.get('WEATHER_API_KEY')
        self.base_url = "http://api.weatherapi.com/v1"

        if not self.api_key:
            print("[WEATHER] ⚠️ WEATHER_API_KEY não configurada. Clima desativado.")

    def get_weather(self, cidade: str, estado: Optional[str] = None) -> Optional[Dict]:
        """
        Busca informações de clima para uma cidade.

        Args:
            cidade: Nome da cidade (ex: "São Paulo")
            estado: Sigla do estado (ex: "SP") - opcional

        Returns:
            dict: {
                'temperatura': 24,
                'condicao': 'Ensolarado',
                'emoji': '☀️',
                'sensacao_termica': 26,
                'umidade': 65,
                'chance_chuva': 10,
                'descricao_completa': '24°C, Ensolarado ☀️'
            }
            ou None se falhar
        """
        if not self.api_key:
            return None

        try:
            # Montar query de busca
            location = f"{cidade},{estado}" if estado else cidade

            # Se for cidade brasileira sem estado, adicionar "Brazil"
            if not estado and ',' not in location:
                location = f"{cidade},Brazil"

            # Fazer requisição à API
            url = f"{self.base_url}/current.json"
            params = {
                'key': self.api_key,
                'q': location,
                'lang': 'pt'  # Português
            }

            response = requests.get(url, params=params, timeout=5)

            if response.status_code != 200:
                print(f"[WEATHER] ❌ Erro ao buscar clima: HTTP {response.status_code}")
                return None

            data = response.json()

            # Extrair informações relevantes
            current = data.get('current', {})
            condition_text = current.get('condition', {}).get('text', 'Desconhecido')

            # Traduzir condição para português (fallback para o texto original da API)
            condicao, emoji = self.WEATHER_TRANSLATIONS.get(
                condition_text,
                (condition_text, '🌡️')  # Fallback genérico
            )

            temperatura = int(current.get('temp_c', 0))
            sensacao = int(current.get('feelslike_c', 0))
            umidade = int(current.get('humidity', 0))

            # Chance de chuva (forecast)
            # Para obter chance de chuva, precisamos do endpoint forecast
            # Por simplicidade, deixamos como None aqui
            chance_chuva = None

            # Montar descrição completa
            descricao = f"{temperatura}°C, {condicao} {emoji}"

            result = {
                'temperatura': temperatura,
                'condicao': condicao,
                'emoji': emoji,
                'sensacao_termica': sensacao,
                'umidade': umidade,
                'chance_chuva': chance_chuva,
                'descricao_completa': descricao
            }

            print(f"[WEATHER] ✅ Clima obtido para {location}: {descricao}")
            return result

        except requests.exceptions.Timeout:
            print(f"[WEATHER] ⏱️ Timeout ao buscar clima para {cidade}")
            return None
        except requests.exceptions.RequestException as e:
            print(f"[WEATHER] ❌ Erro de rede ao buscar clima: {e}")
            return None
        except Exception as e:
            print(f"[WEATHER] ❌ Erro inesperado ao buscar clima: {e}")
            return None

    def get_forecast(self, cidade: str, estado: Optional[str] = None, dias: int = 1) -> Optional[Dict]:
        """
        Busca previsão do tempo para os próximos dias.

        Args:
            cidade: Nome da cidade
            estado: Sigla do estado (opcional)
            dias: Número de dias de previsão (1-3)

        Returns:
            dict com previsão ou None se falhar
        """
        if not self.api_key:
            return None

        try:
            location = f"{cidade},{estado}" if estado else f"{cidade},Brazil"

            url = f"{self.base_url}/forecast.json"
            params = {
                'key': self.api_key,
                'q': location,
                'days': min(dias, 3),  # API gratuita: máximo 3 dias
                'lang': 'pt'
            }

            response = requests.get(url, params=params, timeout=5)

            if response.status_code != 200:
                return None

            data = response.json()
            forecast_day = data.get('forecast', {}).get('forecastday', [])[0]
            day_data = forecast_day.get('day', {})

            # Chance de chuva
            chance_chuva = int(day_data.get('daily_chance_of_rain', 0))

            return {
                'chance_chuva': chance_chuva,
                'temp_max': int(day_data.get('maxtemp_c', 0)),
                'temp_min': int(day_data.get('mintemp_c', 0))
            }

        except Exception as e:
            print(f"[WEATHER] Erro ao buscar previsão: {e}")
            return None

    def format_weather_for_briefing(self, weather_data: Dict) -> str:
        """
        Formata informações de clima para o resumo matinal.

        Args:
            weather_data: Dados retornados por get_weather()

        Returns:
            String formatada para exibição
        """
        if not weather_data:
            return ""

        texto = f"🌡️ Clima: {weather_data['descricao_completa']}"

        # Adicionar sensação térmica se diferente
        if abs(weather_data['temperatura'] - weather_data['sensacao_termica']) >= 3:
            texto += f" (sensação de {weather_data['sensacao_termica']}°C)"

        # Adicionar chance de chuva se disponível
        if weather_data.get('chance_chuva') is not None:
            chance = weather_data['chance_chuva']
            if chance >= 60:
                texto += f"\n☔ Alta chance de chuva ({chance}%)"
            elif chance >= 30:
                texto += f"\n🌦️ Possível chuva ({chance}%)"

        return texto
