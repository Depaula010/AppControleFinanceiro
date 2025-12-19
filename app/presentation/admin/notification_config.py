"""
Módulo de configuração de notificações.

Rotas para visualizar e configurar notificações de usuários.
"""

from flask import Blueprint, request
from app.shared.decorators import handle_errors, validate_required_fields
from app.shared.responses import ApiResponse

# Blueprint para notification config
notification_config_bp = Blueprint('admin_notification_config', __name__)


@notification_config_bp.route('/get-notification-config/<int:usuario_id>', methods=['GET'])
@handle_errors(tag="GET-NOTIFICATION-CONFIG")
def get_notification_config(usuario_id):
    """
    Endpoint para visualizar configurações de notificação de um usuário.

    Exemplo:
    GET http://212.47.65.37:8000/admin/get-notification-config/1
    """
    from app.services.notification_config_service import NotificationConfigService

    config = NotificationConfigService.get_or_create_config(usuario_id)

    # Converter objetos time para string para JSON
    config_json = {
        'resumo_matinal_ativo': config['resumo_matinal_ativo'],
        'resumo_matinal_hora': config['resumo_matinal_hora'].strftime('%H:%M'),
        'alertas_financeiros_ativos': config['alertas_financeiros_ativos']
    }

    return ApiResponse.success(
        "Configurações de notificação obtidas com sucesso",
        usuario_id=usuario_id,
        configuracoes=config_json
    )


@notification_config_bp.route('/config-alertas-financeiros', methods=['POST'])
@validate_required_fields('usuario_id')
@handle_errors(tag="CONFIG-ALERTAS-FINANCEIROS")
def config_alertas_financeiros():
    """
    Configura alertas financeiros para um usuário.

    Body JSON:
    {
        "usuario_id": 1,
        "ativo": true/false
    }

    Exemplo:
    POST http://212.47.65.37:8000/admin/config-alertas-financeiros
    Body: {"usuario_id": 1, "ativo": true}
    """
    from app.services.notification_config_service import NotificationConfigService

    data = request.get_json()
    usuario_id = data['usuario_id']  # Já validado
    ativo = data.get('ativo')

    if ativo is None:
        return ApiResponse.bad_request("Campo 'ativo' é obrigatório (true ou false)")

    # Atualizar configuração
    sucesso, mensagem, config = NotificationConfigService.update_alertas_financeiros_config(
        usuario_id, ativo
    )

    if sucesso:
        return ApiResponse.success(mensagem, configuracao=config)
    else:
        return ApiResponse.error(mensagem, status_code=500)
