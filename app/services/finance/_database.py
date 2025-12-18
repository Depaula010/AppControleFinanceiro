# app/services/finance/_database.py
"""
Utilitários de banco de dados compartilhados pelos serviços financeiros.

Este módulo contém imports comuns e funções auxiliares de database
utilizadas por todos os serviços do domínio finance.
"""

from sqlalchemy import text
from datetime import date, datetime, time, timedelta
from calendar import monthrange
from typing import Any, Optional, Dict, List, Tuple
from sqlalchemy.engine import Connection

# Importa o "Singleton" do motor de banco de dados
from app import db_engine


def execute_query(conn: Connection, query: str, params: Optional[Dict[str, Any]] = None) -> Any:
    """
    Executa uma query SQL com parâmetros.

    Args:
        conn: Conexão do SQLAlchemy
        query: Query SQL (pode usar :param_name para parâmetros)
        params: Dicionário com parâmetros nomeados

    Returns:
        Resultado da execução
    """
    if params:
        return conn.execute(text(query), params)
    return conn.execute(text(query))


def fetchone(conn: Connection, query: str, params: Optional[Dict[str, Any]] = None) -> Optional[Any]:
    """
    Executa query e retorna um único resultado.

    Args:
        conn: Conexão do SQLAlchemy
        query: Query SQL
        params: Parâmetros da query

    Returns:
        Primeira linha do resultado ou None
    """
    result = execute_query(conn, query, params)
    return result.fetchone()


def fetchall(conn: Connection, query: str, params: Optional[Dict[str, Any]] = None) -> List[Any]:
    """
    Executa query e retorna todos os resultados.

    Args:
        conn: Conexão do SQLAlchemy
        query: Query SQL
        params: Parâmetros da query

    Returns:
        Lista com todas as linhas do resultado
    """
    result = execute_query(conn, query, params)
    return result.fetchall()


__all__ = [
    'text',
    'date',
    'datetime',
    'time',
    'timedelta',
    'monthrange',
    'Connection',
    'db_engine',
    'execute_query',
    'fetchone',
    'fetchall',
]
