# app/services/gemini_cache_service.py
import json
import hashlib
import time
from datetime import datetime
from app.services.redis_service import redis_service
from app.services.gemini_service import safe_generate_content


# Configuração de TTL por tipo de operação (em segundos)
CACHE_TTL_CONFIG = {
    'intent': 604800,        # 7 dias - Intents são globais e fixos
    'category': 2592000,     # 30 dias - Usuários frequentam mesmos lugares
    'extract_trans': 86400,  # 24 horas - Extrações podem variar
    'period_query': 604800,  # 7 dias - Queries determinísticas
    'calendar_query': 604800,# 7 dias - Queries determinísticas
    'chart_type': 604800,    # 7 dias
    'fatura_query': 604800,  # 7 dias
    'saldo_query': 604800,   # 7 dias
    'default': 259200        # 3 dias (fallback)
}


class CachedGeminiResponse:
    """
    Mock object que simula response do Gemini para dados cacheados.
    Compatível com get_gemini_text_response() que acessa response.text
    """
    def __init__(self, text, prompt_feedback=None):
        self.text = text
        self.prompt_feedback = prompt_feedback or "CACHED_RESPONSE"


class GeminiCacheService:
    """
    Serviço de cache para chamadas ao Gemini API.
    Reduz quota usage cacheando respostas determinísticas.

    Funcionalidades:
    - Cache transparente (wrapper para safe_generate_content)
    - TTL diferenciado por tipo de operação
    - Métricas (hits, misses, saves, errors)
    - Invalidação seletiva por usuário
    - Fallback gracioso se Redis offline
    """

    def __init__(self):
        self.redis = redis_service

        # Métricas detalhadas por tipo
        self.metrics = {
            'total': {'hits': 0, 'misses': 0, 'saves': 0, 'errors': 0},
            'intent': {'hits': 0, 'misses': 0},
            'category': {'hits': 0, 'misses': 0},
            'extract_trans': {'hits': 0, 'misses': 0},
            'period_query': {'hits': 0, 'misses': 0},
            'calendar_query': {'hits': 0, 'misses': 0},
            'chart_type': {'hits': 0, 'misses': 0},
            'fatura_query': {'hits': 0, 'misses': 0},
            'saldo_query': {'hits': 0, 'misses': 0},
            'other': {'hits': 0, 'misses': 0}
        }

    def _generate_cache_key(self, prompt, key_prefix, usuario_id=None):
        """
        Gera chave única e consistente para cache.

        Args:
            prompt: Prompt completo enviado ao Gemini
            key_prefix: Prefixo do tipo (intent, category, etc)
            usuario_id: ID do usuário (None = global)

        Returns:
            str: Chave no formato gemini_cache:{prefix}:{user}:{hash}

        Examples:
            gemini_cache:intent:global:a3f5d8e9c2b1
            gemini_cache:category:123:8b4c7f1a
        """
        # Hash MD5 do prompt (rápido e suficiente para cache)
        prompt_hash = hashlib.md5(prompt.encode('utf-8')).hexdigest()[:12]

        # Usar 'global' se não for user-specific
        user_part = str(usuario_id) if usuario_id else 'global'

        return f"gemini_cache:{key_prefix}:{user_part}:{prompt_hash}"

    def _serialize_response(self, response):
        """
        Serializa response do Gemini para salvar no Redis.

        Args:
            response: Response object do Gemini

        Returns:
            dict: Dados serializáveis {text, prompt_feedback, cached_at}
        """
        try:
            return {
                'text': response.text,
                'prompt_feedback': str(response.prompt_feedback) if hasattr(response, 'prompt_feedback') else None,
                'cached_at': datetime.now().isoformat()
            }
        except Exception as e:
            print(f"[GEMINI-CACHE] Erro ao serializar response: {e}")
            return None

    def _deserialize_response(self, cached_data):
        """
        Deserializa dados do cache para mock response object.

        Args:
            cached_data: Dict com {text, prompt_feedback, cached_at}

        Returns:
            CachedGeminiResponse: Mock object compatível com Gemini response
        """
        return CachedGeminiResponse(
            text=cached_data['text'],
            prompt_feedback=cached_data.get('prompt_feedback')
        )

    def cached_generate_content(self, model, prompt, cache_config=None):
        """
        Wrapper cacheado para safe_generate_content().

        Args:
            model: Gemini model instance
            prompt: Prompt string
            cache_config: {
                'enabled': bool (default: True),
                'ttl': int (segundos, default: CACHE_TTL_CONFIG['default']),
                'key_prefix': str (default: 'default'),
                'user_specific': bool (default: False),
                'usuario_id': int (obrigatório se user_specific=True)
            }

        Returns:
            Response object (do cache ou do Gemini)

        Fluxo:
            1. Verifica se cache está habilitado
            2. Gera cache_key
            3. Busca no Redis
            4. Se HIT: retorna cached response (metrics.hits++)
            5. Se MISS: chama Gemini, salva no Redis, retorna (metrics.misses++)
            6. Fallback: se Redis offline, chama Gemini direto
        """
        # Configuração padrão
        if cache_config is None:
            cache_config = {}

        enabled = cache_config.get('enabled', True)
        key_prefix = cache_config.get('key_prefix', 'default')
        user_specific = cache_config.get('user_specific', False)
        usuario_id = cache_config.get('usuario_id')
        ttl = cache_config.get('ttl', CACHE_TTL_CONFIG.get(key_prefix, CACHE_TTL_CONFIG['default']))

        # Validação: user_specific requer usuario_id
        if user_specific and not usuario_id:
            raise ValueError("user_specific cache requires usuario_id")

        # Se cache desabilitado, chamar Gemini direto
        if not enabled:
            return safe_generate_content(model, prompt)

        # Se Redis não está conectado, bypass cache
        if not self.redis.is_connected():
            print("[GEMINI-CACHE] Redis offline, bypassing cache")
            return safe_generate_content(model, prompt)

        # Início do timer para métricas
        start_time = time.time()

        # Gerar chave de cache
        cache_key = self._generate_cache_key(
            prompt,
            key_prefix,
            usuario_id if user_specific else None
        )

        # Tentar buscar do cache
        try:
            cached_data = self.redis.get(cache_key)

            if cached_data:
                # CACHE HIT
                elapsed_ms = (time.time() - start_time) * 1000
                print(f"[GEMINI-CACHE-HIT] {key_prefix} | {elapsed_ms:.1f}ms | Key: {cache_key}")

                # Incrementar métricas
                self.metrics['total']['hits'] += 1
                if key_prefix in self.metrics:
                    self.metrics[key_prefix]['hits'] += 1
                else:
                    self.metrics['other']['hits'] += 1

                # Deserializar e retornar
                return self._deserialize_response(cached_data)

        except Exception as e:
            print(f"[GEMINI-CACHE] Erro ao buscar cache: {e}")
            self.metrics['total']['errors'] += 1

        # CACHE MISS - Chamar Gemini
        try:
            response = safe_generate_content(model, prompt)
            elapsed_ms = (time.time() - start_time) * 1000
            print(f"[GEMINI-CACHE-MISS] {key_prefix} | {elapsed_ms:.0f}ms | Gemini called | Key: {cache_key}")

            # Incrementar métricas de miss
            self.metrics['total']['misses'] += 1
            if key_prefix in self.metrics:
                self.metrics[key_prefix]['misses'] += 1
            else:
                self.metrics['other']['misses'] += 1

            # Salvar no cache
            try:
                serialized = self._serialize_response(response)
                if serialized:
                    success = self.redis.set_with_ttl(cache_key, serialized, ttl)
                    if success:
                        self.metrics['total']['saves'] += 1
                        print(f"[GEMINI-CACHE-SAVE] {key_prefix} | TTL: {ttl}s | Key: {cache_key}")
                    else:
                        print(f"[GEMINI-CACHE] Falha ao salvar no Redis")
            except Exception as e:
                print(f"[GEMINI-CACHE] Erro ao salvar cache: {e}")
                self.metrics['total']['errors'] += 1

            return response

        except Exception as e:
            # Erro ao chamar Gemini
            print(f"[GEMINI-CACHE] Erro ao chamar Gemini: {e}")
            self.metrics['total']['errors'] += 1
            raise  # Re-lançar exceção para não mascarar erros do Gemini

    def invalidate_user_cache(self, usuario_id, pattern=None):
        """
        Invalida cache de um usuário específico.

        Args:
            usuario_id: ID do usuário
            pattern: Padrão de chave (ex: 'category:*', None = tudo)

        Examples:
            invalidate_user_cache(123, 'category:*')
            → Deleta: gemini_cache:category:123:*

            invalidate_user_cache(123)
            → Deleta: gemini_cache:*:123:*
        """
        if not self.redis.is_connected():
            print("[GEMINI-CACHE] Redis offline, cannot invalidate cache")
            return 0

        try:
            # Construir padrão completo
            if pattern:
                full_pattern = f'gemini_cache:{pattern}:{usuario_id}:*'
            else:
                full_pattern = f'gemini_cache:*:{usuario_id}:*'

            # Buscar chaves correspondentes
            keys = self.redis.get_keys_by_pattern(full_pattern)

            # Deletar todas
            deleted_count = 0
            for key in keys:
                if self.redis.delete(key):
                    deleted_count += 1

            print(f"[CACHE-INVALIDATE] {deleted_count} keys deleted for user {usuario_id} (pattern: {pattern or 'all'})")
            return deleted_count

        except Exception as e:
            print(f"[GEMINI-CACHE] Erro ao invalidar cache: {e}")
            return 0

    def invalidate_pattern(self, pattern):
        """
        Invalida cache global por padrão (ex: 'intent:*').

        Args:
            pattern: Padrão de chave (ex: 'intent:*')

        Example:
            invalidate_pattern('intent:*')
            → Deleta: gemini_cache:intent:*:*
        """
        if not self.redis.is_connected():
            print("[GEMINI-CACHE] Redis offline, cannot invalidate cache")
            return 0

        try:
            full_pattern = f'gemini_cache:{pattern}:*'
            keys = self.redis.get_keys_by_pattern(full_pattern)

            deleted_count = 0
            for key in keys:
                if self.redis.delete(key):
                    deleted_count += 1

            print(f"[CACHE-INVALIDATE] {deleted_count} keys deleted (pattern: {pattern})")
            return deleted_count

        except Exception as e:
            print(f"[GEMINI-CACHE] Erro ao invalidar cache: {e}")
            return 0

    def get_metrics(self):
        """
        Retorna estatísticas de cache.

        Returns:
            dict: Métricas completas com breakdown por tipo
        """
        total = self.metrics['total']
        total_requests = total['hits'] + total['misses']
        hit_rate = (total['hits'] / total_requests * 100) if total_requests > 0 else 0

        # Breakdown por tipo
        breakdown = {}
        for key_prefix in ['intent', 'category', 'extract_trans', 'period_query',
                          'calendar_query', 'chart_type', 'fatura_query', 'saldo_query', 'other']:
            metrics = self.metrics[key_prefix]
            total_type = metrics['hits'] + metrics['misses']
            type_hit_rate = (metrics['hits'] / total_type * 100) if total_type > 0 else 0

            breakdown[key_prefix] = {
                'hits': metrics['hits'],
                'misses': metrics['misses'],
                'total': total_type,
                'hit_rate': f"{type_hit_rate:.1f}%"
            }

        return {
            'total_requests': total_requests,
            'cache_hits': total['hits'],
            'cache_misses': total['misses'],
            'cache_saves': total['saves'],
            'cache_errors': total['errors'],
            'hit_rate': f"{hit_rate:.1f}%",
            'breakdown_by_type': breakdown,
            'estimated_savings': {
                'calls_saved': total['hits'],
                'quota_saved_pct': f"{hit_rate:.1f}%"
            }
        }

    def get_hit_rate(self):
        """
        Calcula hit rate percentual.

        Returns:
            float: Hit rate (0-100)
        """
        total = self.metrics['total']
        total_requests = total['hits'] + total['misses']
        return (total['hits'] / total_requests * 100) if total_requests > 0 else 0


# Instância global (singleton)
gemini_cache_service = GeminiCacheService()
