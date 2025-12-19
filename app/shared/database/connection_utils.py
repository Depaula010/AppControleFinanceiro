# app/shared/database/connection_utils.py
"""
Utilitários para gerenciamento de conexões de banco de dados
"""
import time
from functools import wraps
from sqlalchemy import text
from sqlalchemy.exc import OperationalError, DBAPIError


def with_db_retry(max_retries=3, retry_delay=1):
    """
    Decorator para retry automático em caso de falha de conexão DB.

    Args:
        max_retries: Número máximo de tentativas
        retry_delay: Tempo em segundos entre tentativas (multiplicado por tentativa)

    Usage:
        @with_db_retry(max_retries=3)
        def minha_funcao_db():
            with db_engine.connect() as conn:
                ...

    Examples:
        >>> @with_db_retry(max_retries=2, retry_delay=0.1)
        ... def query_db():
        ...     # Código que pode falhar
        ...     pass
    """
    def decorator(func):
        @wraps(func)
        def wrapper(*args, **kwargs):
            last_exception = None

            for attempt in range(max_retries):
                try:
                    return func(*args, **kwargs)

                except (OperationalError, DBAPIError) as e:
                    last_exception = e
                    error_msg = str(e).lower()

                    # Verificar se é erro de conexão
                    is_connection_error = any(keyword in error_msg for keyword in [
                        'connection', 'closed', 'terminated',
                        'timeout', 'network', 'broken pipe'
                    ])

                    if is_connection_error and attempt < max_retries - 1:
                        wait_time = retry_delay * (attempt + 1)
                        print(f"[DB-RETRY] Tentativa {attempt + 1}/{max_retries} falhou: {str(e)[:100]}")
                        print(f"[DB-RETRY] Aguardando {wait_time}s antes de tentar novamente...")
                        time.sleep(wait_time)

                        # Força disposal do pool para reconectar
                        from app import db_engine
                        if db_engine:
                            try:
                                db_engine.dispose()
                                print("[DB-RETRY] Pool de conexões resetado")
                            except:
                                pass

                        continue
                    else:
                        # Não é erro de conexão ou última tentativa
                        raise

                except Exception as e:
                    # Outros erros não relacionados à conexão
                    raise

            # Se chegou aqui, todas as tentativas falharam
            raise last_exception

        return wrapper
    return decorator


def check_db_connection():
    """
    Verifica se a conexão com o banco está ativa.

    Returns:
        tuple: (conectado: bool, mensagem: str)

    Examples:
        >>> conectado, msg = check_db_connection()
        >>> print(f"Status: {msg}")
    """
    from app import db_engine

    if not db_engine:
        return False, "Engine não configurado"

    try:
        with db_engine.connect() as conn:
            result = conn.execute(text("SELECT 1")).scalar()
            if result == 1:
                return True, "Conexão OK"
            else:
                return False, "Query retornou resultado inesperado"
    except Exception as e:
        return False, f"Erro: {str(e)[:100]}"


def ensure_db_connection():
    """
    Garante que a conexão DB está ativa antes de prosseguir.
    Tenta reconectar automaticamente se a conexão for perdida.

    Raises:
        Exception: Se não conseguir estabelecer conexão

    Usage:
        # Chame no início de cada rota crítica
        ensure_db_connection()
        with db_engine.connect() as conn:
            # ... queries

    Examples:
        >>> try:
        ...     ensure_db_connection()
        ...     print("DB conectado")
        ... except Exception as e:
        ...     print(f"Falha: {e}")
    """
    from app import db_engine

    if not db_engine:
        raise Exception("DB engine não configurado")

    try:
        with db_engine.connect() as conn:
            conn.execute(text("SELECT 1"))
    except Exception as e:
        print(f"[DB] Conexão perdida, resetando pool: {e}")
        db_engine.dispose()

        # Tentar reconectar
        try:
            with db_engine.connect() as conn:
                conn.execute(text("SELECT 1"))
            print("[DB] ✅ Reconectado com sucesso")
        except Exception as e2:
            print(f"[DB] ❌ Falha ao reconectar: {e2}")
            raise
