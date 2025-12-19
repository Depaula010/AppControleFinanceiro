# app/routes/webhooks/whatsapp_router.py
"""
Rota principal do WhatsApp webhook com sistema de Intent Routing.

Esta rota recebe mensagens do WhatsApp via Twilio, classifica o intent
usando Gemini AI, e roteia para o handler apropriado usando o Factory Pattern.

Fluxo:
1. Webhook recebe mensagem do WhatsApp (via Twilio)
2. Valida HMAC signature para segurança
3. Identifica usuário pelo número WhatsApp
4. Classifica intent da mensagem usando Gemini AI
5. Roteia para intent handler apropriado
6. Processa e retorna resposta formatada
7. Envia resposta via WhatsApp

Substituindo:
- handle_whatsapp_webhook() do webhooks.py original (linhas 437-2533)
"""

from flask import request, jsonify
from sqlalchemy import text
import logging

from app import db_engine
from app.services import gemini_service, whatsapp_service
from app.services.transaction_confirmation_service import TransactionConfirmationService

# Utilitários Fase A
from app.shared.decorators import handle_errors

from . import webhooks_bp
from .base import require_hmac_validation
from .intents import route_intent

logger = logging.getLogger(__name__)


# =============================================================================
# WHATSAPP WEBHOOK - MAIN ROUTE
# =============================================================================

@webhooks_bp.route('/whatsapp', methods=['POST'])
@handle_errors(tag="WHATSAPP")
@require_hmac_validation(header_name='X-Twilio-Signature')
def handle_whatsapp_webhook():
    """
    Webhook principal do WhatsApp com Intent Routing.

    Recebe mensagens do WhatsApp via Twilio, classifica intent usando Gemini AI,
    e roteia para o handler apropriado.

    Fase A: Refatorado com decorators (economia: ~6 linhas)
    - @handle_errors: Tratamento de exceções automático
    - @require_hmac_validation: Validação HMAC do Twilio (já existente)

    Segurança:
        - Validação de HMAC signature via @require_hmac_validation
        - Autenticação de usuário via número WhatsApp

    Request Body (Form Data - Twilio format):
        - From: Número WhatsApp do remetente (+5511999999999)
        - Body: Texto da mensagem

    Response:
        - 200 OK: Mensagem processada e resposta enviada
        - 400 Bad Request: Dados inválidos
        - 401 Unauthorized: Usuário não encontrado ou não autorizado
        - 500 Internal Server Error: Erro no processamento

    Exemplo de mensagem do usuário:
        "quanto tenho no nubank?"
        → Intent classificado: "Consulta Saldo"
        → Handler: ConsultaSaldoIntent
        → Resposta: "💰 Saldo Nubank: R$ 1.234,56"
    """
    # 1. Extrair dados da mensagem
    from_number = request.form.get('From', '').replace('whatsapp:', '')
    message_body = request.form.get('Body', '').strip()

    logger.info(
        f"[WHATSAPP] Mensagem recebida de {from_number}: '{message_body[:50]}...'"
    )

    # Validação básica
    if not from_number or not message_body:
        logger.warning("[WHATSAPP] Mensagem sem número ou corpo")
        return jsonify({"status": "erro", "mensagem": "Dados inválidos"}), 400

    # 2. Identificar usuário pelo número WhatsApp
    with db_engine.connect() as conn:
        sql_user = text("""
            SELECT id, nome_usuario
            FROM Usuarios
            WHERE numero_whatsapp = :numero AND ativo = TRUE
        """)
        user_row = conn.execute(sql_user, {"numero": from_number}).fetchone()

        if not user_row:
            logger.warning(
                f"[WHATSAPP] Usuário não encontrado para número {from_number}"
            )
            whatsapp_service.send_message(
                to_number=from_number,
                message=(
                    "❌ Número não cadastrado.\n\n"
                    "Por favor, cadastre-se primeiro no sistema ou "
                    "atualize seu número WhatsApp nas configurações."
                )
            )
            return jsonify({"status": "usuario_nao_encontrado"}), 401

        usuario_id, nome_usuario = user_row
        logger.info(
            f"[WHATSAPP] Usuário identificado: {nome_usuario} (ID: {usuario_id})"
        )

    # 3. Verificar se é confirmação de transação pendente
    confirmation_service = TransactionConfirmationService()

    if message_body.lower() in ['confirmar', 'sim', 's', 'ok']:
        # Tentar confirmar transação pendente
        result = confirmation_service.confirm_pending_transaction(usuario_id)

        if result['success']:
            whatsapp_service.send_message(
                to_number=from_number,
                message=result['message']
            )
            return jsonify({"status": "sucesso", "acao": "confirmacao"}), 200

    elif message_body.lower() in ['cancelar', 'nao', 'n']:
        # Tentar cancelar transação pendente
        result = confirmation_service.cancel_pending_transaction(usuario_id)

        if result['success']:
            whatsapp_service.send_message(
                to_number=from_number,
                message=result['message']
            )
            return jsonify({"status": "sucesso", "acao": "cancelamento"}), 200

    # 4. Classificar intent usando Gemini AI
    try:
        intent_name = gemini_service.classify_intent(message_body, usuario_id)
        logger.info(
            f"[WHATSAPP] Intent classificado: '{intent_name}' "
            f"para mensagem '{message_body[:30]}...'"
        )
    except Exception as e:
        logger.error(f"[WHATSAPP] Erro ao classificar intent: {e}", exc_info=True)
        intent_name = None

    # Se classificação falhar, enviar mensagem padrão
    if not intent_name:
        logger.warning("[WHATSAPP] Falha na classificação de intent")
        whatsapp_service.send_message(
            to_number=from_number,
            message=(
                "❓ Desculpe, não entendi sua mensagem.\n\n"
                "Você pode tentar:\n"
                "• Consultar seu saldo\n"
                "• Registrar uma despesa ou renda\n"
                "• Ver sua agenda\n"
                "• Configurar notificações\n\n"
                "Exemplo: 'quanto tenho no nubank?'"
            )
        )
        return jsonify({"status": "intent_nao_classificado"}), 200

    # 5. Rotear para intent handler apropriado
    with db_engine.connect() as conn:
        result = route_intent(
            intent_name=intent_name,
            usuario_id=usuario_id,
            mensagem=message_body,
            conn=conn,
            numero_whatsapp=from_number
        )

    # 6. Processar resultado e enviar resposta
    if result['success']:
        response_message = result['message']
        logger.info(
            f"[WHATSAPP] Intent '{intent_name}' processado com sucesso"
        )
    else:
        response_message = result['message']
        logger.warning(
            f"[WHATSAPP] Intent '{intent_name}' falhou: {response_message[:50]}"
        )

    # 7. Enviar resposta via WhatsApp
    whatsapp_service.send_message(
        to_number=from_number,
        message=response_message
    )

    logger.info(f"[WHATSAPP] Resposta enviada para {from_number}")

    return jsonify({
        "status": "sucesso",
        "intent": intent_name,
        "usuario_id": usuario_id
    }), 200


