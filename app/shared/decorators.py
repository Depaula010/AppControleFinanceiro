"""
Decoradores reutilizáveis para rotas Flask.

Elimina código duplicado de autenticação, validação e error handling.
"""

from functools import wraps
from flask import request, jsonify
import traceback
from typing import Callable, Any, Optional, List

from app.config import API_SECRET_KEY
from app import db_engine


def require_api_key(f: Callable) -> Callable:
    """
    Decorator para validar API key no header x-api-key.

    Elimina código duplicado presente em 10+ rotas.

    Usage:
        @app.route('/admin/something')
        @require_api_key
        def my_route():
            # API key já validada aqui
            return jsonify({"status": "sucesso"})

    Retorna 401 se API key inválida ou ausente.
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        secret_key = request.headers.get('x-api-key')
        if secret_key != API_SECRET_KEY:
            return jsonify({
                "status": "erro",
                "mensagem": "Não autorizado"
            }), 401
        return f(*args, **kwargs)
    return decorated_function


def require_db_connection(f: Callable) -> Callable:
    """
    Decorator para garantir que db_engine está configurado.

    Elimina código duplicado presente em 40+ funções.

    Usage:
        @require_db_connection
        def my_function():
            # db_engine garantidamente configurado
            with db_engine.connect() as conn:
                ...

    Levanta Exception se db_engine não configurado.
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        if not db_engine:
            raise Exception("Banco de dados não configurado")
        return f(*args, **kwargs)
    return decorated_function


def validate_required_fields(*required_fields: str):
    """
    Decorator para validar campos obrigatórios no request JSON.

    Elimina código duplicado de validação em múltiplas rotas.

    Usage:
        @app.route('/webhook', methods=['POST'])
        @validate_required_fields('user_api_key', 'texto_notificacao')
        def handle_webhook():
            # Campos já validados aqui
            data = request.json
            user_api_key = data['user_api_key']  # Garantido que existe
            ...

    Args:
        *required_fields: Nomes dos campos obrigatórios

    Retorna 400 se algum campo obrigatório estiver faltando.
    """
    def decorator(f: Callable) -> Callable:
        @wraps(f)
        def decorated_function(*args, **kwargs):
            data = request.json or {}

            missing = [field for field in required_fields if not data.get(field)]

            if missing:
                return jsonify({
                    "status": "erro",
                    "mensagem": f"Campos obrigatórios faltando: {', '.join(missing)}"
                }), 400

            return f(*args, **kwargs)
        return decorated_function
    return decorator


def handle_errors(tag: str, default_status_code: int = 500):
    """
    Decorator para tratar erros de forma padronizada.

    Elimina código duplicado de try/except presente em 60+ funções.

    Usage:
        @app.route('/something')
        @handle_errors(tag="SOMETHING", default_status_code=500)
        def my_route():
            # Qualquer exception é capturada e retornada como JSON
            raise ValueError("Algo deu errado")
            # Retorna: {"status": "erro", "mensagem": "Algo deu errado"}, 500

    Args:
        tag: Tag para identificar erro nos logs
        default_status_code: Código HTTP padrão para erros

    Loga o erro completo com traceback e retorna JSON padronizado.
    """
    def decorator(f: Callable) -> Callable:
        @wraps(f)
        def decorated_function(*args, **kwargs):
            try:
                return f(*args, **kwargs)
            except Exception as e:
                print(f"[{tag}] Erro: {e}")
                traceback.print_exc()

                # Se for um tuple (response, status_code), usar o status_code
                if isinstance(e, tuple) and len(e) == 2:
                    mensagem, status_code = e
                else:
                    mensagem = str(e)
                    status_code = default_status_code

                return jsonify({
                    "status": "erro",
                    "mensagem": mensagem
                }), status_code
        return decorated_function
    return decorator


def require_user_auth(f: Callable) -> Callable:
    """
    Decorator para autenticar usuário via user_api_key no request JSON.

    Elimina código duplicado presente em múltiplas rotas de webhook.

    Usage:
        @webhooks_bp.route('/webhook-automate', methods=['POST'])
        @require_user_auth
        def handle_automate(usuario_id, numero_whatsapp):
            # usuario_id e numero_whatsapp já injetados aqui
            print(f"Usuário autenticado: {usuario_id}")
            ...

    Retorna 401 se API key inválida ou usuário não encontrado.
    Injeta usuario_id e numero_whatsapp como kwargs na função decorada.
    """
    @wraps(f)
    def decorated_function(*args, **kwargs):
        from app.services import finance_service

        data = request.json or {}
        user_api_key = data.get('user_api_key')

        if not user_api_key:
            return jsonify({
                "status": "erro",
                "mensagem": "user_api_key é obrigatório"
            }), 400

        user_info = finance_service.get_user_by_api_key(user_api_key)

        if not user_info:
            return jsonify({
                "status": "erro",
                "mensagem": "API key inválida"
            }), 401

        usuario_id, numero_whatsapp = user_info

        # Injetar usuario_id e numero_whatsapp como kwargs
        kwargs['usuario_id'] = usuario_id
        kwargs['numero_whatsapp'] = numero_whatsapp

        return f(*args, **kwargs)
    return decorated_function


def combine_decorators(*decorators):
    """
    Combina múltiplos decoradores em um só.

    Usage:
        # Em vez de:
        @require_api_key
        @handle_errors(tag="ADMIN")
        @validate_required_fields('field1', 'field2')
        def my_route():
            ...

        # Pode usar:
        admin_route = combine_decorators(
            require_api_key,
            handle_errors(tag="ADMIN"),
            validate_required_fields('field1', 'field2')
        )

        @admin_route
        def my_route():
            ...
    """
    def decorator(f: Callable) -> Callable:
        for dec in reversed(decorators):
            f = dec(f)
        return f
    return decorator


# Decoradores pré-configurados comuns
admin_endpoint = combine_decorators(
    require_api_key,
    handle_errors(tag="ADMIN"),
)

webhook_endpoint = combine_decorators(
    handle_errors(tag="WEBHOOK"),
)
