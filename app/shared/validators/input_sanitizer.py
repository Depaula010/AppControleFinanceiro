# app/shared/validators/input_sanitizer.py
"""
Sanitização e validação de inputs de usuários
Proteção contra XSS, SQL Injection e outras vulnerabilidades
"""
import re


def sanitize_for_log(data: dict) -> dict:
    """
    Remove campos sensíveis de dados antes de logar.
    Previne exposição de credenciais e tokens em logs.

    Args:
        data: Dicionário com dados

    Returns:
        Dicionário sanitizado (cópia)

    Examples:
        >>> sanitize_for_log({'api_key': '123456789', 'name': 'João'})
        {'api_key': '12345678...', 'name': 'João'}
    """
    if not data or not isinstance(data, dict):
        return data

    sanitized = data.copy()
    sensitive_keys = [
        'api_key', 'user_api_key', 'password', 'token',
        'secret', 'access_token', 'refresh_token',
        'authorization', 'x-api-key'
    ]

    for key in sensitive_keys:
        if key in sanitized:
            # Mostra apenas os primeiros 8 caracteres
            value = str(sanitized[key])
            sanitized[key] = f"{value[:8]}..." if len(value) > 8 else "[REDACTED]"

    return sanitized


def sanitize_input(text: str, max_length: int = 200, allow_special_chars: bool = False) -> str:
    """
    Sanitiza input de usuário para prevenir XSS e injection.

    Args:
        text: Texto a sanitizar
        max_length: Comprimento máximo permitido
        allow_special_chars: Se False, remove caracteres especiais perigosos

    Returns:
        Texto sanitizado

    Examples:
        >>> sanitize_input("<script>alert('xss')</script>")
        "scriptalert('xss')/script"
        >>> sanitize_input("Olá, mundo!", max_length=5)
        "Olá,"
    """
    if not text:
        return text

    # 1. Limitar tamanho
    text = text[:max_length]

    # 2. Remover caracteres de controle (exceto espaço, tab, newline)
    text = ''.join(char for char in text if ord(char) >= 32 or char in '\t\n\r')

    # 3. Remover caracteres perigosos se necessário
    if not allow_special_chars:
        # Remove: < > { } \ $ ` | ; &
        dangerous_chars = r'[<>{}\\$`|;&]'
        text = re.sub(dangerous_chars, '', text)

    # 4. Normalizar espaços múltiplos
    text = re.sub(r'\s+', ' ', text)

    return text.strip()
