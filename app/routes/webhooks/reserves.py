# app/routes/webhooks/reserves.py
"""
Rotas de reserva de emergencia - modulo ponte.

Registra rotas e delega para webhooks_legacy.py.
"""

from . import webhooks_bp
from app.routes.webhooks_legacy import (
    toggle_incluir_reserva_agendamento as legacy_toggle,
    listar_agendamentos_reserva as legacy_listar,
)


@webhooks_bp.route('/api/agendamento/<int:agendamento_id>/reserva', methods=['PATCH'])
def toggle_incluir_reserva_agendamento(agendamento_id):
    """Altera o flag incluir_na_reserva de um agendamento."""
    return legacy_toggle(agendamento_id)


@webhooks_bp.route('/api/agendamentos/reserva', methods=['GET'])
def listar_agendamentos_reserva():
    """Lista agendamentos com filtros para gerenciar reserva."""
    return legacy_listar()


__all__ = ['toggle_incluir_reserva_agendamento', 'listar_agendamentos_reserva']