# =============================================================================
# WEBHOOK VERIFICATION (Twilio)
# =============================================================================

@webhooks_bp.route('/whatsapp', methods=['GET'])
def verify_whatsapp_webhook():
    """
    Endpoint de verificação do webhook do Twilio/WhatsApp.

    Twilio pode fazer requisições GET para verificar que o webhook está ativo.

    Returns:
        200 OK: Webhook ativo e funcionando
    """
    logger.info("[WHATSAPP] Verificação de webhook recebida")
    return jsonify({
        "status": "ativo",
        "mensagem": "WhatsApp webhook ativo"
    }), 200


# =============================================================================
# STATUS ENDPOINT
# =============================================================================

@webhooks_bp.route('/whatsapp/status', methods=['GET'])
def whatsapp_status():
    """
    Endpoint para verificar status do sistema de intents.

    Útil para monitoramento e debugging.

    Returns:
        JSON com estatísticas do sistema:
        - Número de intents registrados
        - Lista de intents disponíveis
        - Status dos serviços
    """
    from .intents import list_registered_intents

    registered_intents = list_registered_intents()

    return jsonify({
        "status": "ativo",
        "intents_registrados": len(registered_intents),
        "intents_disponiveis": registered_intents,
        "servicos": {
            "gemini_service": "ativo",
            "whatsapp_service": "ativo",
            "db_engine": "ativo"
        }
    }), 200


__all__ = [
    'handle_whatsapp_webhook',
    'verify_whatsapp_webhook',
    'whatsapp_status',
]
