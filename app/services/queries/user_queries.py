"""
Queries SQL centralizadas para Usuários (Usuarios).

Este módulo contém todas as queries SQL relacionadas a usuários
para evitar duplicação de código e facilitar manutenção.

IMPORTANTE: Ao modificar uma query aqui, a mudança afeta TODOS os lugares que a utilizam.
"""

from sqlalchemy import text
from typing import Dict, Any


class UserQueries:
    """
    Queries SQL reutilizáveis para operações com Usuários.
    """

    @staticmethod
    def get_all_users_with_api_key() -> text:
        """
        Busca todos os usuários que possuem API key configurada.

        Usado em:
        - Jobs que processam todos os usuários
        - Envio de notificações em massa
        - Relatórios automáticos

        Parâmetros necessários:
            Nenhum

        Retorna: id, numero_whatsapp, api_key_automate
        """
        return text("""
            SELECT id, numero_whatsapp, api_key_automate
            FROM Usuarios
            WHERE api_key_automate IS NOT NULL
            ORDER BY id
        """)

    @staticmethod
    def get_user_by_whatsapp() -> text:
        """
        Busca usuário pelo número de WhatsApp.

        Usado em:
        - Webhook do WhatsApp (identificar usuário que enviou mensagem)
        - Autenticação por número de telefone
        - Busca de usuário para envio de notificação

        Parâmetros necessários:
            :num (str) - Número do WhatsApp (formato: +5511999999999)

        Retorna: id do usuário
        """
        return text("""
            SELECT id
            FROM Usuarios
            WHERE numero_whatsapp = :num
        """)

    @staticmethod
    def get_user_default_accounts() -> text:
        """
        Busca contas padrão do usuário (renda e despesa).

        Usado em:
        - Criação automática de transações
        - Sugerir conta para registro rápido
        - Fallback quando usuário não especifica conta

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: conta_padrao_renda_id, conta_padrao_despesa_id

        NOTA: Pode retornar NULL se usuário não configurou contas padrão
        """
        return text("""
            SELECT conta_padrao_renda_id, conta_padrao_despesa_id
            FROM Usuarios
            WHERE id = :uid
        """)

    @staticmethod
    def get_user_full_info() -> text:
        """
        Busca informações completas do usuário.

        Usado em:
        - Perfil do usuário
        - Configurações
        - Debug e logs

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: Todos os campos do usuário
        """
        return text("""
            SELECT
                id,
                nome,
                numero_whatsapp,
                api_key_automate,
                conta_padrao_renda_id,
                conta_padrao_despesa_id,
                created_at,
                updated_at
            FROM Usuarios
            WHERE id = :uid
        """)

    @staticmethod
    def check_user_exists() -> text:
        """
        Verifica se um usuário existe.

        Usado em:
        - Validação antes de criar usuário
        - Verificar se WhatsApp está cadastrado

        Parâmetros necessários:
            :num (str) - Número do WhatsApp

        Retorna: Contador (1 se existe, 0 se não)
        """
        return text("""
            SELECT COUNT(*) as existe
            FROM Usuarios
            WHERE numero_whatsapp = :num
        """)

    @staticmethod
    def update_default_income_account() -> text:
        """
        Atualiza conta padrão de renda do usuário.

        Usado em:
        - Configuração de conta padrão
        - Atualização de preferências

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :conta_id (int) - ID da conta padrão de renda

        Retorna: Nenhum (UPDATE)
        """
        return text("""
            UPDATE Usuarios
            SET conta_padrao_renda_id = :conta_id,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = :uid
        """)

    @staticmethod
    def update_default_expense_account() -> text:
        """
        Atualiza conta padrão de despesa do usuário.

        Usado em:
        - Configuração de conta padrão
        - Atualização de preferências

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :conta_id (int) - ID da conta padrão de despesa

        Retorna: Nenhum (UPDATE)
        """
        return text("""
            UPDATE Usuarios
            SET conta_padrao_despesa_id = :conta_id,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = :uid
        """)

    @staticmethod
    def get_parametros_usuario(usuario_id: int) -> Dict[str, Any]:
        """
        Retorna parâmetros padrão para queries de usuário.

        Args:
            usuario_id: ID do usuário

        Returns:
            Dict com parâmetros comuns
        """
        return {
            "uid": usuario_id
        }

    @staticmethod
    def get_parametros_whatsapp(numero_whatsapp: str) -> Dict[str, Any]:
        """
        Retorna parâmetros para busca por WhatsApp.

        Args:
            numero_whatsapp: Número do WhatsApp

        Returns:
            Dict com parâmetros
        """
        return {
            "num": numero_whatsapp
        }
