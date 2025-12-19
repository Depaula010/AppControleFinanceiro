# app/infrastructure/database/connection.py
"""
Wrapper de conexao para use cases.

Fornece um context manager get_db_connection() que abstrai
a obtencao de conexao do db_engine global.
"""

from contextlib import contextmanager


@contextmanager
def get_db_connection():
    """
    Context manager para obter uma conexao do banco.
    
    Uso:
        with get_db_connection() as conn:
            result = conn.execute(query)
            conn.commit()
    
    Returns:
        Connection: Conexao SQLAlchemy ativa
    """
    from app import db_engine
    
    if not db_engine:
        raise RuntimeError("Database engine nao configurado")
    
    conn = db_engine.connect()
    try:
        yield conn
    except Exception:
        conn.rollback()
        raise
    finally:
        conn.close()


__all__ = ['get_db_connection']
