# app/routes/webhooks/handlers/whatsapp_handler.py
"""
WhatsAppHandler - Processa mensagens do bot WhatsApp (Baileys).

Este handler delega para o modulo de logica de negocios (webhooks/logic.py).
A logica de autenticacao HMAC/API key ja esta implementada e funciona
corretamente (HMAC opcional, API key obrigatorio).

NOTA: A logica sera gradualmente migrada para dentro deste handler.
"""

from typing import Tuple, Any


class WhatsAppHandler:
    """Handler para webhook do WhatsApp."""
    
    def handle(self) -> Tuple[Any, int]:
        """
        Processa mensagem do WhatsApp.
        Delega para funcao de logica de negocio.
        """
        from app.routes.webhooks.logic import handle_whatsapp_webhook
        return handle_whatsapp_webhook()


# Instancia singleton
_handler = WhatsAppHandler()


def handle_whatsapp_webhook() -> Tuple[Any, int]:
    """Funcao de entrada para o webhook."""
    return _handler.handle()
