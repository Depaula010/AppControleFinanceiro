# app/routes/webhooks/handlers/reserve_handler.py
"""
ReserveHandler - Processa webhooks de reserva de emergencia.

Rotas:
- /api/agendamento/<id>/reserva: Toggle incluir na reserva
- /api/agendamentos/reserva: Listar agendamentos
"""

from typing import Tuple, Any


class ReserveHandler:
    """Handler para webhooks de reserva de emergencia."""
    
    def handle_toggle_reserva(self, agendamento_id: int) -> Tuple[Any, int]:
        """Altera flag incluir_na_reserva."""
        from app.routes.webhooks.logic import legacy_toggle_incluir_reserva_agendamento
        return legacy_toggle_incluir_reserva_agendamento(agendamento_id)

    def handle_listar_reserva(self) -> Tuple[Any, int]:
        """Lista agendamentos para reserva."""
        from app.routes.webhooks.logic import legacy_listar_agendamentos_reserva
        return legacy_listar_agendamentos_reserva()


# Instancia singleton
_handler = ReserveHandler()


def toggle_incluir_reserva_agendamento(agendamento_id: int) -> Tuple[Any, int]:
    return _handler.handle_toggle_reserva(agendamento_id)


def listar_agendamentos_reserva() -> Tuple[Any, int]:
    return _handler.handle_listar_reserva()
