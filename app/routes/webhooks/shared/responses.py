# app/routes/webhooks/shared/responses.py
"""
Respostas padronizadas para webhooks.

Implementa factory methods para respostas HTTP
seguindo padrao consistente em todo sistema.
"""

from flask import jsonify
from typing import Any


class WebhookResponse:
    """Factory de respostas padronizadas."""
    
    @staticmethod
    def success(message: str = "OK", data: Any = None, resposta: str = None) -> tuple:
        """Resposta de sucesso (200)."""
        response = {"status": "sucesso", "mensagem": message}
        if data:
            response["data"] = data
        if resposta:
            response["resposta"] = resposta
        return jsonify(response), 200
    
    @staticmethod
    def created(message: str = "Criado", data: Any = None) -> tuple:
        """Resposta de criacao (201)."""
        response = {"status": "sucesso", "mensagem": message}
        if data:
            response.update(data)
        return jsonify(response), 201
    
    @staticmethod
    def bad_request(message: str = "Dados invalidos") -> tuple:
        """Resposta de erro de validacao (400)."""
        return jsonify({"status": "erro", "mensagem": message}), 400
    
    @staticmethod
    def unauthorized(message: str = "Nao autorizado") -> tuple:
        """Resposta de nao autorizado (401)."""
        return jsonify({"status": "erro", "mensagem": message, "resposta": message}), 401
    
    @staticmethod
    def forbidden(message: str = "Acesso negado") -> tuple:
        """Resposta de acesso negado (403)."""
        return jsonify({"status": "erro", "mensagem": message}), 403
    
    @staticmethod
    def not_found(message: str = "Nao encontrado") -> tuple:
        """Resposta de nao encontrado (404)."""
        return jsonify({"status": "erro", "mensagem": message}), 404
    
    @staticmethod
    def server_error(message: str = "Erro interno") -> tuple:
        """Resposta de erro interno (500)."""
        return jsonify({"status": "erro", "mensagem": message}), 500
    
    @staticmethod
    def service_unavailable(message: str = "Servico indisponivel") -> tuple:
        """Resposta de servico indisponivel (503)."""
        return jsonify({"status": "erro", "mensagem": message, "resposta": message}), 503
