"""
Módulo de gerenciamento de cache do Gemini AI.

Rotas para limpar e visualizar estatísticas do cache.
"""

from flask import Blueprint, request
from app.shared.decorators import require_api_key, handle_errors
from app.shared.responses import ApiResponse

# Blueprint para cache management
cache_bp = Blueprint('admin_cache', __name__)


@cache_bp.route('/gemini-cache-clear', methods=['POST'])
@require_api_key
@handle_errors(tag="GEMINI-CACHE-CLEAR")
def gemini_cache_clear():
    """
    Limpa cache do Gemini AI por padrão ou tudo.

    Body JSON (opcional):
    {
        "pattern": "intent:*",  # Limpar apenas intents
        "usuario_id": 123       # Limpar apenas de um usuário
    }

    Se não passar nada no body, limpa TUDO.

    Exemplo:
    POST https://seu-backend.onrender.com/admin/gemini-cache-clear
    Header: x-api-key: sua_chave_secreta
    Body: {"pattern": "intent:*"}  # Limpa só intents
    """
    from app.services.gemini_cache_service import gemini_cache_service

    data = request.get_json() or {}
    pattern = data.get('pattern')
    usuario_id = data.get('usuario_id')

    if usuario_id:
        # Limpar cache de usuário específico
        deleted = gemini_cache_service.invalidate_user_cache(usuario_id, pattern)
        mensagem = f"{deleted} chaves deletadas para usuário {usuario_id}"
    elif pattern:
        # Limpar por padrão
        deleted = gemini_cache_service.invalidate_pattern(pattern)
        mensagem = f"{deleted} chaves deletadas (pattern: {pattern})"
    else:
        # Limpar TUDO
        deleted = gemini_cache_service.invalidate_pattern('*')
        mensagem = f"Cache completo limpo: {deleted} chaves deletadas"

    return ApiResponse.success(mensagem, keys_deleted=deleted)


@cache_bp.route('/gemini-cache-stats', methods=['GET'])
@require_api_key
@handle_errors(tag="GEMINI-CACHE-STATS")
def gemini_cache_stats():
    """
    Retorna estatísticas do cache do Gemini AI.

    Mostra:
    - Total de requisições (hits + misses)
    - Cache hits e misses
    - Hit rate geral
    - Breakdown por tipo de operação (intent, category, extract_trans, etc)
    - Economia estimada de quota

    Exemplo:
    GET https://seu-backend.onrender.com/admin/gemini-cache-stats
    Header: x-api-key: sua_chave_secreta

    Resposta:
    {
        "status": "sucesso",
        "total_requests": 1000,
        "cache_hits": 650,
        "cache_misses": 350,
        "cache_saves": 650,
        "cache_errors": 0,
        "hit_rate": "65.0%",
        "breakdown_by_type": {...},
        "estimated_savings": {...}
    }
    """
    from app.services.gemini_cache_service import gemini_cache_service

    stats = gemini_cache_service.get_cache_stats()

    return ApiResponse.success(
        "Estatísticas do cache obtidas com sucesso",
        **stats
    )
