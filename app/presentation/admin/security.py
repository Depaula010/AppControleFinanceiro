"""
Módulo de gerenciamento de segurança.

Rotas para gerenciar blacklist de IPs e visualizar estatísticas de segurança.
"""

from flask import Blueprint, request
from app.shared.decorators import require_api_key, handle_errors, validate_required_fields
from app.shared.responses import ApiResponse

# Blueprint para security
security_bp = Blueprint('admin_security', __name__)


@security_bp.route('/security-stats', methods=['GET'])
@require_api_key
@handle_errors(tag="SECURITY-STATS")
def security_stats():
    """
    Endpoint para visualizar estatísticas de segurança.

    Retorna:
    - IPs na blacklist permanente
    - IPs bloqueados temporariamente
    - Atividade suspeita recente
    - Totais de bloqueios e tentativas

    Exemplo:
    GET https://seu-backend.onrender.com/admin/security-stats
    Header: x-api-key: sua_chave_secreta
    """
    from app.middleware.security import get_security_stats

    stats = get_security_stats()
    return ApiResponse.success("Estatísticas de segurança obtidas", **stats)


@security_bp.route('/security-blacklist-add', methods=['POST'])
@require_api_key
@validate_required_fields('ip')
@handle_errors(tag="SECURITY-BLACKLIST-ADD")
def security_blacklist_add():
    """
    Adiciona um IP à blacklist permanente (bloqueado por 1 ano).

    Body JSON:
    {
        "ip": "192.168.1.100",
        "reason": "Motivo do bloqueio" (opcional)
    }

    Exemplo:
    POST https://seu-backend.onrender.com/admin/security-blacklist-add
    Header: x-api-key: sua_chave_secreta
    Body: {"ip": "172.19.0.6", "reason": "Tentativas repetidas de invasão"}
    """
    from app.middleware.security import blacklist_ip

    data = request.get_json()
    ip = data['ip']  # Já validado por @validate_required_fields
    reason = data.get('reason', 'Manual block via API')

    success = blacklist_ip(ip, reason)

    if success:
        return ApiResponse.success(
            f"IP {ip} adicionado à blacklist permanente",
            ip=ip,
            reason=reason
        )
    else:
        return ApiResponse.error(
            "Falha ao adicionar IP à blacklist (Redis indisponível)",
            status_code=500
        )


@security_bp.route('/security-blacklist-remove', methods=['POST'])
@require_api_key
@validate_required_fields('ip')
@handle_errors(tag="SECURITY-BLACKLIST-REMOVE")
def security_blacklist_remove():
    """
    Remove um IP da blacklist permanente.

    Body JSON:
    {
        "ip": "192.168.1.100"
    }

    Exemplo:
    POST https://seu-backend.onrender.com/admin/security-blacklist-remove
    Header: x-api-key: sua_chave_secreta
    Body: {"ip": "172.19.0.6"}
    """
    from app.middleware.security import remove_from_blacklist

    data = request.get_json()
    ip = data['ip']  # Já validado por @validate_required_fields

    success = remove_from_blacklist(ip)

    if success:
        return ApiResponse.success(
            f"IP {ip} removido da blacklist permanente",
            ip=ip
        )
    else:
        return ApiResponse.error(
            "Falha ao remover IP da blacklist (Redis indisponível ou IP não estava na blacklist)",
            status_code=500
        )
