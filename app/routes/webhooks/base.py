# app/routes/webhooks/base.py
"""
Utilitários compartilhados para todos os webhooks.

Contém:
- Validação de segurança (HMAC, API key)
- Helpers de resposta
- Decorators comuns
- Constantes
"""

from flask import request, jsonify
from functools import wraps
from typing import Tuple, Optional, Any
import logging

from app.config import API_SECRET_KEY, WEBHOOK_SIGNATURE_KEY
from app.utils import verify_hmac_signature, compare_keys_safe
from app.services import user_service
from app import db_engine

logger = logging.getLogger(__name__)


# =============================================================================
# SECURITY VALIDATORS
# =============================================================================

def validate_hmac_signature() -> Tuple[bool, Optional[str]]:
    """
    Valida assinatura HMAC do webhook.

    Returns:
        (is_valid, error_message)
        - (True, None) se válido
        - (False, "mensagem de erro") se inválido
    """
    if not WEBHOOK_SIGNATURE_KEY:
        # Ambiente de desenvolvimento sem HMAC configurado
        logger.warning("WEBHOOK_SIGNATURE_KEY não configurado - pulando validação HMAC")
        return True, None

    signature = request.headers.get('X-Webhook-Signature')

    if not signature:
        return False, "Missing X-Webhook-Signature header"

    if not verify_hmac_signature(request.data, signature, WEBHOOK_SIGNATURE_KEY):
        return False, "Invalid HMAC signature"

    return True, None


def validate_api_key(provided_key: str) -> Tuple[bool, Optional[int], Optional[str]]:
    """
    Valida API key e retorna usuario_id se válido.

    Args:
        provided_key: API key fornecida na request

    Returns:
        (is_valid, usuario_id, error_message)
        - (True, usuario_id, None) se válido
        - (False, None, "mensagem de erro") se inválido
    """
    if not provided_key:
        return False, None, "Missing API key"

    # Buscar usuário pela API key
    with db_engine.connect() as conn:
        result = user_service.get_user_by_api_key(provided_key, conn)

    if not result:
        return False, None, "Invalid API key"

    usuario_id, _ = result
    return True, usuario_id, None


def validate_user_registered(usuario_id: int) -> Tuple[bool, Optional[str]]:
    """
    Verifica se usuário está registrado no sistema.

    Args:
        usuario_id: ID do usuário

    Returns:
        (is_registered, error_message)
    """
    with db_engine.connect() as conn:
        user = user_service.get_user_by_id(usuario_id, conn)

    if not user:
        return False, "User not registered"

    return True, None


# =============================================================================
# RESPONSE HELPERS
# =============================================================================

def success_response(message: str = "OK", data: Any = None, status_code: int = 200):
    """
    Retorna resposta JSON de sucesso padronizada.

    Args:
        message: Mensagem de sucesso
        data: Dados adicionais (opcional)
        status_code: Código HTTP (padrão: 200)

    Returns:
        Response JSON
    """
    response = {"status": "ok", "mensagem": message}

    if data is not None:
        response["data"] = data

    return jsonify(response), status_code


def error_response(message: str, status_code: int = 400, data: Any = None):
    """
    Retorna resposta JSON de erro padronizada.

    Args:
        message: Mensagem de erro
        status_code: Código HTTP (padrão: 400)
        data: Dados adicionais (opcional)

    Returns:
        Response JSON
    """
    response = {"status": "erro", "mensagem": message}

    if data is not None:
        response["data"] = data

    return jsonify(response), status_code


def service_unavailable_response():
    """Retorna resposta 503 Service Unavailable."""
    return error_response(
        "Serviço temporariamente indisponível",
        status_code=503
    )


# =============================================================================
# DECORATORS
# =============================================================================

def require_hmac_validation(f):
    """
    Decorator que valida HMAC signature antes de executar rota.

    Uso:
        @webhooks_bp.route('/webhook', methods=['POST'])
        @require_hmac_validation
        def my_webhook():
            ...
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        is_valid, error = validate_hmac_signature()

        if not is_valid:
            logger.warning(f"HMAC validation failed: {error}")
            return error_response(error, status_code=401)

        return f(*args, **kwargs)

    return decorated_function


def require_api_key_auth(f):
    """
    Decorator que valida API key antes de executar rota.

    Espera que a request tenha:
    - JSON body com campo 'api_key' ou 'user_api_key'
    - Ou query parameter 'api_key'

    Injeta 'usuario_id' como keyword argument na função.

    Uso:
        @webhooks_bp.route('/api/endpoint', methods=['POST'])
        @require_api_key_auth
        def my_endpoint(usuario_id):
            # usuario_id já validado e injetado
            ...
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        # Tentar extrair API key de múltiplas fontes
        data = request.get_json() or {}
        api_key = (
            data.get('api_key')
            or data.get('user_api_key')
            or request.args.get('api_key')
        )

        is_valid, usuario_id, error = validate_api_key(api_key)

        if not is_valid:
            logger.warning(f"API key validation failed: {error}")
            return error_response(error, status_code=401)

        # Injetar usuario_id na função
        kwargs['usuario_id'] = usuario_id

        return f(*args, **kwargs)

    return decorated_function


def require_db_engine(f):
    """
    Decorator que verifica se db_engine está disponível.

    Uso:
        @webhooks_bp.route('/webhook', methods=['POST'])
        @require_db_engine
        def my_webhook():
            # db_engine garantido estar disponível
            ...
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        if not db_engine:
            logger.error("Database engine not configured")
            return service_unavailable_response()

        return f(*args, **kwargs)

    return decorated_function


# =============================================================================
# CONSTANTS
# =============================================================================

# Mensagens padrão
MSG_NOT_UNDERSTOOD = (
    "❓ Desculpe, não entendi sua mensagem.\n\n"
    "Você pode tentar:\n"
    "• Consultar seu saldo\n"
    "• Registrar uma despesa ou renda\n"
    "• Ver sua agenda\n"
    "• Configurar notificações"
)

MSG_INTERNAL_ERROR = (
    "❌ Ops! Algo deu errado ao processar sua solicitação.\n"
    "Por favor, tente novamente em alguns instantes."
)

MSG_USER_NOT_REGISTERED = (
    "👋 Olá! Você ainda não está cadastrado.\n\n"
    "Para se cadastrar, envie:\n"
    "*cadastrar [dia_vencimento] [dia_fechamento]*\n\n"
    "Exemplo: cadastrar 10 5"
)


__all__ = [
    # Validators
    'validate_hmac_signature',
    'validate_api_key',
    'validate_user_registered',
    # Response helpers
    'success_response',
    'error_response',
    'service_unavailable_response',
    # Decorators
    'require_hmac_validation',
    'require_api_key_auth',
    'require_db_engine',
    # Constants
    'MSG_NOT_UNDERSTOOD',
    'MSG_INTERNAL_ERROR',
    'MSG_USER_NOT_REGISTERED',
]
