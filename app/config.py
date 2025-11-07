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

# Configuração do Banco de Dados
if DATABASE_URL.startswith("postgres://"):
    DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)