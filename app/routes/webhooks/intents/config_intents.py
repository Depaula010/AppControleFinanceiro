# app/routes/webhooks/intents/config_intents.py
"""
Intent handlers para configurações de endereços favoritos.

Permite usuários:
- Configurar endereços favoritos (casa, trabalho, outro)
- Listar endereços cadastrados
- Deletar endereços
"""

from typing import Dict, Any, Optional
from .base_intent import BaseIntent
from app.services import gemini_service


class ConfigurarEnderecoIntent(BaseIntent):
    """
    Handler para intent 'Configurar Endereço'.

    Permite cadastrar endereços favoritos para uso em rotas.

    Exemplo de mensagem:
    - "Configurar endereço casa: Rua X, 123, Bairro, Cidade-SP"
    - "Meu trabalho é na Av Y, 456"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai label e endereço da mensagem."""
        addr_data = gemini_service.extract_address_config(self.mensagem, self.usuario_id)
        return {
            "label": addr_data.get('label'),
            "endereco_completo": addr_data.get('endereco_completo')
        }

    def validate(self) -> Optional[str]:
        """Valida se label e endereço foram identificados."""
        if not self.params.get("label") or not self.params.get("endereco_completo"):
            return (
                "❌ Não entendi o endereço.\n\n"
                "Use o formato:\n"
                "*'Configurar endereço casa: Rua X, 123, Bairro, Cidade-SP'*\n\n"
                "Tipos de endereço:\n"
                "• Casa\n"
                "• Trabalho\n"
                "• Outro"
            )
        return None

    def execute(self) -> Dict[str, Any]:
        """Salva endereço favorito."""
        from app.services.user_address_service import UserAddressService

        sucesso, mensagem = UserAddressService.save_favorite_address(
            self.usuario_id,
            self.params["label"],
            self.params["endereco_completo"]
        )

        return {
            "sucesso": sucesso,
            "mensagem": mensagem
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna mensagem do serviço."""
        return data.get("mensagem", "❌ Erro ao configurar endereço.")


class ListarEnderecosIntent(BaseIntent):
    """
    Handler para intent 'Listar Endereços'.

    Lista todos os endereços favoritos cadastrados.

    Exemplo de mensagem:
    - "Meus endereços"
    - "Quais endereços tenho cadastrados?"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Sem parâmetros necessários."""
        return {}

    def validate(self) -> Optional[str]:
        return None

    def execute(self) -> Dict[str, Any]:
        """Busca endereços do usuário."""
        from app.services.user_address_service import UserAddressService

        mensagem = UserAddressService.format_address_list_message(self.usuario_id)
        return {"mensagem_formatada": mensagem}

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna a mensagem formatada pelo serviço."""
        return data.get("mensagem_formatada", "❌ Erro ao listar endereços.")


class DeletarEnderecoIntent(BaseIntent):
    """
    Handler para intent 'Deletar Endereço'.

    Remove um endereço favorito pelo label.

    Exemplo de mensagem:
    - "Remover endereço casa"
    - "Deletar endereço trabalho"
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai label do endereço a deletar."""
        label_data = gemini_service.extract_address_label_from_deletion(self.mensagem, self.usuario_id)
        return {
            "label": label_data.get('label', 'outro')
        }

    def validate(self) -> Optional[str]:
        """Sempre válido - fallback para 'outro'."""
        return None

    def execute(self) -> Dict[str, Any]:
        """Deleta endereço."""
        from app.services.user_address_service import UserAddressService

        sucesso, mensagem = UserAddressService.delete_address(
            self.usuario_id,
            self.params["label"]
        )

        return {
            "sucesso": sucesso,
            "mensagem": mensagem
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Retorna mensagem do serviço."""
        return data.get("mensagem", "❌ Erro ao deletar endereço.")


__all__ = [
    'ConfigurarEnderecoIntent',
    'ListarEnderecosIntent',
    'DeletarEnderecoIntent',
]
