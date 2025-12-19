# app/routes/webhooks/handlers/whatsapp_handler.py
"""
WhatsAppHandler - Processa mensagens do bot WhatsApp (Baileys).

Este handler delega para a logica consolidada em webhooks_legacy.py.
A logica de autenticacao HMAC/API key ja esta no legacy e funciona
corretamente (HMAC opcional, API key obrigatorio).

NOTA: Em futuras iteracoes, a logica sera movida diretamente para ca.
"""

from typing import Tuple, Any


class WhatsAppHandler:
    """Handler para webhook do WhatsApp."""
    
    def handle(self) -> Tuple[Any, int]:
        """
        Processa mensagem do WhatsApp.
        Delega para funcao consolidada no legacy.
        """
        from app.routes.webhooks_logic import handle_whatsapp_webhook as legacy
        return legacy()


# Instancia singleton
_handler = WhatsAppHandler()


def handle_whatsapp_webhook() -> Tuple[Any, int]:
    """Funcao de entrada para o webhook."""
    return _handler.handle()
