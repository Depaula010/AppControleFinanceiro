# app/routes/webhooks/handlers/transaction_handler.py
"""
TransactionHandler - Processa webhooks de transacoes.

Rotas:
- /webhook-automate: Gatilho Android
- /api/transacao: API direta iPhone
- /webhook-sms-payment: Pagamento via SMS
"""

from typing import Tuple, Any
from flask import request, jsonify

from app import db_engine, gemini_model
from app.utils import ensure_db_connection

from ..shared.responses import WebhookResponse


class TransactionHandler:
    """Handler para webhooks de transacoes."""
    
    def __init__(self):
        self.response = WebhookResponse()
    
    def handle_automate(self) -> Tuple[Any, int]:
        """Processa webhook do Automate (Android)."""
        from app.routes.webhooks.logic import handle_automate_webhook
        return handle_automate_webhook()

    def handle_api_transacao(self) -> Tuple[Any, int]:
        """Processa API de transacao direta."""
        from app.routes.webhooks.logic import handle_api_transacao
        return handle_api_transacao()

    def handle_sms_payment(self) -> Tuple[Any, int]:
        """Processa pagamento via SMS."""
        from app.routes.webhooks.logic import legacy_handle_sms_payment
        return legacy_handle_sms_payment()


# Instancia singleton
_handler = TransactionHandler()


def handle_automate_webhook() -> Tuple[Any, int]:
    return _handler.handle_automate()


def handle_api_transacao() -> Tuple[Any, int]:
    return _handler.handle_api_transacao()


def handle_sms_payment() -> Tuple[Any, int]:
    return _handler.handle_sms_payment()
