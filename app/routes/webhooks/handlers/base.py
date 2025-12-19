# app/routes/webhooks/handlers/base.py
"""
BaseHandler - Template Method Pattern.

Define o esqueleto de processamento de webhooks.
Subclasses implementam os passos especificos.

Principios SOLID:
- SRP: Handler faz apenas processamento de webhook
- OCP: Extensivel via heranca sem modificar base
- LSP: Subclasses sao substituiveis
- DIP: Depende de abstrações (services)
"""

from abc import ABC, abstractmethod
from flask import request
from typing import Any, Tuple
import traceback

from app import db_engine, gemini_model
from ..shared.validators import WebhookValidator
from ..shared.responses import WebhookResponse


class BaseHandler(ABC):
    """
    Handler base com Template Method Pattern.
    
    Esqueleto de processamento:
    1. validate() - Validar request
    2. authenticate() - Autenticar usuario
    3. process() - Processar logica de negocio
    4. respond() - Formatar resposta
    """
    
    def __init__(self):
        self.validator = WebhookValidator()
        self.response = WebhookResponse()
    
    def handle(self, **kwargs) -> Tuple[Any, int]:
        """
        Template Method - executa o pipeline de processamento.
        
        Returns:
            Tuple[response, status_code]
        """
        try:
            # 1. Verificar servicos
            if not self._check_services():
                return self.response.service_unavailable("Servico nao configurado")
            
            # 2. Validar request
            is_valid, error = self.validate()
            if not is_valid:
                return self.response.bad_request(error)
            
            # 3. Autenticar (opcional)
            auth_result = self.authenticate()
            if auth_result is not None:
                is_auth, error = auth_result
                if not is_auth:
                    return self.response.unauthorized(error)
            
            # 4. Processar
            return self.process(**kwargs)
            
        except Exception as e:
            print(f"[{self.__class__.__name__}] Erro: {e}")
            traceback.print_exc()
            return self.response.server_error(str(e))
    
    def _check_services(self) -> bool:
        """Verifica se servicos essenciais estao disponiveis."""
        return db_engine is not None and gemini_model is not None
    
    def validate(self) -> Tuple[bool, str | None]:
        """
        Validacao do request. Override para customizar.
        
        Returns:
            (is_valid, error_message)
        """
        return True, None
    
    def authenticate(self) -> Tuple[bool, str | None] | None:
        """
        Autenticacao do request. Override para customizar.
        Retorna None para pular autenticacao.
        
        Returns:
            (is_authenticated, error_message) ou None
        """
        return None
    
    @abstractmethod
    def process(self, **kwargs) -> Tuple[Any, int]:
        """
        Processa a logica de negocio. DEVE ser implementado.
        
        Returns:
            Tuple[response, status_code]
        """
        pass
