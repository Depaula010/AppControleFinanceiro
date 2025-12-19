# app/routes/webhooks/__init__.py
"""
Webhooks Package - Arquitetura SOLID.

Estrutura:
├── handlers/       - Logica de negocios
├── shared/         - Utilitarios
└── __init__.py     - Blueprint + Rotas

Principios SOLID:
- Single Responsibility: Cada handler = 1 dominio
- Open/Closed: Extensivel via novos handlers
- Liskov Substitution: Handlers intercambiaveis
- Interface Segregation: Interfaces focadas
- Dependency Inversion: Depende de abstractions (services)
"""

from flask import Blueprint

# Criar blueprint principal
webhooks_bp = Blueprint('webhooks', __name__)


# =============================================================================
# IMPORTAR HANDLERS
# =============================================================================

from .handlers.whatsapp_handler import handle_whatsapp_webhook
from .handlers.transaction_handler import (
    handle_automate_webhook,
    handle_api_transacao,
    handle_sms_payment,
)
from .handlers.calendar_handler import (
    connect_calendar,
    oauth2callback,
    disconnect_calendar,
)
from .handlers.reserve_handler import (
    toggle_incluir_reserva_agendamento,
    listar_agendamentos_reserva,
)


# =============================================================================
# REGISTRAR ROTAS - WHATSAPP
# =============================================================================

@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def route_webhook_whatsapp():
    """Webhook principal do WhatsApp (Baileys)."""
    return handle_whatsapp_webhook()


# =============================================================================
# REGISTRAR ROTAS - TRANSACTIONS
# =============================================================================

@webhooks_bp.route('/webhook-automate', methods=['POST'])
def route_webhook_automate():
    """Webhook do Automate (Android)."""
    return handle_automate_webhook()


@webhooks_bp.route('/api/transacao', methods=['POST'])
def route_api_transacao():
    """API de transacao direta."""
    return handle_api_transacao()


@webhooks_bp.route('/webhook-sms-payment', methods=['POST'])
def route_sms_payment():
    """Webhook de pagamento via SMS."""
    return handle_sms_payment()


# =============================================================================
# REGISTRAR ROTAS - CALENDAR
# =============================================================================

@webhooks_bp.route('/connect-calendar/<int:usuario_id>', methods=['GET'])
def route_connect_calendar(usuario_id):
    """Inicia conexao OAuth com Google Calendar."""
    return connect_calendar(usuario_id)


@webhooks_bp.route('/oauth2callback', methods=['GET'])
def route_oauth2callback():
    """Callback OAuth do Google."""
    return oauth2callback()


@webhooks_bp.route('/disconnect-calendar/<int:usuario_id>', methods=['POST'])
def route_disconnect_calendar(usuario_id):
    """Desconecta Google Calendar."""
    return disconnect_calendar(usuario_id)


# =============================================================================
# REGISTRAR ROTAS - RESERVES
# =============================================================================

@webhooks_bp.route('/api/agendamento/<int:agendamento_id>/reserva', methods=['PATCH'])
def route_toggle_reserva(agendamento_id):
    """Toggle incluir agendamento na reserva."""
    return toggle_incluir_reserva_agendamento(agendamento_id)


@webhooks_bp.route('/api/agendamentos/reserva', methods=['GET'])
def route_listar_reserva():
    """Lista agendamentos para reserva."""
    return listar_agendamentos_reserva()


__all__ = ['webhooks_bp']
