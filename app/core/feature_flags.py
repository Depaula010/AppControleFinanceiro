"""
Sistema de Feature Flags para migração gradual SQL → ORM.

Permite habilitar/desabilitar uso do ORM por módulo sem quebrar
funcionalidades existentes.

Usage:
    from app.core.feature_flags import feature_flags

    if feature_flags.use_orm_for_users:
        # Usar ORM
        user = user_repository.get_by_whatsapp(numero)
    else:
        # Usar SQL legado
        user = execute_sql_query(...)
"""

import os
from typing import Dict


class FeatureFlags:
    """
    Gerenciador de feature flags da aplicação.

    Lê configurações do ambiente (.env) e fornece acesso booleano
    para controlar comportamentos da aplicação.

    Flags disponíveis:
        - use_orm_for_users: Usar ORM para operações de usuários
        - use_orm_for_accounts: Usar ORM para operações de contas
        - use_orm_for_transactions: Usar ORM para operações de transações
        - use_orm_for_categories: Usar ORM para operações de categorias
        - use_orm_for_invoices: Usar ORM para operações de faturas
        - use_orm_for_schedules: Usar ORM para operações de agendamentos
        - use_orm_for_budgets: Usar ORM para operações de orçamentos
        - use_orm_globally: Ativar ORM para TODOS os módulos (override)

    Environment Variables:
        USE_ORM_FOR_USERS=true
        USE_ORM_FOR_ACCOUNTS=true
        USE_ORM_FOR_TRANSACTIONS=false
        USE_ORM_GLOBALLY=false
    """

    def __init__(self):
        """Inicializa feature flags lendo do ambiente."""
        self._flags: Dict[str, bool] = {}
        self._load_from_env()

    def _load_from_env(self) -> None:
        """Carrega flags do ambiente (.env)."""
        # Flag global (override todas as outras)
        self._flags['use_orm_globally'] = self._get_bool_env('USE_ORM_GLOBALLY', False)

        # Flags por módulo
        self._flags['use_orm_for_users'] = self._get_bool_env('USE_ORM_FOR_USERS', False)
        self._flags['use_orm_for_accounts'] = self._get_bool_env('USE_ORM_FOR_ACCOUNTS', False)
        self._flags['use_orm_for_transactions'] = self._get_bool_env('USE_ORM_FOR_TRANSACTIONS', False)
        self._flags['use_orm_for_categories'] = self._get_bool_env('USE_ORM_FOR_CATEGORIES', False)
        self._flags['use_orm_for_invoices'] = self._get_bool_env('USE_ORM_FOR_INVOICES', False)
        self._flags['use_orm_for_schedules'] = self._get_bool_env('USE_ORM_FOR_SCHEDULES', False)
        self._flags['use_orm_for_budgets'] = self._get_bool_env('USE_ORM_FOR_BUDGETS', False)

    def _get_bool_env(self, key: str, default: bool = False) -> bool:
        """
        Lê variável de ambiente como boolean.

        Args:
            key: Nome da variável
            default: Valor padrão

        Returns:
            True se valor for 'true', '1', 'yes', 'on' (case-insensitive)
        """
        value = os.getenv(key, str(default)).lower()
        return value in ('true', '1', 'yes', 'on')

    @property
    def use_orm_globally(self) -> bool:
        """
        Ativar ORM para TODOS os módulos.

        Se True, todos os outros flags são ignorados e ORM é usado.
        """
        return self._flags['use_orm_globally']

    @property
    def use_orm_for_users(self) -> bool:
        """Usar ORM para operações de usuários."""
        return self.use_orm_globally or self._flags['use_orm_for_users']

    @property
    def use_orm_for_accounts(self) -> bool:
        """Usar ORM para operações de contas."""
        return self.use_orm_globally or self._flags['use_orm_for_accounts']

    @property
    def use_orm_for_transactions(self) -> bool:
        """Usar ORM para operações de transações."""
        return self.use_orm_globally or self._flags['use_orm_for_transactions']

    @property
    def use_orm_for_categories(self) -> bool:
        """Usar ORM para operações de categorias."""
        return self.use_orm_globally or self._flags['use_orm_for_categories']

    @property
    def use_orm_for_invoices(self) -> bool:
        """Usar ORM para operações de faturas."""
        return self.use_orm_globally or self._flags['use_orm_for_invoices']

    @property
    def use_orm_for_schedules(self) -> bool:
        """Usar ORM para operações de agendamentos."""
        return self.use_orm_globally or self._flags['use_orm_for_schedules']

    @property
    def use_orm_for_budgets(self) -> bool:
        """Usar ORM para operações de orçamentos."""
        return self.use_orm_globally or self._flags['use_orm_for_budgets']

    def get_status(self) -> Dict[str, bool]:
        """
        Retorna status de todos os flags.

        Returns:
            Dicionário com todos os flags e seus valores
        """
        return {
            'use_orm_globally': self.use_orm_globally,
            'use_orm_for_users': self.use_orm_for_users,
            'use_orm_for_accounts': self.use_orm_for_accounts,
            'use_orm_for_transactions': self.use_orm_for_transactions,
            'use_orm_for_categories': self.use_orm_for_categories,
            'use_orm_for_invoices': self.use_orm_for_invoices,
            'use_orm_for_schedules': self.use_orm_for_schedules,
            'use_orm_for_budgets': self.use_orm_for_budgets,
        }

    def reload(self) -> None:
        """
        Recarrega flags do ambiente.

        Útil para testes ou mudanças em runtime.
        """
        self._load_from_env()


# Singleton global
feature_flags = FeatureFlags()


def get_feature_flags() -> FeatureFlags:
    """
    Retorna instância global de feature flags.

    Returns:
        Instância singleton de FeatureFlags
    """
    return feature_flags
