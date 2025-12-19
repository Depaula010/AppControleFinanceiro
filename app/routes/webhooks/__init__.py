# app/routes/webhooks/__init__.py
"""
Webhooks package - Modular webhook handlers.

Este pacote contém todos os webhooks refatorados do sistema:
- Transações (Automate, API, SMS)
- Calendar (OAuth, eventos)
- Reserva de emergência
- WhatsApp (roteador de intents)

Arquitetura:
- Cada domínio tem seu próprio módulo
- Intent handlers usam padrão Template Method
- Factory pattern para roteamento de intents
- 100% backward compatibility mantida

Migração:
- ANTES: app/routes/webhooks.py (3.322 linhas monolíticas)
- DEPOIS: app/routes/webhooks/* (21 arquivos modulares)
"""

from flask import Blueprint

# Criar blueprint principal
webhooks_bp = Blueprint('webhooks', __name__)

# Importar sub-módulos para registrar rotas
# Isso garante que as rotas sejam registradas no blueprint
from . import transactions
from . import calendar
from . import reserves
from . import whatsapp_router  # Intent-based WhatsApp webhook router

__all__ = ['webhooks_bp']
