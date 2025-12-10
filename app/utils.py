# app/utils.py (ADICIONAR ESTAS FUNÇÕES)
import locale
import time
import hmac
import hashlib
import secrets
import re
from functools import wraps
from sqlalchemy import text
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
    Chame no início de cada rota crítica.
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


# ============ FUNÇÕES DE SEGURANÇA ============

def verify_hmac_signature(payload: bytes, signature: str, secret_key: str) -> bool:
    """
    Verifica assinatura HMAC-SHA256 de um payload

    Args:
        payload: Dados em bytes
        signature: Assinatura recebida (hexadecimal)
        secret_key: Chave secreta para verificação

    Returns:
        True se assinatura é válida, False caso contrário
    """
    if not signature or not secret_key:
        return False

    try:
        expected_signature = hmac.new(
            secret_key.encode('utf-8'),
            payload,
            hashlib.sha256
        ).hexdigest()

        # Usa compare_digest para evitar timing attacks
        return secrets.compare_digest(signature, expected_signature)
    except Exception as e:
        print(f"[SECURITY] Erro ao verificar HMAC: {e}")
        return False


def generate_hmac_signature(payload: bytes, secret_key: str) -> str:
    """
    Gera assinatura HMAC-SHA256 de um payload

    Args:
        payload: Dados em bytes
        secret_key: Chave secreta

    Returns:
        Assinatura em hexadecimal
    """
    return hmac.new(
        secret_key.encode('utf-8'),
        payload,
        hashlib.sha256
    ).hexdigest()


def sanitize_for_log(data: dict) -> dict:
    """
    Remove campos sensíveis de dados antes de logar

    Args:
        data: Dicionário com dados

    Returns:
        Dicionário sanitizado (cópia)
    """
    if not data or not isinstance(data, dict):
        return data

    sanitized = data.copy()
    sensitive_keys = [
        'api_key', 'user_api_key', 'password', 'token',
        'secret', 'access_token', 'refresh_token',
        'authorization', 'x-api-key'
    ]

    for key in sensitive_keys:
        if key in sanitized:
            # Mostra apenas os primeiros 8 caracteres
            value = str(sanitized[key])
            sanitized[key] = f"{value[:8]}..." if len(value) > 8 else "[REDACTED]"

    return sanitized


def sanitize_input(text: str, max_length: int = 200, allow_special_chars: bool = False) -> str:
    """
    Sanitiza input de usuário para prevenir XSS e injection

    Args:
        text: Texto a sanitizar
        max_length: Comprimento máximo permitido
        allow_special_chars: Se False, remove caracteres especiais perigosos

    Returns:
        Texto sanitizado
    """
    if not text:
        return text

    # 1. Limitar tamanho
    text = text[:max_length]

    # 2. Remover caracteres de controle (exceto espaço, tab, newline)
    text = ''.join(char for char in text if ord(char) >= 32 or char in '\t\n\r')

    # 3. Remover caracteres perigosos se necessário
    if not allow_special_chars:
        # Remove: < > { } \ $ ` | ; &
        dangerous_chars = r'[<>{}\\$`|;&]'
        text = re.sub(dangerous_chars, '', text)

    # 4. Normalizar espaços múltiplos
    text = re.sub(r'\s+', ' ', text)

    return text.strip()


def compare_keys_safe(key1: str, key2: str) -> bool:
    """
    Compara duas chaves de forma segura (timing-attack resistant)

    Args:
        key1: Primeira chave
        key2: Segunda chave

    Returns:
        True se as chaves são iguais
    """
    if not key1 or not key2:
        return False

    return secrets.compare_digest(key1, key2)


# ============ FUNÇÕES DE FORMATAÇÃO DE DATA ============

# Dicionários de tradução para português brasileiro
MESES_PT_BR = {
    'January': 'Janeiro',
    'February': 'Fevereiro',
    'March': 'Março',
    'April': 'Abril',
    'May': 'Maio',
    'June': 'Junho',
    'July': 'Julho',
    'August': 'Agosto',
    'September': 'Setembro',
    'October': 'Outubro',
    'November': 'Novembro',
    'December': 'Dezembro'
}

DIAS_SEMANA_PT_BR = {
    'Monday': 'Segunda-feira',
    'Tuesday': 'Terça-feira',
    'Wednesday': 'Quarta-feira',
    'Thursday': 'Quinta-feira',
    'Friday': 'Sexta-feira',
    'Saturday': 'Sábado',
    'Sunday': 'Domingo'
}


def formatar_mes_pt(data):
    """
    Formata o nome do mês em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Nome do mês em português (ex: "Janeiro", "Dezembro")
    """
    mes_en = data.strftime('%B')
    return MESES_PT_BR.get(mes_en, mes_en)


def formatar_mes_ano_pt(data):
    """
    Formata mês/ano em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Mês/Ano em português (ex: "Janeiro/2025", "Dezembro/2024")
    """
    mes_pt = formatar_mes_pt(data)
    ano = data.strftime('%Y')
    return f"{mes_pt}/{ano}"


def formatar_dia_semana_pt(data):
    """
    Formata o nome do dia da semana em português brasileiro.

    Args:
        data: objeto date ou datetime

    Returns:
        str: Nome do dia em português (ex: "Segunda-feira", "Sábado")
    """
    dia_en = data.strftime('%A')
    return DIAS_SEMANA_PT_BR.get(dia_en, dia_en)