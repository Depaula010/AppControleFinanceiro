# app/routes/webhooks/shared/validators.py
"""
Validadores para webhooks.

Implementa validacao de HMAC, API key e dados de request
de forma reutilizavel seguindo Single Responsibility.
"""

from flask import request
from app.config import API_SECRET_KEY, WEBHOOK_SIGNATURE_KEY
from app.utils import verify_hmac_signature, compare_keys_safe


class WebhookValidator:
    """Validador centralizado para webhooks."""
    
    @staticmethod
    def validate_hmac(required: bool = False) -> tuple[bool, str | None]:
        """
        Valida assinatura HMAC do webhook.
        
        Args:
            required: Se True, falha se header nao existir
            
        Returns:
            (is_valid, error_message)
        """
        signature = request.headers.get('X-Webhook-Signature', '').strip()
        
        if not signature:
            if required:
                return False, "Missing X-Webhook-Signature header"
            # Modo compatibilidade - HMAC opcional
            print("[SECURITY] Webhook sem assinatura HMAC (modo compatibilidade)")
            return True, None
            
        payload = request.get_data()
        if not verify_hmac_signature(payload, signature, WEBHOOK_SIGNATURE_KEY):
            print("[SECURITY] Assinatura HMAC invalida")
            return False, "Assinatura invalida"
            
        return True, None
    
    @staticmethod
    def validate_api_key() -> tuple[bool, str | None]:
        """
        Valida API key do header.
        
        Returns:
            (is_valid, error_message)
        """
        api_key = request.headers.get('x-api-key', '').strip()
        
        if not api_key:
            return False, "Missing API key"
            
        if not compare_keys_safe(api_key, API_SECRET_KEY):
            return False, "Invalid API key"
            
        return True, None
    
    @staticmethod
    def validate_json_fields(*required_fields: str) -> tuple[bool, str | None, dict]:
        """
        Valida campos obrigatorios no JSON.
        
        Args:
            required_fields: Lista de campos obrigatorios
            
        Returns:
            (is_valid, error_message, data)
        """
        data = request.get_json() or {}
        
        missing = [f for f in required_fields if not data.get(f)]
        
        if missing:
            return False, f"Campos faltando: {', '.join(missing)}", data
            
        return True, None, data
