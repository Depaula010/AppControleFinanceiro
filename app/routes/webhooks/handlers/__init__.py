# app/routes/webhooks/handlers/__init__.py
"""
Handlers de webhooks - camada de logica de negocios.

Cada handler implementa a logica para um dominio especifico,
delegando operacoes para os services apropriados.
"""

from .base import BaseHandler
from .whatsapp_handler import WhatsAppHandler
from .transaction_handler import TransactionHandler
from .calendar_handler import CalendarHandler
from .reserve_handler import ReserveHandler

__all__ = [
    'BaseHandler',
    'WhatsAppHandler', 
    'TransactionHandler',
    'CalendarHandler',
    'ReserveHandler',
]
