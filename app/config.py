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
API_SECRET_KEY = os.environ.get('API_SECRET_KEY', 'uma-senha-bem-forte-12345') 

# URL do serviço do Bot (para enviar notificações)
BOT_WHATSAPP_URL = os.environ.get('BOT_WHATSAPP_URL', 'https://bot-appfinanceiro-whatsapp.onrender.com')

REDIS_URL = os.environ.get('REDIS_URL', 'redis://localhost:6379')

GOOGLE_CLIENT_ID = os.environ.get('GOOGLE_CLIENT_ID')
GOOGLE_CLIENT_SECRET = os.environ.get('GOOGLE_CLIENT_SECRET')
GOOGLE_REDIRECT_URI = os.environ.get(
    'GOOGLE_REDIRECT_URI',
    'https://app-controle-financeiro-oh32.onrender.com/oauth2callback'
)

# API de Clima (opcional)
WEATHER_API_KEY = os.environ.get('WEATHER_API_KEY')

# Configuração do Banco de Dados
if DATABASE_URL.startswith("postgres://"):
    DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)