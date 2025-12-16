"""
Container de Dependency Injection.

Centraliza a configuração e criação de todas as dependências da aplicação.
"""

import os
from dependency_injector import containers, providers
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker, Session

from app.infrastructure.database.repositories import (
    SQLAlchemyUserRepository,
    SQLAlchemyAccountRepository,
    SQLAlchemyTransactionRepository,
)


class Container(containers.DeclarativeContainer):
    """
    Container principal de Dependency Injection.

    Gerencia criação e injeção de:
    - Configurações
    - Database sessions
    - Repositórios
    - Serviços

    Usage:
        # Inicializar container
        container = Container()
        container.config.database_url.from_env("DATABASE_URL")

        # Obter dependências
        user_repo = container.user_repository()
        account_repo = container.account_repository()
    """

    # Configurações
    config = providers.Configuration()

    # Database Engine
    database_engine = providers.Singleton(
        create_engine,
        config.database_url,
        pool_pre_ping=True,  # Verificar conexão antes de usar
        pool_recycle=3600,   # Reciclar conexões a cada 1 hora
        echo=config.database_echo.as_(bool),  # Logar SQL (dev only)
    )

    # Session Factory
    database_session_factory = providers.Singleton(
        sessionmaker,
        bind=database_engine,
        autocommit=False,
        autoflush=False,
        expire_on_commit=False,
    )

    # Database Session (scope: request)
    database_session = providers.Factory(
        database_session_factory,
    )

    # ========================================================================
    # REPOSITÓRIOS
    # ========================================================================

    user_repository = providers.Factory(
        SQLAlchemyUserRepository,
        session=database_session,
    )

    account_repository = providers.Factory(
        SQLAlchemyAccountRepository,
        session=database_session,
    )

    transaction_repository = providers.Factory(
        SQLAlchemyTransactionRepository,
        session=database_session,
    )

    # ========================================================================
    # SERVIÇOS
    # ========================================================================

    # Import lazy para evitar imports circulares
    from app.application.services.user_service import UserService

    user_service = providers.Factory(
        UserService,
        user_repository=user_repository,
        account_repository=account_repository,
    )

    # Outros serviços a serem implementados:
    # finance_service = providers.Factory(
    #     FinanceService,
    #     user_repository=user_repository,
    #     account_repository=account_repository,
    #     transaction_repository=transaction_repository,
    # )


def create_container() -> Container:
    """
    Factory para criar e configurar o container.

    Returns:
        Container configurado e pronto para uso
    """
    container = Container()

    # Carregar configurações do ambiente
    container.config.database_url.from_env("DATABASE_URL")
    container.config.database_echo.from_env("DATABASE_ECHO", default="false")

    return container


# Container global (singleton)
_container = None


def get_container() -> Container:
    """
    Retorna o container global (singleton).

    Se ainda não foi criado, cria e configura.

    Returns:
        Container global
    """
    global _container
    if _container is None:
        _container = create_container()
    return _container
