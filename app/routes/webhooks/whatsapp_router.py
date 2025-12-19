# app/routes/webhooks/whatsapp_router.py
"""
Rota do WhatsApp webhook - modulo ponte.

Este modulo registra a rota /webhook-whatsapp e delega
para a implementacao consolidada em webhooks_legacy.py.

Arquitetura:
- Single Responsibility: este modulo so faz o roteamento
- Open/Closed: pode adicionar middleware sem alterar logica
- A logica de negocio esta em webhooks_legacy.handle_whatsapp_webhook()

Futura refatoracao:
- Quando o webhooks_legacy.py for desmembrado, a logica
  pode ser movida diretamente para ca
"""

from flask import request, jsonify
from . import webhooks_bp

# Importar handler do arquivo legacy (ate refatoracao completa)
from app.routes.webhooks_legacy import handle_whatsapp_webhook as legacy_handler


@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """
    Webhook WhatsApp - delega para handler consolidado.
    
    Formato esperado (JSON):
        - texto: Mensagem do usuario
        - numero_remetente: Numero WhatsApp (ex: 553194001072)
    
    Headers:
        - X-Webhook-Signature: HMAC signature (opcional)
        - x-api-key: API key de autenticacao
    """
    return legacy_handler()


__all__ = ['handle_whatsapp_webhook']
