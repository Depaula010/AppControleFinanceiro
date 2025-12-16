"""
Core module - Configuração central da aplicação.

Contém:
- Dependency Injection container
- Helpers de injeção
- Feature flags para migração gradual
- Configurações globais
"""

from .container import Container, create_container, get_container
from .dependencies import (
    get_db_session,
    inject_repositories,
    get_user_repository,
    get_account_repository,
    get_transaction_repository,
    teardown_db_session,
)
from .feature_flags import feature_flags, get_feature_flags, FeatureFlags

__all__ = [
    # Container
    "Container",
    "create_container",
    "get_container",

    # Dependencies
    "get_db_session",
    "inject_repositories",
    "get_user_repository",
    "get_account_repository",
    "get_transaction_repository",
    "teardown_db_session",

    # Feature Flags
    "feature_flags",
    "get_feature_flags",
    "FeatureFlags",
]
