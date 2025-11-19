# app/utils.py (ADICIONAR ESTAS FUNÇÕES)
import locale
import time
from functools import wraps
from sqlalchemy.exc import OperationalError, DBAPIError

def formatar_moeda(valor):
    """ 
    Tenta formatar como R$ (BRL). Se falhar, usa um formato simples. 
    Esta função agora é centralizada aqui.
    """
    if valor is None:
        return "R$ 0,00"
    try:
        # A chamada correta é para a biblioteca 'locale'.
        return locale.currency(valor, grouping=True)
    except Exception:
        # Se o locale 'pt_BR' não estiver disponível no servidor, usa um fallback manual.
        return f"R$ {valor:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


def with_db_retry(max_retries=3, retry_delay=1):
    """
    Decorator para retry automático em caso de falha de conexão DB.
    
    Uso:
        @with_db_retry(max_retries=3)
        def minha_funcao_db():
            with db_engine.connect() as conn:
                ...
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
    Retorna: (conectado: bool, mensagem: str)
    """
    from app import db_engine
    
    if not db_engine:
        return False, "Engine não configurado"
    
    try:
        with db_engine.connect() as conn:
            result = conn.execute("SELECT 1").scalar()
            if result == 1:
                return True, "Conexão OK"
            else:
                return False, "Query retornou resultado inesperado"
    except Exception as e:
        return False, f"Erro: {str(e)[:100]}"
    
def ensure_db_connection():
    """
    Garante que a conexão DB está ativa antes de prosseguir.
    Chame no início de cada rota crítica.
    """
    from app import db_engine
    
    if not db_engine:
        raise Exception("DB engine não configurado")
    
    try:
        with db_engine.connect() as conn:
            conn.execute("SELECT 1")
    except Exception as e:
        print(f"[DB] Conexão perdida, resetando pool: {e}")
        db_engine.dispose()
        
        # Tentar reconectar
        try:
            with db_engine.connect() as conn:
                conn.execute("SELECT 1")
            print("[DB] ✅ Reconectado com sucesso")
        except Exception as e2:
            print(f"[DB] ❌ Falha ao reconectar: {e2}")
            raise