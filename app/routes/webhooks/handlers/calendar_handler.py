# app/routes/webhooks/handlers/calendar_handler.py
"""
CalendarHandler - Processa webhooks de calendario.

Rotas:
- /connect-calendar/<usuario_id>: Inicia OAuth
- /oauth2callback: Callback do Google
- /disconnect-calendar/<usuario_id>: Desconecta
"""

from typing import Tuple, Any


class CalendarHandler:
    """Handler para webhooks de calendario."""
    
    def handle_connect(self, usuario_id: int) -> Tuple[Any, int]:
        """Inicia processo de conexao OAuth2."""
        from app.routes.webhooks_logic import connect_calendar as legacy
        return legacy(usuario_id)
    
    def handle_oauth2callback(self) -> Tuple[Any, int]:
        """Callback do Google apos autorizacao."""
        from app.routes.webhooks_logic import oauth2callback as legacy
        return legacy()
    
    def handle_disconnect(self, usuario_id: int) -> Tuple[Any, int]:
        """Desconecta Google Calendar."""
        from app.routes.webhooks_logic import disconnect_calendar as legacy
        return legacy(usuario_id)


# Instancia singleton
_handler = CalendarHandler()


def connect_calendar(usuario_id: int) -> Tuple[Any, int]:
    return _handler.handle_connect(usuario_id)


def oauth2callback() -> Tuple[Any, int]:
    return _handler.handle_oauth2callback()


def disconnect_calendar(usuario_id: int) -> Tuple[Any, int]:
    return _handler.handle_disconnect(usuario_id)
