# app/security_config.py
"""
Configurações de segurança centralizadas
"""
import os

# Rate Limiting
RATELIMIT_ENABLED = os.getenv('RATELIMIT_ENABLED', 'true').lower() == 'true'
RATELIMIT_STORAGE_URI = os.getenv('REDIS_URL', 'memory://')
RATELIMIT_STRATEGY = 'fixed-window'

# Limites por tipo de endpoint (ajustados para uso real + preparado para SaaS)
RATELIMIT_DEFAULTS = {
    'default': '1000 per hour',  # ~16 req/min - uso confortável pessoal
    'api': '500 per hour',       # Endpoints de transação/consulta
    'webhooks': '100 per minute; 3000 per hour',  # Suporta bursts do WhatsApp
    'admin': '20 per minute; 200 per hour',       # Proteção brute force
}

# Headers de segurança (Talisman)
SECURITY_HEADERS_ENABLED = os.getenv('SECURITY_HEADERS_ENABLED', 'true').lower() == 'true'

# CORS
CORS_ENABLED = os.getenv('CORS_ENABLED', 'false').lower() == 'true'
CORS_ORIGINS = os.getenv('CORS_ORIGINS', '*').split(',')

# Content Security Policy
CSP_CONFIG = {
    'default-src': ["'self'"],
    'script-src': ["'self'"],
    'style-src': ["'self'", "'unsafe-inline'"],
    'img-src': ["'self'", 'data:', 'https:'],
    'font-src': ["'self'"],
    'connect-src': ["'self'"],
    'frame-ancestors': ["'none'"],
}

# Proteção contra bots
BOT_PROTECTION_ENABLED = os.getenv('BOT_PROTECTION_ENABLED', 'true').lower() == 'true'
AUTO_BLOCK_ENABLED = os.getenv('AUTO_BLOCK_ENABLED', 'true').lower() == 'true'
BLOCK_DURATION_MINUTES = int(os.getenv('BLOCK_DURATION_MINUTES', '60'))
MAX_SUSPICIOUS_ATTEMPTS = int(os.getenv('MAX_SUSPICIOUS_ATTEMPTS', '5'))

# Logging de segurança
SECURITY_LOG_FILE = os.getenv('SECURITY_LOG_FILE', 'logs/security.log')
SECURITY_LOG_LEVEL = os.getenv('SECURITY_LOG_LEVEL', 'WARNING')
