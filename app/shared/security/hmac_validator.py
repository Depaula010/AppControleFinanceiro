# app/shared/security/hmac_validator.py
"""
Validação de assinaturas HMAC para webhooks e APIs externas
"""
import hmac
import hashlib
import secrets


def verify_hmac_signature(payload: bytes, signature: str, secret_key: str) -> bool:
    """
    Verifica assinatura HMAC-SHA256 de um payload.
    Usa compare_digest para prevenir timing attacks.

    Args:
        payload: Dados em bytes
        signature: Assinatura recebida (hexadecimal)
        secret_key: Chave secreta para verificação

    Returns:
        True se assinatura é válida, False caso contrário

    Examples:
        >>> payload = b"dados"
        >>> signature = generate_hmac_signature(payload, "secret")
        >>> verify_hmac_signature(payload, signature, "secret")
        True
        >>> verify_hmac_signature(payload, signature, "wrong_secret")
        False
    """
    if not signature or not secret_key:
        return False

    try:
        expected_signature = hmac.new(
            secret_key.encode('utf-8'),
            payload,
            hashlib.sha256
        ).hexdigest()

        # Usa compare_digest para evitar timing attacks
        return secrets.compare_digest(signature, expected_signature)
    except Exception as e:
        print(f"[SECURITY] Erro ao verificar HMAC: {e}")
        return False


def generate_hmac_signature(payload: bytes, secret_key: str) -> str:
    """
    Gera assinatura HMAC-SHA256 de um payload.

    Args:
        payload: Dados em bytes
        secret_key: Chave secreta

    Returns:
        Assinatura em hexadecimal

    Examples:
        >>> payload = b"dados"
        >>> signature = generate_hmac_signature(payload, "secret")
        >>> len(signature)  # SHA256 hex = 64 caracteres
        64
    """
    return hmac.new(
        secret_key.encode('utf-8'),
        payload,
        hashlib.sha256
    ).hexdigest()


def compare_keys_safe(key1: str, key2: str) -> bool:
    """
    Compara duas chaves de forma segura (timing-attack resistant).
    Usa secrets.compare_digest para evitar vazamento de tempo.

    Args:
        key1: Primeira chave
        key2: Segunda chave

    Returns:
        True se as chaves são iguais

    Examples:
        >>> compare_keys_safe("secret123", "secret123")
        True
        >>> compare_keys_safe("secret123", "secret456")
        False
    """
    if not key1 or not key2:
        return False

    return secrets.compare_digest(key1, key2)
