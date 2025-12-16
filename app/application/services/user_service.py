"""
Serviço de negócio para operações de usuários.

Camada de aplicação que orquestra repositórios e lógica de negócio.
"""

from typing import Optional
from datetime import datetime, timezone

from app.domain.repositories import IUserRepository, IAccountRepository
from app.infrastructure.database.models import UserModel


class UserService:
    """
    Serviço de usuários.

    Encapsula lógica de negócio relacionada a usuários.
    Usa repositórios via injeção de dependências.

    Usage:
        # Com DI container
        container = get_container()
        user_service = container.user_service()

        # Manual
        user_service = UserService(user_repo, account_repo)
        user = user_service.authenticate_by_whatsapp("+5511999999999")
    """

    def __init__(
        self,
        user_repository: IUserRepository,
        account_repository: IAccountRepository,
    ):
        """
        Inicializa serviço.

        Args:
            user_repository: Repositório de usuários (injetado)
            account_repository: Repositório de contas (injetado)
        """
        self.user_repo = user_repository
        self.account_repo = account_repository

    def authenticate_by_whatsapp(self, numero_whatsapp: str) -> Optional[UserModel]:
        """
        Autentica usuário por número de WhatsApp.

        Busca usuário e atualiza último acesso.

        Args:
            numero_whatsapp: Número do WhatsApp

        Returns:
            Usuário se encontrado e ativo, None caso contrário
        """
        # Buscar usuário
        user = self.user_repo.get_by_whatsapp(numero_whatsapp)

        if user is None:
            return None

        # Verificar se está ativo
        if not user.ativo:
            return None

        # Atualizar último acesso
        self.user_repo.update_last_access(user.id)

        return user

    def authenticate_by_api_key(self, api_key: str) -> Optional[UserModel]:
        """
        Autentica usuário por API key.

        Args:
            api_key: API key do Automate

        Returns:
            Usuário se encontrado e ativo
        """
        user = self.user_repo.get_by_api_key(api_key)

        if user is None or not user.ativo:
            return None

        self.user_repo.update_last_access(user.id)

        return user

    def get_user_summary(self, user_id: int) -> dict:
        """
        Retorna resumo completo do usuário.

        Args:
            user_id: ID do usuário

        Returns:
            Dicionário com dados do usuário e contas
        """
        # Buscar usuário
        user = self.user_repo.get_by_id(user_id)
        if user is None:
            raise ValueError("Usuário não encontrado")

        # Buscar contas ativas
        accounts = self.account_repo.get_active_by_user(user_id)

        return {
            "id": user.id,
            "nome": user.nome,
            "whatsapp": user.numero_whatsapp,
            "email": user.email,
            "fuso_horario": user.fuso_horario,
            "ativo": user.ativo,
            "ultimo_acesso": user.ultimo_acesso.isoformat() if user.ultimo_acesso else None,
            "contas": [
                {
                    "id": acc.id,
                    "nome": acc.nome_conta,
                    "tipo": acc.tipo_conta,
                    "ativa": acc.ativa,
                }
                for acc in accounts
            ],
            "total_contas": len(accounts),
        }

    def register_user(
        self,
        nome: str,
        numero_whatsapp: str,
        email: Optional[str] = None,
        fuso_horario: str = "America/Sao_Paulo",
    ) -> UserModel:
        """
        Registra novo usuário.

        Args:
            nome: Nome do usuário
            numero_whatsapp: Número WhatsApp (único)
            email: Email (opcional)
            fuso_horario: Timezone (padrão: America/Sao_Paulo)

        Returns:
            Usuário criado

        Raises:
            ValueError: Se WhatsApp ou email já existir
        """
        # Validar se WhatsApp já existe
        if self.user_repo.exists_by_whatsapp(numero_whatsapp):
            raise ValueError("WhatsApp já cadastrado")

        # Validar se email já existe (se fornecido)
        if email and self.user_repo.exists_by_email(email):
            raise ValueError("Email já cadastrado")

        # Criar usuário
        new_user = UserModel(
            nome=nome,
            numero_whatsapp=numero_whatsapp,
            email=email,
            fuso_horario=fuso_horario,
            ativo=True,
        )

        return self.user_repo.create(new_user)

    def update_user_email(self, user_id: int, email: str) -> bool:
        """
        Atualiza email do usuário.

        Args:
            user_id: ID do usuário
            email: Novo email

        Returns:
            True se atualizou

        Raises:
            ValueError: Se email já está em uso
        """
        # Verificar se email já existe
        existing_user = self.user_repo.get_by_email(email)
        if existing_user and existing_user.id != user_id:
            raise ValueError("Email já está em uso por outro usuário")

        # Atualizar
        result = self.user_repo.update(user_id, email=email)
        return result is not None

    def deactivate_user(self, user_id: int) -> bool:
        """
        Desativa usuário.

        Args:
            user_id: ID do usuário

        Returns:
            True se desativou
        """
        return self.user_repo.deactivate(user_id)

    def activate_user(self, user_id: int) -> bool:
        """
        Ativa usuário.

        Args:
            user_id: ID do usuário

        Returns:
            True se ativou
        """
        return self.user_repo.activate(user_id)
