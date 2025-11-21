import json
import redis
from datetime import timedelta
from app.config import REDIS_URL

class RedisService:
    """Serviço centralizado para gerenciar o Redis"""
    
    def __init__(self):
        try:
            self.redis_client = redis.from_url(
                REDIS_URL,
                decode_responses=True,  # Retorna strings em vez de bytes
                socket_connect_timeout=5
            )
            # Testa a conexão
            self.redis_client.ping()
            print("[REDIS] ✅ Conexão estabelecida com sucesso!")
        except Exception as e:
            print(f"[REDIS] ❌ ERRO ao conectar: {e}")
            self.redis_client = None
    
    def is_connected(self):
        """Verifica se o Redis está conectado"""
        return self.redis_client is not None
    
    def set_with_ttl(self, key, value, ttl_seconds=300):
        """Define um valor com tempo de expiração (padrão: 5 minutos)"""
        if not self.is_connected():
            return False
        try:
            self.redis_client.setex(key, ttl_seconds, json.dumps(value))
            return True
        except Exception as e:
            print(f"[REDIS] Erro ao salvar {key}: {e}")
            return False
    
    def get(self, key):
        """Recupera um valor"""
        if not self.is_connected():
            return None
        try:
            value = self.redis_client.get(key)
            return json.loads(value) if value else None
        except Exception as e:
            print(f"[REDIS] Erro ao recuperar {key}: {e}")
            return None
    
    def delete(self, key):
        """Deleta uma chave"""
        if not self.is_connected():
            return False
        try:
            self.redis_client.delete(key)
            return True
        except Exception as e:
            print(f"[REDIS] Erro ao deletar {key}: {e}")
            return False
    
    def exists(self, key):
        """Verifica se uma chave existe"""
        if not self.is_connected():
            return False
        try:
            return self.redis_client.exists(key) > 0
        except Exception as e:
            print(f"[REDIS] Erro ao verificar {key}: {e}")
            return False

    def get_keys_by_pattern(self, pattern):
        """
        Busca chaves que correspondem a um padrão.

        Args:
            pattern: Padrão Redis (ex: "pending_event:553194001072:*")

        Returns:
            list: Lista de chaves encontradas
        """
        if not self.is_connected():
            return []
        try:
            return self.redis_client.keys(pattern)
        except Exception as e:
            print(f"[REDIS] Erro ao buscar padrão {pattern}: {e}")
            return []

# Instância global (singleton)
redis_service = RedisService()