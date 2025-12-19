# app/routes/webhooks/calendar.py
"""
Rotas de calendario (OAuth, eventos) - modulo ponte.

Registra rotas e delega para webhooks_legacy.py.
"""

from . import webhooks_bp
from app.routes.webhooks_legacy import (
    connect_calendar as legacy_connect,
    oauth2callback as legacy_oauth2callback,
    disconnect_calendar as legacy_disconnect,
)


@webhooks_bp.route('/connect-calendar/<int:usuario_id>', methods=['GET'])
def connect_calendar(usuario_id):
    """Endpoint para iniciar processo de conexao OAuth2."""
    return legacy_connect(usuario_id)


@webhooks_bp.route('/oauth2callback', methods=['GET'])
def oauth2callback():
    """Callback do Google apos autorizacao."""
    return legacy_oauth2callback()


@webhooks_bp.route('/disconnect-calendar/<int:usuario_id>', methods=['POST'])
def disconnect_calendar(usuario_id):
    """Permite usuario desconectar Google Calendar."""
    return legacy_disconnect(usuario_id)


__all__ = ['connect_calendar', 'oauth2callback', 'disconnect_calendar']
