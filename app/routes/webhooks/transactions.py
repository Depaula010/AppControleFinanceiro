# app/routes/webhooks/transactions.py
"""
Rotas de transacoes - modulo ponte.

Registra rotas e delega para webhooks_legacy.py.
"""

from . import webhooks_bp
from app.routes.webhooks_legacy import (
    handle_automate_webhook as legacy_automate,
    handle_api_transacao as legacy_api_transacao,
)


@webhooks_bp.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """Rota do Gatilho Android com CONFIRMACAO."""
    return legacy_automate()


@webhooks_bp.route('/api/transacao', methods=['POST'])
def handle_api_transacao():
    """Endpoint direto para registro de transacoes."""
    return legacy_api_transacao()


__all__ = ['handle_automate_webhook', 'handle_api_transacao']
