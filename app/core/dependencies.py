"""
Utilitários para injeção de dependências.

Fornece decorators e helpers para usar DI em rotas Flask.
"""

from functools import wraps
from typing import Callable, Any
from contextlib import contextmanager
from flask import g
from sqlalchemy.orm import Session

from app.core.container import get_container


@contextmanager
def get_db_session() -> Session:
    """
    Context manager para obter sessão de banco de dados.

    Gerencia automaticamente commit/rollback e close.

    Usage:
        with get_db_session() as session:
            user_repo = SQLAlchemyUserRepository(session)
            user = user_repo.get_by_id(1)
            # Commit automático ao sair do bloco

    Yields:
        Session: Sessão SQLAlchemy
    """
    container = get_container()
    session = container.database_session()

    try:
        yield session
        session.commit()
    except Exception:
        session.rollback()
        raise
    finally:
        session.close()


def inject_repositories(*repo_names: str):
    """
    Decorator para injetar repositórios em rotas Flask.

    Repositórios são injetados como kwargs na função decorada.

    Args:
        *repo_names: Nomes dos repositórios a injetar
            Opções: 'user', 'account', 'transaction'

    Usage:
        @app.route('/users/<int:user_id>')
        @inject_repositories('user', 'account')
        def get_user(user_id, user_repository, account_repository):
            user = user_repository.get_by_id(user_id)
            accounts = account_repository.get_by_user(user_id)
            return {"user": user, "accounts": accounts}

    Note:
        A sessão é gerenciada automaticamente (commit/rollback/close)
    """
    def decorator(func: Callable) -> Callable:
        @wraps(func)
        def wrapper(*args, **kwargs):
            container = get_container()

            # Mapeamento de nomes para providers
            repo_providers = {
                'user': container.user_repository,
                'account': container.account_repository,
                'transaction': container.transaction_repository,
            }

            # Criar sessão
            session = container.database_session()

            try:
                # Injetar repositórios
                for repo_name in repo_names:
                    if repo_name not in repo_providers:
                        raise ValueError(f"Repository '{repo_name}' não existe")

                    # Instanciar repositório com sessão
                    repo = repo_providers[repo_name](session)

                    # Adicionar como kwarg
                    kwargs[f'{repo_name}_repository'] = repo

                # Executar função
                result = func(*args, **kwargs)

                # Commit se não houver exceção
                session.commit()

                return result

            except Exception:
                # Rollback em caso de erro
                session.rollback()
                raise

            finally:
                # Sempre fechar sessão
                session.close()

        return wrapper
    return decorator


def get_user_repository():
    """
    Helper para obter UserRepository com sessão gerenciada.

    Usa Flask g para armazenar sessão durante requisição.

    Usage:
        user_repo = get_user_repository()
        user = user_repo.get_by_id(1)

    Note:
        Requer contexto de requisição Flask
    """
    if 'db_session' not in g:
        container = get_container()
        g.db_session = container.database_session()

    container = get_container()
    return container.user_repository(g.db_session)


def get_account_repository():
    """Helper para obter AccountRepository."""
    if 'db_session' not in g:
        container = get_container()
        g.db_session = container.database_session()

    container = get_container()
    return container.account_repository(g.db_session)


def get_transaction_repository():
    """Helper para obter TransactionRepository."""
    if 'db_session' not in g:
        container = get_container()
        g.db_session = container.database_session()

    container = get_container()
    return container.transaction_repository(g.db_session)


def teardown_db_session(exception=None):
    """
    Teardown function para Flask.

    Fecha sessão ao final de cada requisição.

    Usage:
        app.teardown_appcontext(teardown_db_session)
    """
    session = g.pop('db_session', None)

    if session is not None:
        try:
            if exception:
                session.rollback()
            else:
                session.commit()
        finally:
            session.close()
