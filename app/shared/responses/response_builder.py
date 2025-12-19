"""
Builder para respostas API padronizadas.

Elimina código duplicado de formatação de respostas JSON.
"""

from flask import jsonify
from typing import Dict, Any, Tuple, Optional


class ApiResponse:
    """
    Builder para criar respostas JSON padronizadas.

    Elimina duplicação de código em 40+ lugares onde fazemos:
        return jsonify({"status": "sucesso", ...}), 200

    Usage:
        # Em vez de:
        return jsonify({"status": "sucesso", "mensagem": "OK"}), 200

        # Use:
        return ApiResponse.success("OK")

        # Com dados extras:
        return ApiResponse.success("Usuário criado", usuario_id=123, nome="João")
    """

    @staticmethod
    def success(mensagem: str, **kwargs) -> Tuple[Any, int]:
        """
        Resposta de sucesso (200).

        Args:
            mensagem: Mensagem de sucesso
            **kwargs: Dados adicionais para incluir na resposta

        Returns:
            Tuple (jsonify response, 200)

        Example:
            return ApiResponse.success(
                "Transação criada",
                transacao_id=123,
                valor=100.50
            )
            # {"status": "sucesso", "mensagem": "...", "transacao_id": 123, "valor": 100.50}
        """
        response_data = {
            "status": "sucesso",
            "mensagem": mensagem,
            **kwargs
        }
        return jsonify(response_data), 200

    @staticmethod
    def error(mensagem: str, status_code: int = 500, **kwargs) -> Tuple[Any, int]:
        """
        Resposta de erro (status code customizável).

        Args:
            mensagem: Mensagem de erro
            status_code: Código HTTP (padrão 500)
            **kwargs: Dados adicionais para incluir na resposta

        Returns:
            Tuple (jsonify response, status_code)

        Example:
            return ApiResponse.error("Usuário não encontrado", status_code=404)
            # {"status": "erro", "mensagem": "Usuário não encontrado"}, 404
        """
        response_data = {
            "status": "erro",
            "mensagem": mensagem,
            **kwargs
        }
        return jsonify(response_data), status_code

    @staticmethod
    def unauthorized(mensagem: str = "Não autorizado") -> Tuple[Any, int]:
        """
        Resposta 401 Unauthorized.

        Args:
            mensagem: Mensagem de erro (padrão "Não autorizado")

        Returns:
            Tuple (jsonify response, 401)

        Example:
            return ApiResponse.unauthorized()
            # {"status": "erro", "mensagem": "Não autorizado"}, 401
        """
        return ApiResponse.error(mensagem, status_code=401)

    @staticmethod
    def bad_request(mensagem: str = "Requisição inválida") -> Tuple[Any, int]:
        """
        Resposta 400 Bad Request.

        Args:
            mensagem: Mensagem de erro (padrão "Requisição inválida")

        Returns:
            Tuple (jsonify response, 400)

        Example:
            return ApiResponse.bad_request("Campos obrigatórios faltando")
            # {"status": "erro", "mensagem": "..."}, 400
        """
        return ApiResponse.error(mensagem, status_code=400)

    @staticmethod
    def not_found(mensagem: str = "Recurso não encontrado") -> Tuple[Any, int]:
        """
        Resposta 404 Not Found.

        Args:
            mensagem: Mensagem de erro (padrão "Recurso não encontrado")

        Returns:
            Tuple (jsonify response, 404)

        Example:
            return ApiResponse.not_found("Transação não encontrada")
            # {"status": "erro", "mensagem": "..."}, 404
        """
        return ApiResponse.error(mensagem, status_code=404)

    @staticmethod
    def service_unavailable(mensagem: str = "Serviço não configurado") -> Tuple[Any, int]:
        """
        Resposta 503 Service Unavailable.

        Args:
            mensagem: Mensagem de erro (padrão "Serviço não configurado")

        Returns:
            Tuple (jsonify response, 503)

        Example:
            return ApiResponse.service_unavailable("Banco de dados indisponível")
            # {"status": "erro", "mensagem": "..."}, 503
        """
        return ApiResponse.error(mensagem, status_code=503)

    @staticmethod
    def created(mensagem: str, **kwargs) -> Tuple[Any, int]:
        """
        Resposta 201 Created.

        Args:
            mensagem: Mensagem de sucesso
            **kwargs: Dados adicionais (ex: id do recurso criado)

        Returns:
            Tuple (jsonify response, 201)

        Example:
            return ApiResponse.created("Usuário criado", usuario_id=123)
            # {"status": "sucesso", "mensagem": "...", "usuario_id": 123}, 201
        """
        response_data = {
            "status": "sucesso",
            "mensagem": mensagem,
            **kwargs
        }
        return jsonify(response_data), 201

    @staticmethod
    def no_content() -> Tuple[str, int]:
        """
        Resposta 204 No Content.

        Returns:
            Tuple ("", 204)

        Example:
            return ApiResponse.no_content()
            # "", 204
        """
        return "", 204


# Alias para compatibilidade retroativa
def success_response(mensagem: str, **kwargs) -> Tuple[Any, int]:
    """Alias para ApiResponse.success()"""
    return ApiResponse.success(mensagem, **kwargs)


def error_response(mensagem: str, status_code: int = 500, **kwargs) -> Tuple[Any, int]:
    """Alias para ApiResponse.error()"""
    return ApiResponse.error(mensagem, status_code, **kwargs)
