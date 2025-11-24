# app/security_config.py
"""
Configurações de segurança centralizadas
"""
import os

# Rate Limiting
RATELIMIT_ENABLED = os.getenv('RATELIMIT_ENABLED', 'true').lower() == 'true'
RATELIMIT_STORAGE_URI = os.getenv('REDIS_URL', 'memory://')
RATELIMIT_STRATEGY = 'fixed-window'

# Limites por tipo de endpoint (VALORES SEGUROS - ajustados após auditoria de segurança)
RATELIMIT_DEFAULTS = {
    'default': '100 per hour',   # ~1.6 req/min - uso pessoal normal
    'api': '200 per hour',       # Endpoints de transação/consulta (~3 req/min)
    'webhooks': '30 per minute; 500 per hour',  # WhatsApp: usuário não envia 30 msgs/min
    'admin': '5 per minute; 50 per hour',       # CRÍTICO: proteção contra brute force
}

# Limites específicos por endpoint (sobrescrevem defaults)
RATELIMIT_SPECIFIC = {
    '/admin/setup-database': '2 per day',        # Setup é raro
    '/admin/clear-bot-session': '5 per day',     # Limpar sessão é excepcional
    '/webhook-whatsapp': '20 per minute',        # Conversas normais
    '/api/transacao': '10 per minute',           # ~1 transação a cada 6 segundos
}

# Headers de segurança (Talisman)
SECURITY_HEADERS_ENABLED = os.getenv('SECURITY_HEADERS_ENABLED', 'true').lower() == 'true'

# CORS (Cross-Origin Resource Sharing)
CORS_ENABLED = os.getenv('CORS_ENABLED', 'false').lower() == 'true'

# Validar configuração de CORS
cors_origins_str = os.getenv('CORS_ORIGINS', '').strip()
if CORS_ENABLED:
    if not cors_origins_str:
        raise ValueError(
            "CORS_ENABLED=true requer CORS_ORIGINS configurado. "
            "Defina origens explícitas, separadas por vírgula. "
            "NUNCA use '*' (wildcard)."
        )
    if cors_origins_str == '*':
        raise ValueError(
            "CORS_ORIGINS='*' não é permitido por motivos de segurança. "
            "Especifique domínios explícitos: https://app.exemplo.com,https://exemplo.com"
        )

CORS_ORIGINS = [o.strip() for o in cors_origins_str.split(',') if o.strip()] if cors_origins_str else []

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
