# app/config.py
import os

"""
Carrega as configurações a partir das variáveis de ambiente.
Isso é o equivalente ao appsettings.json ou à configuração 
de 'Services' no Program.cs.
"""

# Chaves de API e Conexões
GEMINI_API_KEY = os.environ['GEMINI_API_KEY']
DATABASE_URL = os.environ['DATABASE_URL']

# Chave secreta da *nossa* API (para o bot e o automate)
# IMPORTANTE: Deve ter pelo menos 32 caracteres para segurança adequada
API_SECRET_KEY = os.environ['API_SECRET_KEY']
if len(API_SECRET_KEY) < 32:
    raise ValueError("API_SECRET_KEY deve ter pelo menos 32 caracteres. "
                     "Gere uma chave segura com: python -c \"import secrets; print(secrets.token_urlsafe(32))\"")

# Chave separada para assinatura de webhooks (HMAC)
WEBHOOK_SIGNATURE_KEY = os.environ.get('WEBHOOK_SIGNATURE_KEY', API_SECRET_KEY) 

# Integração com Bot WhatsApp (API v1 - SaaS)
BOT_WHATSAPP_URL = os.environ.get('BOT_WHATSAPP_URL', 'https://bot-appfinanceiro-whatsapp.onrender.com')
BOT_SESSION_ID = os.environ.get('BOT_SESSION_ID', '')
BOT_API_KEY = os.environ.get('BOT_API_KEY', '')

REDIS_URL = os.environ.get('REDIS_URL', 'redis://localhost:6379')

GOOGLE_CLIENT_ID = os.environ.get('GOOGLE_CLIENT_ID')
GOOGLE_CLIENT_SECRET = os.environ.get('GOOGLE_CLIENT_SECRET')
GOOGLE_REDIRECT_URI = os.environ.get(
    'GOOGLE_REDIRECT_URI',
    'https://app-controle-financeiro-oh32.onrender.com/oauth2callback'
)

# API de Clima (opcional)
WEATHER_API_KEY = os.environ.get('WEATHER_API_KEY')

# OpenRouteService API (para cálculo de tempo de deslocamento)
OPENROUTE_API_KEY = os.environ.get('OPENROUTE_API_KEY')
OPENROUTE_BASE_URL = os.environ.get(
    'OPENROUTE_BASE_URL',
    'https://api.openrouteservice.org'
)

# Rate Limiting para cálculo de tempo de viagem
TRAVEL_TIME_DAILY_LIMIT = int(os.environ.get('TRAVEL_TIME_DAILY_LIMIT', '10'))

# Configuração do Banco de Dados
if DATABASE_URL.startswith("postgres://"):
    DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)