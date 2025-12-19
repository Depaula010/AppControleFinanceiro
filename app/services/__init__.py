# app/services/__init__.py
"""
Modulo de services - logica de negocio da aplicacao.
"""

from . import gemini_service
from . import whatsapp_service

__all__ = [
    'gemini_service',
    'whatsapp_service',
]
