"""
Context manager para transações de banco de dados.

Elimina código duplicado presente em 100+ lugares.
"""

from contextlib import contextmanager
from typing import Generator
from sqlalchemy.engine import Connection

from app import db_engine


@contextmanager
def db_transaction() -> Generator[Connection, None, None]:
    """
    Context manager para transações de banco de dados.

    Elimina código duplicado como:
        with db_engine.connect() as conn:
            conn.begin()
            # ... operações SQL ...
            conn.commit()

    Gerencia automaticamente:
    - Conexão ao banco
    - Início de transação
    - Commit em caso de sucesso
    - Rollback em caso de erro
    - Fechamento da conexão

    Usage:
        from app.shared.database.transaction_manager import db_transaction

        # Uso básico
        with db_transaction() as conn:
            conn.execute(text("INSERT INTO..."))
            conn.execute(text("UPDATE..."))
        # Commit automático no final

        # Com erro (rollback automático)
        try:
            with db_transaction() as conn:
                conn.execute(text("INSERT INTO..."))
                raise ValueError("Erro!")  # Rollback automático
        except ValueError:
            print("Transação foi revertida")

    Yields:
        Connection: Conexão do SQLAlchemy com transação iniciada

    Raises:
        Exception: Se db_engine não configurado
        Qualquer exceção das operações SQL (após rollback)
    """
    if not db_engine:
        raise Exception("Banco de dados não configurado")

    conn = db_engine.connect()
    try:
        conn.begin()
        yield conn
        conn.commit()
    except Exception as e:
        conn.rollback()
        raise e
    finally:
        conn.close()


@contextmanager
def db_connection() -> Generator[Connection, None, None]:
    """
    Context manager para conexão simples (sem transação automática).

    Similar ao db_transaction(), mas sem begin/commit automático.
    Útil para operações read-only.

    Usage:
        from app.shared.database.transaction_manager import db_connection

        with db_connection() as conn:
            result = conn.execute(text("SELECT * FROM..."))
            rows = result.fetchall()

    Yields:
        Connection: Conexão do SQLAlchemy

    Raises:
        Exception: Se db_engine não configurado
        Qualquer exceção das operações SQL
    """
    if not db_engine:
        raise Exception("Banco de dados não configurado")

    conn = db_engine.connect()
    try:
        yield conn
    finally:
        conn.close()


def execute_in_transaction(func):
    """
    Decorator para executar função dentro de uma transação.

    Elimina necessidade de escrever 'with db_transaction()' repetidamente.

    Usage:
        from app.shared.database.transaction_manager import execute_in_transaction

        @execute_in_transaction
        def create_user(conn, nome, email):
            # conn já está em uma transação
            conn.execute(text("INSERT INTO Usuarios..."))
            conn.execute(text("INSERT INTO Logs..."))
            # Commit automático no final

        # Chamar função normalmente
        create_user(nome="João", email="joao@example.com")

    Note:
        A função decorada DEVE aceitar 'conn' como primeiro argumento.
    """
    from functools import wraps

    @wraps(func)
    def wrapper(*args, **kwargs):
        with db_transaction() as conn:
            # Injetar conn como primeiro argumento
            return func(conn, *args, **kwargs)
    return wrapper
