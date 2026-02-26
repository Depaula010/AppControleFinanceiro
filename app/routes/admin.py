# app/routes/admin.py
"""
Módulo de rotas administrativas (REFATORADO - Fase B.1).

Este arquivo agora importa o blueprint modularizado de app/presentation/admin/
e adiciona apenas rotas legadas/especiais que não se encaixam nos módulos.

HISTÓRICO:
- Antes: 1.792 linhas, 31 rotas em arquivo monolítico
- Depois: Refatorado em 7 módulos especializados (30 rotas)
- Permanece aqui: 1 rota especial (/run-motor-agendamentos)
"""

from flask import jsonify, request

# Importar blueprint modularizado
from app.presentation.admin import admin_bp

# Imports necessários para rotas legadas
from app.jobs.schedule_processor import processar_agendamentos
from app.config import API_SECRET_KEY
from app.middleware.security import remove_temp_block, remove_from_blacklist


# ============================================================================
# ROTAS LEGADAS / ESPECIAIS
# ============================================================================
# Rotas que não se encaixam bem nos módulos especializados permanecem aqui.


@admin_bp.route('/run-motor-agendamentos', methods=['POST'])
def run_motor_agendamentos():
    """
    Rota secreta que o Bot chama para rodar os agendamentos.

    Esta rota permanece no arquivo original pois é chamada pelo bot
    (sistema externo) e processa agendamentos através do motor_agendamentos.py.

    ⚠️ CRÍTICO: Esta rota é chamada pelo bot via webhook.

    Exemplo:
    POST /admin/run-motor-agendamentos
    Header: x-api-key: SUA_SECRET_KEY
    """
    secret_key_recebida = request.headers.get('x-api-key')
    if secret_key_recebida != API_SECRET_KEY:
        print(f"[MOTOR] Acesso negado à rota /run-motor-agendamentos. Chave errada.")
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    try:
        print("[MOTOR] Rota /run-motor-agendamentos chamada com sucesso! Iniciando processamento...")
        processar_agendamentos()  # Chama a função importada
        return jsonify({"status": "sucesso", "mensagem": "Agendamentos processados."}), 200
    except Exception as e:
        print(f"[MOTOR] ERRO CRÍTICO ao rodar /run-motor-agendamentos: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500


@admin_bp.route('/security-unblock-ip', methods=['POST'])
def security_unblock_ip():
    """
    POST /admin/security-unblock-ip
    Header: x-api-key: API_SECRET_KEY
    Body:   { "ip": "X.X.X.X" }

    Remove bloqueio temporário de um IP (falsos positivos / desenvolvimento).
    """
    secret_key_recebida = request.headers.get('x-api-key')
    if secret_key_recebida != API_SECRET_KEY:
        return jsonify({"status": "erro", "mensagem": "Não autorizado"}), 401

    body = request.get_json(silent=True) or {}
    ip = body.get('ip', '').strip()

    if not ip:
        return jsonify({"status": "erro", "mensagem": "Campo 'ip' obrigatório"}), 400

    remove_temp_block(ip)
    remove_from_blacklist(ip)

    return jsonify({"status": "sucesso", "mensagem": f"IP {ip} desbloqueado"}), 200


# ============================================================================
# EXPORTAÇÃO
# ============================================================================
# Re-exportar o blueprint para manter compatibilidade com app.py

__all__ = ['admin_bp']
