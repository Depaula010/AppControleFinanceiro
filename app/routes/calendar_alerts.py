# app/routes/calendar_alerts.py
"""
Rotas para configuração e gerenciamento de alertas de tarefas do Google Calendar
"""

from flask import Blueprint, jsonify, request
from app.services.calendar_alert_config_service import CalendarAlertConfigService
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService

calendar_alerts_bp = Blueprint('calendar_alerts', __name__, url_prefix='/calendar-alerts')


@calendar_alerts_bp.route('/config/<int:usuario_id>', methods=['GET'])
def get_config(usuario_id):
    """
    Obtém configuração de alertas de tarefas do usuário.

    Returns:
        JSON com configuração atual
    """
    try:
        config = CalendarAlertConfigService.get_or_create_config(usuario_id)

        return jsonify({
            "status": "sucesso",
            "config": config
        }), 200

    except Exception as e:
        print(f"[CALENDAR-ALERTS-API] ❌ Erro ao obter config: {e}")
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@calendar_alerts_bp.route('/config/<int:usuario_id>', methods=['POST'])
def update_config(usuario_id):
    """
    Atualiza configuração de alertas de tarefas.

    Body JSON:
    {
        "ativo": true | false,
        "minutos_antes": 1-60 (opcional)
    }

    Exemplo para ativar alertas 5 minutos antes:
    {
        "ativo": true,
        "minutos_antes": 5
    }

    Exemplo para desativar alertas:
    {
        "ativo": false
    }
    """
    try:
        data = request.get_json()

        ativo = data.get('ativo')
        minutos_antes = data.get('minutos_antes')

        # Validar que pelo menos um campo foi fornecido
        if ativo is None and minutos_antes is None:
            return jsonify({
                "status": "erro",
                "mensagem": "Nenhum parâmetro fornecido. Use 'ativo' e/ou 'minutos_antes'"
            }), 400

        # Verificar se usuário tem Google Calendar conectado (apenas se ativando)
        if ativo:
            if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
                return jsonify({
                    "status": "erro",
                    "mensagem": "Você precisa conectar o Google Calendar primeiro para ativar alertas de tarefas"
                }), 400

        # Atualizar configuração
        sucesso, mensagem, config = CalendarAlertConfigService.update_alertas_tarefas_config(
            usuario_id=usuario_id,
            ativo=ativo,
            minutos_antes=minutos_antes
        )

        if not sucesso:
            return jsonify({
                "status": "erro",
                "mensagem": mensagem
            }), 400

        return jsonify({
            "status": "sucesso",
            "mensagem": mensagem,
            "config": config
        }), 200

    except Exception as e:
        print(f"[CALENDAR-ALERTS-API] ❌ Erro ao atualizar config: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@calendar_alerts_bp.route('/ativar/<int:usuario_id>', methods=['POST'])
def ativar_alertas(usuario_id):
    """
    Atalho para ativar alertas de tarefas (mantém minutos_antes atual).

    Body JSON (opcional):
    {
        "minutos_antes": 1-60
    }
    """
    try:
        # Verificar se usuário tem Google Calendar conectado
        if not GoogleCalendarOAuthService.is_user_connected(usuario_id):
            return jsonify({
                "status": "erro",
                "mensagem": "Você precisa conectar o Google Calendar primeiro para ativar alertas de tarefas"
            }), 400

        data = request.get_json() or {}
        minutos_antes = data.get('minutos_antes')

        sucesso, mensagem, config = CalendarAlertConfigService.update_alertas_tarefas_config(
            usuario_id=usuario_id,
            ativo=True,
            minutos_antes=minutos_antes
        )

        if not sucesso:
            return jsonify({
                "status": "erro",
                "mensagem": mensagem
            }), 400

        return jsonify({
            "status": "sucesso",
            "mensagem": mensagem,
            "config": config
        }), 200

    except Exception as e:
        print(f"[CALENDAR-ALERTS-API] ❌ Erro ao ativar alertas: {e}")
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@calendar_alerts_bp.route('/desativar/<int:usuario_id>', methods=['POST'])
def desativar_alertas(usuario_id):
    """
    Atalho para desativar alertas de tarefas.
    """
    try:
        sucesso, mensagem, config = CalendarAlertConfigService.update_alertas_tarefas_config(
            usuario_id=usuario_id,
            ativo=False
        )

        if not sucesso:
            return jsonify({
                "status": "erro",
                "mensagem": mensagem
            }), 400

        return jsonify({
            "status": "sucesso",
            "mensagem": mensagem,
            "config": config
        }), 200

    except Exception as e:
        print(f"[CALENDAR-ALERTS-API] ❌ Erro ao desativar alertas: {e}")
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500
