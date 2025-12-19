# app/services/whatsapp_service.py
"""
Servico de WhatsApp - wrapper para envio de mensagens.

Este modulo fornece uma interface simples para enviar mensagens
via WhatsApp usando o bot externo.
"""

from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY
from .notification_service import enviar_notificacao_whatsapp, enviar_imagem_whatsapp_bytes


def send_message(to_number: str, message: str) -> bool:
    """
    Envia mensagem de texto via WhatsApp.
    
    Args:
        to_number: Numero do WhatsApp (ex: 553194001072)
        message: Texto da mensagem
    
    Returns:
        bool: True se enviou com sucesso
    """
    return enviar_notificacao_whatsapp(
        numero=to_number,
        mensagem=message,
        bot_url=BOT_WHATSAPP_URL,
        api_key=API_SECRET_KEY
    )


def send_image(to_number: str, image_bytes: bytes, caption: str = "") -> bool:
    """
    Envia imagem via WhatsApp.
    
    Args:
        to_number: Numero do WhatsApp
        image_bytes: Bytes da imagem
        caption: Legenda da imagem
    
    Returns:
        bool: True se enviou com sucesso
    """
    return enviar_imagem_whatsapp_bytes(
        numero=to_number,
        image_bytes=image_bytes,
        caption=caption,
        bot_url=BOT_WHATSAPP_URL,
        api_key=API_SECRET_KEY
    )


__all__ = ['send_message', 'send_image']
