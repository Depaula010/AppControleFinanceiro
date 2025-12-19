# app/routes/webhooks/intents/base_intent.py
"""
Classe base abstrata para todos os intent handlers do WhatsApp.

Implementa o Template Method Pattern para padronizar o processamento de intents:
1. extract_params() - Extrai parâmetros da mensagem
2. validate() - Valida parâmetros extraídos
3. execute() - Executa a ação do intent
4. format_response() - Formata resposta para WhatsApp

Uso:
    class MeuIntent(BaseIntent):
        def extract_params(self) -> Dict[str, Any]:
            return gemini_service.extract_params(self.mensagem)

        def execute(self) -> Dict[str, Any]:
            # Lógica do intent
            return {"data": result}
"""

from abc import ABC, abstractmethod
from typing import Dict, Any, Optional
from sqlalchemy.engine import Connection
import logging

logger = logging.getLogger(__name__)


class BaseIntent(ABC):
    """
    Classe base abstrata para intent handlers WhatsApp.

    Template Method Pattern:
    - handle() é o método template (final)
    - extract_params(), validate(), execute() são hooks (abstract)
    - format_response() tem implementação padrão (pode ser override)
    """

    def __init__(
        self,
        usuario_id: int,
        mensagem: str,
        conn: Connection,
        numero_whatsapp: Optional[str] = None
    ):
        """
        Inicializa o intent handler.

        Args:
            usuario_id: ID do usuário que enviou a mensagem
            mensagem: Texto da mensagem do WhatsApp
            conn: Conexão ativa com o banco de dados
            numero_whatsapp: Número de WhatsApp do usuário (opcional)
        """
        self.usuario_id = usuario_id
        self.mensagem = mensagem
        self.conn = conn
        self.numero_whatsapp = numero_whatsapp
        self.params = {}

    def handle(self) -> Dict[str, Any]:
        """
        Template Method - Executa o fluxo completo do intent.

        Fluxo:
        1. Extrai parâmetros da mensagem
        2. Valida parâmetros
        3. Executa ação do intent
        4. Formata resposta

        Returns:
            {
                "success": bool,
                "message": str,
                "data": Optional[Dict]
            }
        """
        try:
            # Step 1: Extract parameters
            logger.info(
                f"[{self.__class__.__name__}] Extraindo parâmetros para usuario_id={self.usuario_id}"
            )
            self.params = self.extract_params()

            # Step 2: Validate
            logger.info(f"[{self.__class__.__name__}] Validando parâmetros")
            validation_error = self.validate()
            if validation_error:
                return {
                    "success": False,
                    "message": validation_error,
                    "data": None
                }

            # Step 3: Execute intent logic
            logger.info(f"[{self.__class__.__name__}] Executando intent")
            result = self.execute()

            # Step 4: Format response
            message = self.format_response(result)

            return {
                "success": True,
                "message": message,
                "data": result
            }

        except Exception as e:
            logger.error(
                f"[{self.__class__.__name__}] Erro ao processar intent: {e}",
                exc_info=True
            )
            return {
                "success": False,
                "message": self._get_error_message(e),
                "data": None
            }

    @abstractmethod
    def extract_params(self) -> Dict[str, Any]:
        """
        Extrai parâmetros da mensagem do usuário.

        Geralmente usa Gemini AI para extrair:
        - Valores numéricos
        - Datas
        - Nomes de contas/categorias
        - Descrições

        Returns:
            Dict com parâmetros extraídos
        """
        pass

    def validate(self) -> Optional[str]:
        """
        Valida os parâmetros extraídos.

        Override este método para adicionar validações específicas.
        Retorne None se válido, ou mensagem de erro se inválido.

        Returns:
            None se válido, string com mensagem de erro caso contrário
        """
        return None  # Implementação padrão: sempre válido

    @abstractmethod
    def execute(self) -> Dict[str, Any]:
        """
        Executa a lógica principal do intent.

        Implementa a ação específica do intent:
        - Consultar banco de dados
        - Criar transações
        - Atualizar configurações
        - etc.

        Returns:
            Dict com dados do resultado (será passado para format_response)
        """
        pass

    def format_response(self, data: Dict[str, Any]) -> str:
        """
        Formata a resposta para envio ao WhatsApp.

        Implementação padrão retorna mensagem simples.
        Override para formatações específicas (listas, emojis, etc.)

        Args:
            data: Dados retornados por execute()

        Returns:
            Mensagem formatada para WhatsApp
        """
        return data.get("message", "✅ Operação realizada com sucesso!")

    def _get_error_message(self, exception: Exception) -> str:
        """
        Formata mensagem de erro amigável para o usuário.

        Args:
            exception: Exceção capturada

        Returns:
            Mensagem de erro formatada
        """
        # Em produção, evitar expor detalhes técnicos
        error_msg = "❌ Ops! Algo deu errado. Por favor, tente novamente."

        # Log detalhado para debugging
        logger.error(f"Erro técnico: {str(exception)}")

        return error_msg


class ConfirmationRequiredIntent(BaseIntent):
    """
    Intent base para ações que requerem confirmação do usuário.

    Exemplo: Criar transação, deletar evento, etc.
    Armazena estado pendente no Redis e aguarda "confirmar" ou "cancelar".
    """

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.requires_confirmation = True

    def create_pending_state(self, state_data: Dict[str, Any]) -> str:
        """
        Cria estado pendente de confirmação no Redis.

        Args:
            state_data: Dados a serem confirmados

        Returns:
            ID do estado pendente
        """
        # Implementação será feita nos intents específicos
        # que usam TransactionConfirmationService, EventConfirmationService, etc.
        raise NotImplementedError("Subclasse deve implementar create_pending_state")


__all__ = [
    'BaseIntent',
    'ConfirmationRequiredIntent',
]
