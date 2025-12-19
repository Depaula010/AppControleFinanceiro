"""
Utilitários compartilhados entre módulos admin.

Constantes, imports comuns e helpers usados por múltiplos módulos admin.
"""

from datetime import datetime, date, time, timedelta
from zoneinfo import ZoneInfo

from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app import db_engine

# Timezone do Brasil (usado em múltiplos módulos admin)
TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")


def get_current_datetime_brazil():
    """Retorna datetime atual no timezone do Brasil."""
    return datetime.now(TIMEZONE_BR)


def get_current_date_brazil():
    """Retorna date atual no timezone do Brasil."""
    return get_current_datetime_brazil().date()
