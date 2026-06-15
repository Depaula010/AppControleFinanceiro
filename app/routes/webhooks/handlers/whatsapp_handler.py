# app/routes/webhooks/handlers/whatsapp_handler.py
"""
WhatsAppHandler - Processa mensagens do bot WhatsApp (Baileys).

Este handler contem toda a logica de processamento de mensagens WhatsApp,
incluindo autenticacao, classificacao de intents e execucao de acoes.

MIGRADO de webhooks/logic.py para centralizacao e independencia.
"""

from typing import Tuple, Any
from flask import jsonify, request
from datetime import date, timedelta, datetime
from zoneinfo import ZoneInfo
from sqlalchemy import text
import re
import traceback

from app import db_engine, gemini_model
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL, WEBHOOK_SIGNATURE_KEY
from app.utils import (
    formatar_moeda,
    ensure_db_connection,
    verify_hmac_signature,
    compare_keys_safe,
    sanitize_for_log,
    sanitize_input
)

from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service
from app.services import user_service
from app.services.period_query_service import PeriodQueryService
from app.services.fixed_bills_service import FixedBillsService
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.services.nightly_checkin_service import NightlyCheckinService
from app.services.redis_service import redis_service
from app.services.transaction_feedback_service import gerar_feedback_transacao
from app.services.finance.budget_validation_service import validate_budget
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services.calendar_query_service import CalendarQueryService
from app.services.calendar_management_service import CalendarManagementService
from app.services.notification_config_service import NotificationConfigService
from app.services.event_confirmation_service import EventConfirmationService


class WhatsAppHandler:
    """Handler para webhook do WhatsApp - Logica completa."""

    def handle(self) -> Tuple[Any, int]:
        """
        Processa mensagem do WhatsApp.
        Contem toda a logica de negocio.
        """
        return self._handle_whatsapp_webhook()

    def _handle_whatsapp_webhook(self) -> Tuple[Any, int]:
        """Webhook WhatsApp com CONFIRMACAO e suporte a cadastro"""

        try:
            ensure_db_connection()
        except Exception as e:
            return jsonify({
                "status": "erro",
                "resposta": "Banco de dados temporariamente indisponível"
            }), 503

        if not db_engine or not gemini_model:
            return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503

        # 1. Validar assinatura HMAC (primeira camada de seguranca)
        webhook_signature = request.headers.get('X-Webhook-Signature', '').strip()
        if webhook_signature:
            payload = request.get_data()
            if not verify_hmac_signature(payload, webhook_signature, WEBHOOK_SIGNATURE_KEY):
                print("[SECURITY] ⚠️  Assinatura HMAC inválida no webhook WhatsApp")
                return jsonify({"status": "erro", "resposta": "Assinatura inválida"}), 401
        else:
            print("[SECURITY] ⚠️  Webhook sem assinatura HMAC (modo compatibilidade)")

        # 2. Autenticar API key (segunda camada de seguranca)
        secret_key_recebida = request.headers.get('x-api-key', '').strip()
        if not secret_key_recebida or not compare_keys_safe(secret_key_recebida, API_SECRET_KEY):
            # --- DEBUG LOGGING START ---
            print(f"[AUTH DEBUG] Recebida: '{secret_key_recebida}'")
            print(f"[AUTH DEBUG] Esperada: '{API_SECRET_KEY[:5]}...{API_SECRET_KEY[-5:]}' (Len: {len(API_SECRET_KEY)})")
            # --- DEBUG LOGGING END ---
            return jsonify({"status": "erro", "resposta": "Não autorizado"}), 401

        try:
            data = request.json
            texto_msg_raw = data.get('texto')
            numero_remetente = data.get('numero_remetente')

            if not texto_msg_raw or not numero_remetente:
                return jsonify({"status": "erro", "mensagem": "Dados faltando"}), 400

            # Sanitizar mensagem (permite caracteres especiais para nomes de lugares, etc)
            texto_msg = sanitize_input(texto_msg_raw, max_length=1000, allow_special_chars=True)
            numero_limpo = sanitize_input(numero_remetente.split('@')[0], max_length=20)

            # Limitar tamanho da mensagem no log
            texto_log = texto_msg[:100] + "..." if len(texto_msg) > 100 else texto_msg
            print(f"[WHATSAPP] Mensagem de {numero_limpo}: {texto_log}")

            # 2. Verificar cadastro
            user_info = user_service.check_user_exists(numero_limpo)
            registration_state = user_service.get_registration_state(numero_limpo)

            # 2.1. Processo de cadastro
            if registration_state:
                if texto_msg.lower().strip() in ['cancelar', 'sair']:
                    user_service.cancel_registration(numero_limpo)
                    return jsonify({"status": "sucesso", "resposta": "Cadastro cancelado."}), 200

                resposta, completo = user_service.process_registration_step(numero_limpo, texto_msg)
                return jsonify({"status": "sucesso", "resposta": resposta}), 200

            # 2.2. Usuario nao cadastrado - Redirecionar para cadastro web
            if not user_info:
                msg = (
                    "Olá! 👋 Parece que você ainda não tem cadastro no Meu Secretário.\n\n"
                    "Para começar a usar, crie sua conta em nosso site:\n"
                    "https://app.meusecretario.com/register\n\n"
                    "Após criar sua conta, volte aqui e eu estarei pronto para ajudar! 😊"
                )
                return jsonify({"status": "sucesso", "resposta": msg}), 200

            usuario_id = user_info[0]

            # 3. NOVO: Verificar confirmacoes/cancelamentos de EVENTOS
            msg_lower = texto_msg.strip().lower()

            # Verificar se quer calcular tempo de deslocamento
            wants_travel_time = 'calcular' in msg_lower and 'rota' in msg_lower

            palavras_confirmacao_evento = ['sim', 'confirmar', 'confirma', 'ok', 's']
            palavras_cancelamento_evento = ['não', 'nao', 'cancelar', 'cancela', 'desistir', 'n']

            # Verificar se e resposta simples de confirmacao/cancelamento (mas nao "sim, calcular")
            if not wants_travel_time and (msg_lower in palavras_confirmacao_evento or msg_lower in palavras_cancelamento_evento):
                # Buscar evento pendente
                event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)

                print(f"[EVENT-CONFIRM] Detectado '{msg_lower}' | Event ID: {event_id} | Has data: {event_data is not None}")

                if event_data:
                    if msg_lower in palavras_confirmacao_evento:
                        # Confirmar e criar evento (com ou sem calculo de tempo previo)
                        print(f"[EVENT-CONFIRM] Criando evento no Google Calendar: {event_data.get('titulo')}")
                        sucesso, mensagem, google_event_id = EventConfirmationService.confirm_and_create_event(numero_limpo, event_id)

                        print(f"[EVENT-CONFIRM] Resultado: sucesso={sucesso}, google_event_id={google_event_id}")

                        if sucesso:
                            titulo = event_data['titulo']
                            resposta_para_usuario = f"✅ *Evento Criado!*\n\n"
                            resposta_para_usuario += f"📝 {titulo}\n"
                            resposta_para_usuario += f"{mensagem}"
                        else:
                            resposta_para_usuario = f"❌ {mensagem}"

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    else:  # Cancelamento
                        sucesso, mensagem = EventConfirmationService.cancel_pending_event(numero_limpo, event_id)
                        return jsonify({"status": "sucesso", "resposta": mensagem}), 200
                else:
                    print(f"[EVENT-CONFIRM] Nenhum evento pendente encontrado para '{msg_lower}'")

            # 3.1 Verificar se quer calcular tempo de deslocamento
            if wants_travel_time:
                from app.services.user_address_service import UserAddressService

                # Buscar evento pendente
                event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)

                if not event_data:
                    resposta_para_usuario = "❌ Não encontrei nenhum evento pendente de confirmação."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # Verificar se evento tem localizacao
                if not event_data.get('localizacao'):
                    resposta_para_usuario = "❌ Este evento não tem localização definida, não posso calcular tempo de deslocamento."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # Buscar enderecos cadastrados
                usuario_id = event_data.get('usuario_id')
                user_addresses = UserAddressService.get_user_addresses(usuario_id)

                if not user_addresses or len(user_addresses) == 0:
                    resposta_para_usuario = (
                        "❌ Você não tem endereços cadastrados.\n\n"
                        "Configure um endereço primeiro:\n"
                        "Exemplo: _'Configurar endereço casa: Av Paulista 1000, São Paulo-SP'_"
                    )
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                elif len(user_addresses) == 1:
                    # So 1 endereco: usar automaticamente
                    from app.services.travel_time_service import TravelTimeService

                    origem = user_addresses[0]
                    label_nome = UserAddressService.LABEL_NAMES.get(origem['label'], origem['label'].capitalize())
                    emoji = UserAddressService.LABEL_EMOJIS.get(origem['label'], '📍')

                    # Geocodificar destino
                    dest_lat, dest_lon, dest_formatado = TravelTimeService.geocode_address(event_data['localizacao'])

                    if not dest_lat or not dest_lon:
                        resposta_para_usuario = (
                            f"❌ Não consegui localizar o destino:\n"
                            f"'{event_data['localizacao']}'\n\n"
                            f"Verifique se o endereço está completo e tente novamente."
                        )
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Calcular tempo de viagem
                    origem_lat = origem['lat']
                    origem_lon = origem['lon']

                    travel_info = TravelTimeService.calculate_travel_time(
                        origem_lat, origem_lon,
                        dest_lat, dest_lon
                    )

                    if not travel_info:
                        resposta_para_usuario = (
                            f"❌ Não consegui calcular a rota.\n\n"
                            f"Tente novamente mais tarde ou confirme o evento sem cálculo de tempo."
                        )
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Sucesso! Mostrar resultado
                    duracao = travel_info['duration_minutes']
                    distancia = travel_info['distance_km']

                    resposta_para_usuario = (
                        f"✅ *Rota Calculada!*\n\n"
                        f"📍 Origem: {emoji} {label_nome}\n"
                        f"📍 Destino: {dest_formatado or event_data['localizacao']}\n\n"
                        f"⏱️ *Tempo estimado:* {duracao} minutos\n"
                        f"📏 *Distância:* {distancia} km\n\n"
                        f"💡 Responda *'sim'* para confirmar o evento\n"
                        f"ou *'não'* para cancelar"
                    )

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                else:
                    # 2+ enderecos: perguntar qual usar
                    msg = "🚗 *Qual endereço usar como origem?*\n\n"

                    for addr in user_addresses:
                        label = addr['label']
                        endereco = addr['endereco']
                        emoji = UserAddressService.LABEL_EMOJIS.get(label, '📍')
                        label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())

                        msg += f"{emoji} *{label_nome}*: {endereco}\n"

                    msg += f"\n💬 Responda com: _'casa'_, _'trabalho'_ ou _'outro'_"

                    return jsonify({"status": "sucesso", "resposta": msg}), 200

            # 3.0. Verificar se esta respondendo a um CHECK-IN NOTURNO (PRIORIDADE 2)
            checkin_active_key = f"nightly_checkin_active:{numero_limpo}"
            checkin_active = redis_service.get(checkin_active_key)

            if checkin_active:
                checkin_id = checkin_active  # O valor e o checkin_id
                print(f"[CHECKIN-RESPONSE] Check-in ativo detectado: {checkin_id}")

                # Verificar Escape Hatch (palavras-chave que quebram o check-in)
                if any(kw in texto_msg.lower() for kw in NightlyCheckinService.ESCAPE_KEYWORDS):
                    print(f"[CHECKIN-ESCAPE] Escape hatch detectado: '{texto_msg}'")
                    redis_service.delete(checkin_active_key)
                    # Continuar para classificacao normal de intent
                    # Nao retorna aqui - deixa cair para o processamento normal
                else:
                    # Processar resposta de check-in
                    print(f"[CHECKIN-RESPONSE] Processando resposta: '{texto_msg}'")
                    status, resposta = NightlyCheckinService.process_response(
                        numero_limpo, texto_msg, checkin_id
                    )

                    # A flag ja foi removida dentro do process_response
                    return jsonify({"status": "sucesso", "resposta": resposta}), 200

            # 3.1. Verificar se esta respondendo a uma confirmacao de TRANSACAO
            # Tentar identificar transaction_id na mensagem ou buscar ultima pendente
            # (Simplificado: buscar por padrao "ID: XXXX" na ultima msg ou assumir contexto)

            # Por simplicidade, vamos checar se existe alguma pendente recente
            # Em producao, voce pode enviar o transaction_id na mensagem ou usar contexto

            # Verificar se mensagem parece uma resposta de confirmacao de TRANSACAO
            # IMPORTANTE: So processar se NAO for confirmacao de evento (eventos tem prioridade)
            msg_upper = texto_msg.strip().upper()

            # Bloquear transaction handler APENAS para palavras EXCLUSIVAS de evento
            # Palavras como "ok", "cancelar" sao ambiguas - devem verificar contexto (TX pendente)
            palavras_exclusivas_evento = ['sim', 's', 'não', 'nao', 'n', 'desistir']
            is_exclusive_event_word = msg_lower in palavras_exclusivas_evento

            if not is_exclusive_event_word and (any(word in msg_upper for word in ['CONFIRMAR', 'OK', 'TROCAR', 'CANCELAR']) or msg_upper.isdigit()):
                # Provavel resposta de confirmacao de TRANSACAO
                print(f"[TX-CONFIRM-CHECK] Detectada possível confirmação de transação: '{texto_msg}'")

                # Buscar ultima transacao pendente
                last_tx_key = f"last_pending:{numero_limpo}"
                last_transaction_id = redis_service.get(last_tx_key)

                print(f"[TX-CONFIRM-CHECK] Buscando {last_tx_key} -> {last_transaction_id}")

                if last_transaction_id:
                    status, mensagem, dados = TransactionConfirmationService.process_confirmation_response(
                        numero_limpo,
                        texto_msg,
                        last_transaction_id
                    )

                    if status == 'saved':
                        # Salvar no banco
                        with db_engine.connect() as conn:
                            conn.begin()

                            # Se fatura_id e 'PENDING', criar/buscar fatura agora
                            fatura_id_final = dados.get('fatura_id')
                            if fatura_id_final == 'PENDING':
                                conta_id = dados['conta_id']
                                usuario_id_tx = dados['usuario_id']
                                data_tx = date.fromisoformat(dados['data_transacao'])

                                # Garantir que existe fatura para o periodo atual
                                finance_service.ensure_current_invoice_exists(conn, usuario_id_tx, conta_id)

                                fatura_id_final = finance_service.get_or_create_fatura(conn, conta_id, data_tx, usuario_id_tx)
                                print(f"[CONFIRM-SAVE] Fatura criada/encontrada: {fatura_id_final}")

                            # Usar descricao_final se disponivel, senao usar descricao
                            descricao_para_salvar = dados.get('descricao_final', dados.get('descricao'))

                            # Criar transacao e capturar o ID
                            transacao_id = finance_service.create_transaction(
                                conn,
                                dados['usuario_id'],
                                dados['conta_id'],
                                dados['categoria_id'],
                                fatura_id_final,
                                descricao_para_salvar,
                                dados['valor_db'],
                                dados['tipo_transacao'],
                                date.fromisoformat(dados['data_transacao'])
                            )
                            print(f"[CONFIRM-SAVE] Transação criada com ID: {transacao_id}")

                            # Se for parcelado, criar agendamentos para as parcelas futuras
                            num_parcelas = dados.get('num_parcelas')
                            if num_parcelas and num_parcelas > 1:
                                valor_parcela = abs(dados['valor_db'])  # Valor positivo da parcela
                                data_primeira = date.fromisoformat(dados['data_transacao'])

                                agendamento_id = finance_service.create_parcelamento_agendamento(
                                    conn,
                                    dados['usuario_id'],
                                    dados['conta_id'],
                                    dados['categoria_id'],
                                    descricao_para_salvar,
                                    valor_parcela,
                                    num_parcelas,
                                    data_primeira
                                )
                                print(f"[CONFIRM-SAVE] Agendamento de parcelas criado: {agendamento_id}")

                            # Commit antes de gerar feedback (para garantir que dados estejam salvos)
                            conn.commit()

                            # NOVO: Gerar mensagem de feedback enriquecida
                            resposta = gerar_feedback_transacao(conn, transacao_id)

                        # Limpar last_pending
                        redis_service.delete(last_tx_key)

                        return jsonify({"status": "sucesso", "resposta": resposta}), 200

                    elif status == 'awaiting_category':
                        # Enviar lista de categorias
                        with db_engine.connect() as conn:
                            cats = finance_service.get_user_categories(conn, usuario_id, dados['tipo_transacao'])

                        msg_cats, cat_map = TransactionConfirmationService.format_category_selection_message(cats)

                        # Atualizar transacao com o mapa
                        dados['categoria_map'] = cat_map
                        tx_key = f"pending_tx:{numero_limpo}:{last_transaction_id}"
                        redis_service.set_with_ttl(tx_key, dados, 300)

                        return jsonify({"status": "sucesso", "resposta": msg_cats}), 200

                    elif status == 'cancelled':
                        redis_service.delete(last_tx_key)
                        return jsonify({"status": "sucesso", "resposta": mensagem}), 200

                    elif status == 'error':
                        return jsonify({"status": "sucesso", "resposta": mensagem}), 200
                else:
                    # Palavra-chave de confirmacao detectada, mas sem transacao pendente
                    print(f"[CONFIRM-CHECK] Palavra de confirmação detectada, mas nenhuma transação pendente encontrada")
                    print(f"[TX-CONFIRM-DEBUG] Número limpo: {numero_limpo}")
                    print(f"[TX-CONFIRM-DEBUG] Chave Redis: {last_tx_key}")
                    print(f"[TX-CONFIRM-DEBUG] Mensagem original: '{texto_msg}'")
                    print(f"[TX-CONFIRM-DEBUG] Mensagem upper: '{msg_upper}'")

                    # Se for palavra EXATA de confirmacao/cancelamento, dar feedback claro
                    # Isso evita que "ok" ou "cancelar" sejam classificados como outras intencoes
                    if msg_upper in ['OK', 'CONFIRMAR', 'SIM', 'CONFIRMA']:
                        return jsonify({
                            "status": "sucesso",
                            "resposta": "✅ Não encontrei nada pendente para confirmar.\n\nSe quer registrar algo, diga:\nExemplo: 'gastei 50 em comida'"
                        }), 200
                    elif msg_upper in ['CANCELAR', 'CANCELA', 'NÃO', 'NAO']:
                        return jsonify({
                            "status": "sucesso",
                            "resposta": "❌ Não encontrei nada pendente para cancelar.\n\nSe quer deletar um evento específico, diga qual:\nExemplo: 'Deletar academia de hoje'"
                        }), 200
                    # Se for "TROCAR" ou numero, continuar para classificacao normal

            # Safety check para "cancelar" sem contexto
            if msg_lower in ['cancelar', 'cancela'] and len(texto_msg.strip().split()) == 1:
                # Usuario enviou apenas "cancelar" - verificar se ha algo pendente
                has_pending_tx = redis_service.get(f"last_pending:{numero_limpo}") is not None
                event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)
                has_pending_event = event_data is not None

                if not has_pending_tx and not has_pending_event:
                    print(f"[SAFETY-CHECK] Usuário enviou 'cancelar' mas nada está pendente")
                    return jsonify({
                        "status": "sucesso",
                        "resposta": "❌ Não encontrei nada pendente para cancelar.\n\nSe quer deletar um evento específico, diga qual:\nExemplo: 'Deletar academia de hoje'"
                    }), 200

            # 4. Verificar PRIMEIRO se e pagamento de conta (antes de classificar intent)
            # Isso evita que o Gemini classifique errado e tente extrair valor inexistente
            if any(word in texto_msg.lower() for word in ['paguei', 'quitei', 'liquidei', 'saldei', 'zerei']):
                # Ir direto para o HANDLER 3 (processamento de pagamentos)
                data_hoje = date.today()
                with db_engine.connect() as conn:
                    conn.begin()

                    # PASSO 1: Extrair itens e valores (SEM classificar)
                    payment_data = gemini_service.extract_payment_items(texto_msg, usuario_id)
                    itens_lista = payment_data.get('itens', [])
                    valor_total = payment_data.get('valor_total')  # null ou float
                    trigger_word = payment_data.get('trigger_word', 'paguei')

                    if not itens_lista:
                        resposta_para_usuario = "🤔 Não consegui identificar o que você pagou. Pode reformular?"
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # PASSO 2: Verificar cada item no banco de contas fixas
                    contas_fixas_quitadas = []
                    despesas_criadas = []
                    itens_sem_valor = []  # Itens que nao achou no banco E nao tem valor

                    for item_desc in itens_lista:
                        # Tentar encontrar conta fixa correspondente
                        match = FixedBillsService.find_matching_bill(conn, usuario_id, item_desc)

                        if match:
                            # ENCONTROU CONTA FIXA → Quitar
                            agendamento_id, desc_original, valor_previsto, dia_venc, tipo_agend, categoria, conta_id_agendamento = match

                            # Determinar valor a usar (mesma logica para FIXO e LEMBRETE_VARIAVEL)
                            # Prioridade: valor informado pelo usuario > valor_previsto
                            if valor_total and len(itens_lista) == 1:
                                # UMA conta + valor informado → usar valor informado (ambos os tipos)
                                valor_pagar = valor_total
                            else:
                                # Multiplas contas OU sem valor → usar valor_previsto como fallback
                                valor_pagar = float(valor_previsto) if valor_previsto else None

                            if valor_pagar is None or valor_pagar == 0:
                                itens_sem_valor.append({
                                    'nome': desc_original,
                                    'tipo': 'conta_fixa'
                                })
                                continue

                            # Quitar a conta
                            try:
                                # **IMPORTANTE**: Passar conta_id_agendamento para debitar da conta correta
                                transaction_id = FixedBillsService.settle_fixed_bill(
                                    conn, usuario_id, agendamento_id, valor_pagar,
                                    date.today(),
                                    conta_pagamento_id=conta_id_agendamento,  # Debita da conta do agendamento
                                )

                                contas_fixas_quitadas.append({
                                    'descricao': desc_original,
                                    'valor': valor_pagar,
                                    'valor_previsto': float(valor_previsto) if valor_previsto else 0,
                                    'tipo_agendamento': tipo_agend,
                                    'categoria': categoria,
                                    'transaction_id': transaction_id
                                })

                            except Exception as e:
                                print(f"[PROCESSAR-PAGAMENTO] Erro ao quitar {desc_original}: {e}")
                                # Continua para proximos itens

                        else:
                            # NAO ENCONTROU CONTA FIXA → Criar despesa

                            # Determinar valor da despesa
                            if valor_total and len(itens_lista) == 1:
                                # UMA despesa + valor informado
                                valor_despesa = valor_total
                            elif valor_total and len(itens_lista) > 1:
                                # Multiplas despesas + valor total → precisa dividir
                                # Opcao: Pedir valor individual (por ora, ignora)
                                itens_sem_valor.append({
                                    'nome': item_desc,
                                    'tipo': 'despesa'
                                })
                                continue
                            else:
                                # Sem valor → nao pode criar despesa
                                itens_sem_valor.append({
                                    'nome': item_desc,
                                    'tipo': 'despesa'
                                })
                                continue

                            # Criar despesa avulsa
                            try:
                                # Categorizar
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Iniciando criação de despesa '{item_desc}' com valor {valor_despesa}")
                                cats_list = finance_service.get_user_categories(conn, usuario_id, 'Despesa')
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Categorias obtidas: {len(cats_list)} categorias")

                                id_outros = finance_service.get_fallback_category_id(conn, 'Despesa')
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Categoria fallback 'Outros' ID: {id_outros}")

                                if not id_outros:
                                    print(f"[PROCESSAR-PAGAMENTO] ERRO: Categoria fallback 'Outros' não encontrada no banco de dados!")
                                    itens_sem_valor.append({
                                        'nome': item_desc,
                                        'tipo': 'erro_categoria'
                                    })
                                    continue

                                id_categoria = gemini_service.categorize_transaction(
                                    cats_list, item_desc, 'Despesa', id_outros, usuario_id
                                )
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Categoria selecionada ID: {id_categoria}")

                                # Escolher conta usando funcao centralizada (fuzzy matching + conta padrao do usuario)
                                conta_id, conta_nome, conta_tipo, origem = finance_service.choose_account_for_transaction(
                                    conn, usuario_id, texto_msg, 'Despesa'
                                )
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Conta selecionada: {conta_nome} (ID: {conta_id}, Origem: {origem})")

                                if not conta_id:
                                    print(f"[PROCESSAR-PAGAMENTO] ERRO: Nenhuma conta encontrada para o usuário!")
                                    itens_sem_valor.append({
                                        'nome': item_desc,
                                        'tipo': 'erro_conta'
                                    })
                                    continue

                                # Criar transacao
                                valor_negativo = float(valor_despesa) * -1
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Criando transação - Valor: {valor_negativo}, Categoria: {id_categoria}, Conta: {conta_id}")

                                transaction_id = finance_service.create_transaction(
                                    conn, usuario_id, conta_id, id_categoria, None,
                                    item_desc, valor_negativo, 'Despesa', date.today()
                                )
                                print(f"[PROCESSAR-PAGAMENTO] DEBUG: Transação criada com ID: {transaction_id}")

                                # Buscar nome da categoria
                                categoria_nome = next((c['nome_sub'] for c in cats_list if c['id'] == id_categoria), 'Outros')

                                despesas_criadas.append({
                                    'descricao': item_desc,
                                    'valor': valor_despesa,
                                    'categoria': categoria_nome,
                                    'conta_nome': conta_nome,
                                    'transaction_id': transaction_id
                                })
                                print(f"[PROCESSAR-PAGAMENTO] ✅ Despesa '{item_desc}' criada com sucesso!")

                            except Exception as e:
                                print(f"[PROCESSAR-PAGAMENTO] ❌ ERRO ao criar despesa {item_desc}:")
                                print(f"[PROCESSAR-PAGAMENTO] Tipo do erro: {type(e).__name__}")
                                print(f"[PROCESSAR-PAGAMENTO] Mensagem: {str(e)}")
                                print(f"[PROCESSAR-PAGAMENTO] Traceback:")
                                traceback.print_exc()

                    conn.commit()

                    # PASSO 3: Formatar resposta unificada
                    if not contas_fixas_quitadas and not despesas_criadas and itens_sem_valor:
                        # Nenhuma acao realizada → pedir valores
                        if len(itens_sem_valor) == 1:
                            item = itens_sem_valor[0]
                            resposta_para_usuario = (
                                f"🤔 Para processar '{item['nome']}', preciso do valor.\n\n"
                                f"Exemplo: *{trigger_word} {item['nome']} 150*"
                            )
                        else:
                            nomes = "', '".join([i['nome'] for i in itens_sem_valor])
                            resposta_para_usuario = (
                                f"🤔 Para processar '{nomes}', preciso dos valores.\n\n"
                                f"Tente informar um item por vez com o valor."
                            )

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Montar resposta com tudo que foi processado
                    resposta_para_usuario = ""

                    # Contas fixas quitadas
                    if contas_fixas_quitadas:
                        if len(contas_fixas_quitadas) == 1:
                            c = contas_fixas_quitadas[0]
                            resposta_para_usuario += f"✅ *CONTA QUITADA* ✅\n\n"
                            resposta_para_usuario += f"📝 {c['descricao']}\n"
                            resposta_para_usuario += f"💰 {formatar_moeda(c['valor'])}\n"
                            resposta_para_usuario += f"📊 {c['categoria']}\n\n"
                            resposta_para_usuario += f"_Esta conta não será cobrada automaticamente este mês._\n"

                            # Alerta para VARIAVEL
                            if c['tipo_agendamento'] == 'LEMBRETE_VARIAVEL':
                                diferenca = abs(c['valor_previsto'] - c['valor'])
                                if diferenca > 1:
                                    resposta_para_usuario += (
                                        f"\n📊 *Lembrete Variável*\n"
                                        f"Valor previsto: {formatar_moeda(c['valor_previsto'])}\n"
                                        f"Valor pago: {formatar_moeda(c['valor'])}"
                                    )
                            else:
                                # Conta FIXA - mostrar diferenca se houver
                                diferenca = abs(c['valor_previsto'] - c['valor'])
                                if diferenca > 1:
                                    sinal = "+" if c['valor'] > c['valor_previsto'] else "-"
                                    resposta_para_usuario += (
                                        f"\n💡 Valor previsto era {formatar_moeda(c['valor_previsto'])} "
                                        f"({sinal}{formatar_moeda(diferenca)})"
                                    )

                        else:
                            # Multiplas contas
                            resposta_para_usuario += f"✅ *{len(contas_fixas_quitadas)} CONTAS QUITADAS* ✅\n\n"
                            total = 0
                            for idx, c in enumerate(contas_fixas_quitadas, 1):
                                resposta_para_usuario += f"{idx}. *{c['descricao']}*\n"
                                resposta_para_usuario += f"   💰 {formatar_moeda(c['valor'])}\n"
                                resposta_para_usuario += f"   📊 {c['categoria']}\n"

                                if c['tipo_agendamento'] == 'LEMBRETE_VARIAVEL':
                                    resposta_para_usuario += f"   ⚠️ Variável (previsto: {formatar_moeda(c['valor_previsto'])})\n"

                                resposta_para_usuario += "\n"
                                total += c['valor']

                            resposta_para_usuario += "━━━━━━━━━━━━━━━━━━━━\n"
                            resposta_para_usuario += f"💵 *Total: {formatar_moeda(total)}*\n\n"

                    # Despesas criadas
                    if despesas_criadas:
                        if contas_fixas_quitadas:
                            resposta_para_usuario += "\n━━━━━━━━━━━━━━━━━━━━\n\n"

                        if len(despesas_criadas) == 1:
                            d = despesas_criadas[0]
                            resposta_para_usuario += f"💸 *DESPESA REGISTRADA* 💸\n\n"
                            resposta_para_usuario += f"📝 {d['descricao']}\n"
                            resposta_para_usuario += f"💰 {formatar_moeda(d['valor'])}\n"
                            resposta_para_usuario += f"📊 {d['categoria']}\n"
                            resposta_para_usuario += f"💳 {d['conta_nome']}\n"

                        else:
                            # Multiplas despesas
                            resposta_para_usuario += f"💸 *{len(despesas_criadas)} DESPESAS REGISTRADAS* 💸\n\n"
                            total_despesas = 0
                            for idx, d in enumerate(despesas_criadas, 1):
                                resposta_para_usuario += f"{idx}. *{d['descricao']}*\n"
                                resposta_para_usuario += f"   💰 {formatar_moeda(d['valor'])}\n"
                                resposta_para_usuario += f"   📊 {d['categoria']}\n"
                                resposta_para_usuario += f"   💳 {d['conta_nome']}\n\n"
                                total_despesas += d['valor']

                            resposta_para_usuario += "━━━━━━━━━━━━━━━━━━━━\n"
                            resposta_para_usuario += f"💵 *Total: {formatar_moeda(total_despesas)}*\n"

                    # Se teve algumas acoes mas ainda tem itens pendentes
                    if itens_sem_valor and (contas_fixas_quitadas or despesas_criadas):
                        resposta_para_usuario += "\n\n⚠️ *Itens não processados* (faltou valor):\n"
                        for item in itens_sem_valor:
                            resposta_para_usuario += f"• {item['nome']}\n"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # 5. Classificar intencao (fluxo normal, se nao foi pagamento)
            intent = gemini_service.get_message_intent(texto_msg, usuario_id)
            data_hoje = date.today()

            with db_engine.connect() as conn:
                conn.begin()

                # ===== HANDLER 1: Renda com CONFIRMACAO =====
                if intent == 'Renda':
                    from app.routes.webhooks.intents import route_intent

                    try:
                        result = route_intent(
                            intent_name='Renda',
                            usuario_id=usuario_id,
                            mensagem=texto_msg,
                            conn=conn,
                            numero_whatsapp=numero_limpo
                        )

                        resposta_para_usuario = result["message"]
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    except Exception as e:
                        print(f"[RENDA-INTENT] Erro: {e}")
                        traceback.print_exc()
                        resposta_para_usuario = "❌ Erro ao processar renda."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # ===== HANDLER 2: Despesa com CONFIRMACAO =====
                elif intent == 'Despesa':
                    from app.routes.webhooks.intents import route_intent

                    try:
                        result = route_intent(
                            intent_name='Despesa',
                            usuario_id=usuario_id,
                            mensagem=texto_msg,
                            conn=conn,
                            numero_whatsapp=numero_limpo
                        )

                        resposta_para_usuario = result["message"]
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    except Exception as e:
                        print(f"[DESPESA-INTENT] Erro: {e}")
                        traceback.print_exc()
                        resposta_para_usuario = "❌ Erro ao processar despesa."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # ===== INTENCAO: Consulta Reserva =====
                elif intent == 'Consulta Reserva':
                    media_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)
                    resposta_para_usuario = "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
                    resposta_para_usuario += f"💰 Gasto mensal essencial: *{formatar_moeda(media_mensal)}*\n"
                    resposta_para_usuario += f"🎯 Reserva ideal ({meses} meses): *{formatar_moeda(reserva_ideal)}*\n\n"
                    resposta_para_usuario += "💡 _Digite *'detalhes da reserva'* para ver quais contas estão incluídas no cálculo_"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # ===== INTENCAO: Consulta Detalhes Reserva =====
                elif intent == 'Consulta Detalhes Reserva':
                    # Primeiro, buscar quantos meses o usuario configurou
                    media_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)

                    # Buscar todos os agendamentos incluidos na reserva com calculo dinamico
                    sql_detalhes = text("""
                        SELECT
                            a.descricao,
                            a.valor_previsto,
                            a.periodicidade,
                            s.nome_sub as categoria,
                            CASE
                                WHEN a.periodicidade = 'MENSAL' THEN a.valor_previsto * :meses
                                WHEN a.periodicidade = 'ANUAL' THEN a.valor_previsto * 1
                                WHEN a.periodicidade = 'SEMANAL' THEN a.valor_previsto * ROUND(:meses * 4.33)
                                WHEN a.periodicidade = 'QUINZENAL' THEN a.valor_previsto * (:meses * 2)
                                WHEN a.periodicidade = 'DIARIA' THEN a.valor_previsto * (:meses * 30)
                            END AS impacto_n_meses
                        FROM Agendamentos a
                        JOIN SubCategoria s ON a.subcategoria_id = s.id
                        WHERE a.usuario_id = :uid
                          AND a.ativo = TRUE
                          AND a.incluir_na_reserva = TRUE
                          AND (a.tipo_agendamento = 'FIXO' OR a.tipo_agendamento = 'LEMBRETE_VARIAVEL')
                        ORDER BY impacto_n_meses DESC
                    """)

                    agendamentos = conn.execute(sql_detalhes, {"uid": usuario_id, "meses": meses}).fetchall()

                    if not agendamentos:
                        resposta_para_usuario = "🆘 *Detalhes da Reserva de Emergência*\n\n"
                        resposta_para_usuario += "⚠️ Nenhum agendamento está incluído no cálculo da reserva.\n\n"
                        resposta_para_usuario += "💡 _Configure seus agendamentos fixos (água, luz, aluguel, etc.)_"
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    resposta_para_usuario = "🆘 *Detalhes da Reserva de Emergência*\n\n"
                    resposta_para_usuario += f"📊 *Resumo:*\n"
                    resposta_para_usuario += f"• Gasto mensal essencial: *{formatar_moeda(media_mensal)}*\n"
                    resposta_para_usuario += f"• Reserva {meses} meses: *{formatar_moeda(reserva_ideal)}*\n\n"

                    resposta_para_usuario += "📋 *Contas incluídas:*\n\n"

                    for agend in agendamentos:
                        descricao = agend.descricao
                        valor_previsto = float(agend.valor_previsto or 0)
                        periodicidade = agend.periodicidade
                        impacto = float(agend.impacto_n_meses or 0)

                        # Emoji por periodicidade
                        emoji_periodo = {
                            'MENSAL': '📅',
                            'ANUAL': '🗓️',
                            'SEMANAL': '📆',
                            'QUINZENAL': '📋',
                            'DIARIA': '⏰'
                        }.get(periodicidade, '📌')

                        resposta_para_usuario += f"{emoji_periodo} *{descricao}*\n"
                        resposta_para_usuario += f"   └ {formatar_moeda(valor_previsto)}/{periodicidade.lower()}"

                        # Mostrar calculo normalizado
                        if periodicidade == 'MENSAL':
                            resposta_para_usuario += f" → {formatar_moeda(impacto)} (×{meses})\n"
                        elif periodicidade == 'ANUAL':
                            resposta_para_usuario += f" → {formatar_moeda(impacto)} (integral)\n"
                        elif periodicidade == 'SEMANAL':
                            semanas = round(meses * 4.33)
                            resposta_para_usuario += f" → {formatar_moeda(impacto)} (×{semanas})\n"
                        elif periodicidade == 'QUINZENAL':
                            quinzenas = meses * 2
                            resposta_para_usuario += f" → {formatar_moeda(impacto)} (×{quinzenas})\n"
                        elif periodicidade == 'DIARIA':
                            dias = meses * 30
                            resposta_para_usuario += f" → {formatar_moeda(impacto)} (×{dias})\n"
                        else:
                            resposta_para_usuario += f" → {formatar_moeda(impacto)}\n"

                    resposta_para_usuario += f"\n━━━━━━━━━━━━━━━━━━\n"
                    resposta_para_usuario += f"💰 *Total: {formatar_moeda(reserva_ideal)}*\n\n"
                    resposta_para_usuario += "💡 _Use a aplicação web para editar quais contas incluir_"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # ===== INTENCAO: Consulta Saldo =====
                elif intent == 'Consulta Saldo':
                    contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                    contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                    saldo_query = gemini_service.extract_saldo_query(texto_msg, contas_list, usuario_id)
                    nome_conta = saldo_query.get('nome_conta')

                    conta_id = None
                    if nome_conta:
                        conta_id = finance_service.get_account_by_name(conn, usuario_id, nome_conta)
                        if not conta_id:
                            resposta_para_usuario = f"🤔 Não encontrei uma conta chamada '{nome_conta}'."
                            return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Buscar saldo(s)
                    contas_saldo = finance_service.get_saldo_contas(conn, usuario_id, conta_id)

                    if not contas_saldo:
                        resposta_para_usuario = "❌ Você não tem contas cadastradas."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Formatar resposta
                    if len(contas_saldo) == 1:
                        conta = contas_saldo[0]
                        icone = "💳" if conta['tipo_conta'] == "Cartão de Crédito" else "🏦" if conta['tipo_conta'] == "Conta Corrente" else "💰"
                        resposta_para_usuario = f"{icone} *{conta['nome_conta']}* ({conta['tipo_conta']})\n\n"
                        resposta_para_usuario += f"💵 Saldo: *{formatar_moeda(conta['saldo'])}*"
                    else:
                        resposta_para_usuario = "💰 *Seus Saldos:*\n\n"
                        total_geral = 0
                        for conta in contas_saldo:
                            icone = "💳" if conta['tipo_conta'] == "Cartão de Crédito" else "🏦" if conta['tipo_conta'] == "Conta Corrente" else "💰"
                            resposta_para_usuario += f"{icone} *{conta['nome_conta']}*\n"
                            resposta_para_usuario += f"   {formatar_moeda(conta['saldo'])}\n\n"
                            total_geral += conta['saldo']

                        resposta_para_usuario += f"💵 *Total Geral:* {formatar_moeda(total_geral)}"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # ===== INTENCAO: Listar Contas =====
                elif intent == 'Listar Contas':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Listar Contas',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Ajustar Saldo Inicial =====
                elif intent == 'Ajustar Saldo Inicial':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Ajustar Saldo Inicial',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Consulta por Periodo =====
                elif intent == 'Consulta Período':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consulta Período',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Consulta Potes =====
                elif intent == 'Consulta Potes':
                    potes = finance_service.get_pote_status(conn, usuario_id)
                    resp = "📊 *Seus Potes:*\n\n"
                    if not potes:
                        resp = "Você não tem potes configurados."
                    else:
                        for p in potes:
                            gasto = float(p[2] or 0) * -1
                            limite = float(p[1])
                            rest = limite - gasto
                            resp += f"🏷️*{p[0]}*\n  Gasto: {formatar_moeda(gasto)}\n  Limite: {formatar_moeda(limite)}\n  Resta: {formatar_moeda(rest)}\n\n"
                    return jsonify({"status": "sucesso", "resposta": resp}), 200

                # ===== INTENCAO: Consulta Contas Fixas Pendentes =====
                elif intent == 'Consulta Contas Fixas':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consulta Contas Fixas',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Consulta Todas as Contas Recorrentes =====
                elif intent == 'Consulta Todas Contas':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consulta Todas Contas',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Vencimentos Hoje =====
                elif intent == 'Vencimentos Hoje':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Vencimentos Hoje',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Vencimentos Amanha =====
                elif intent == 'Vencimentos Amanhã':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Vencimentos Amanhã',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Vencimentos Essa Semana =====
                elif intent == 'Vencimentos Essa Semana':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Vencimentos Essa Semana',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                # ===== INTENCAO: Contas Atrasadas =====
                elif intent == 'Contas Atrasadas':
                    from app.routes.webhooks.intents import INTENT_REGISTRY

                    try:
                        # Usar a classe ContasAtrasadasIntent registrada
                        intent_class = INTENT_REGISTRY.get('Contas Atrasadas')

                        if not intent_class:
                            raise Exception("Intent 'Contas Atrasadas' não encontrada no registry")

                        # Instanciar a intent com parametros corretos
                        intent_instance = intent_class(
                            usuario_id=usuario_id,
                            mensagem=texto_msg,
                            conn=conn
                        )

                        # Executar usando handle() e extrair message
                        result = intent_instance.handle()
                        resposta_para_usuario = result["message"]

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    except Exception as e:
                        print(f"[CONTAS-ATRASADAS] Erro: {e}")
                        traceback.print_exc()
                        resposta_para_usuario = "❌ Erro ao consultar contas atrasadas."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # NOVO: Usar TransferenciaIntent com validacao de saldo
                elif intent == 'Transferência':
                    from app.routes.webhooks.intents import INTENT_REGISTRY

                    try:
                        # Usar a classe TransferenciaIntent registrada
                        intent_class = INTENT_REGISTRY.get('Transferência')

                        if not intent_class:
                            raise Exception("Intent 'Transferência' não encontrada no registry")

                        # Instanciar a intent com parametros corretos
                        intent_instance = intent_class(
                            usuario_id=usuario_id,
                            mensagem=texto_msg,
                            conn=conn,
                            numero_whatsapp=numero_limpo  # Corrigido: usar numero_limpo
                        )

                        # Executar usando handle() e extrair message
                        result = intent_instance.handle()
                        resposta_para_usuario = result["message"]

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    except Exception as e:
                        print(f"[TRANSFER-INTENT] Erro ao processar transferência: {e}")
                        resposta_para_usuario = f"❌ Erro ao processar transferência: {str(e)}"
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                #==== INTENCAO: Pagamento Fatura =====
                elif intent == 'Pagamento Fatura':
                    contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                    contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                    fatura_data = gemini_service.extract_fatura_payment_details(texto_msg, contas_list, usuario_id)

                    # Validar se conseguiu extrair os dados necessarios
                    valor_decimal_raw = fatura_data.get('valor_decimal')
                    nome_origem = fatura_data.get('conta_origem')
                    nome_cartao = fatura_data.get('conta_cartao')

                    if not valor_decimal_raw or not nome_origem or not nome_cartao:
                        resposta_para_usuario = (
                            "🤔 Não consegui identificar todos os dados do pagamento.\n\n"
                            "Por favor, informe:\n"
                            "• Valor pago\n"
                            "• Conta de origem\n"
                            "• Cartão que foi pago\n\n"
                            "Exemplo: *Paguei 500 reais da fatura do Nubank com o Inter*"
                        )
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    valor_dec = float(valor_decimal_raw)

                    conta_id_origem = finance_service.get_account_by_name(conn, usuario_id, nome_origem)
                    conta_id_cartao = finance_service.get_account_by_name(conn, usuario_id, nome_cartao)

                    if not conta_id_origem or not conta_id_cartao:
                        resposta_para_usuario = f"❌ Não encontrei as contas mencionadas ({nome_origem} → {nome_cartao})."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    nome_cartao_pago = finance_service.create_fatura_payment(
                        conn, usuario_id, conta_id_origem, conta_id_cartao, valor_dec, data_hoje
                    )

                    conn.commit()

                    valor_fmt = formatar_moeda(valor_dec)
                    resposta_para_usuario = f"✅ Pagamento da fatura '{nome_cartao_pago}' ({valor_fmt}) registrado com sucesso!"

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                #==== INTENCAO: Consulta Valor Fatura =====
                elif intent == 'Consulta Valor Fatura':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consulta Valor Fatura',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #=== INTENCAO: Consulta Categoria Especifica =====
                elif intent == 'Consulta Categoria Específica':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consulta Categoria Específica',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Criar Evento ====
                elif intent == 'Criar Evento':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Criar Evento',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Deletar Evento ====
                elif intent == 'Deletar Evento':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Deletar Evento',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Consultar Agenda ====
                elif intent == 'Consultar Agenda':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Consultar Agenda',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Horarios Livres ====
                elif intent == 'Horários Livres':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Horários Livres',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Configurar Notificacoes ====
                elif intent == 'Configurar Notificações':
                    print(f"[WHATSAPP] Intenção de Configurar Notificações detectada")

                    # Verificar se e sobre RESUMO MATINAL especificamente
                    texto_lower = texto_msg.lower()
                    is_resumo_matinal = any(kw in texto_lower for kw in ['resumo', 'matinal', 'briefing', 'preparação do dia'])

                    if is_resumo_matinal:
                        # HANDLER ESPECIFICO PARA RESUMO MATINAL
                        print(f"[WHATSAPP] Configuração de Resumo Matinal detectada")

                        # Detectar acao (ativar/desativar/configurar)
                        if any(kw in texto_lower for kw in ['ativar', 'ligar', 'ativa', 'ative']):
                            acao = 'ativar'
                        elif any(kw in texto_lower for kw in ['desativar', 'desligar', 'desative']):
                            acao = 'desativar'
                        else:
                            acao = 'configurar'

                        # Extrair horario (se houver)
                        hora_match = re.search(r'(\d{1,2})[h:](\d{2})?', texto_msg)
                        hora = None

                        if hora_match:
                            hora_h = int(hora_match.group(1))
                            hora_m = int(hora_match.group(2)) if hora_match.group(2) else 0
                            hora = f"{hora_h:02d}:{hora_m:02d}"

                        # Executar acao
                        if acao == 'ativar':
                            sucesso, msg, config = NotificationConfigService.update_resumo_matinal_config(
                                usuario_id, ativo=True, hora=hora if hora else None
                            )
                        elif acao == 'desativar':
                            sucesso, msg, config = NotificationConfigService.update_resumo_matinal_config(
                                usuario_id, ativo=False
                            )
                        elif acao == 'configurar':
                            sucesso, msg, config = NotificationConfigService.update_resumo_matinal_config(
                                usuario_id, ativo=True, hora=hora
                            )
                        else:
                            sucesso = False
                            msg = "Ação não reconhecida"
                            config = None

                        if sucesso and config:
                            resposta_para_usuario = f"✅ {msg}\n\n"
                            resposta_para_usuario += f"📱 *Resumo Matinal - Status atual:*\n"
                            resposta_para_usuario += f"• Ativo: {'Sim' if config['resumo_matinal_ativo'] else 'Não'}\n"
                            resposta_para_usuario += f"• Horário: {config['resumo_matinal_hora'].strftime('%H:%M')}\n\n"
                            resposta_para_usuario += "💡 Configure sua localização para receber informações de clima:\n"
                            resposta_para_usuario += '"Configurar localização: [Cidade], [Estado]"'
                        else:
                            resposta_para_usuario = f"❌ {msg}"

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # HANDLER ESPECIFICO PARA CHECK-IN NOTURNO
                    is_checkin_noturno = any(kw in texto_lower for kw in ['check-in', 'checkin', 'check in', 'noturno', 'contas pendentes'])

                    if is_checkin_noturno:
                        print(f"[WHATSAPP] Configuração de Check-in Noturno detectada")

                        # Detectar acao (ativar/desativar/configurar)
                        if any(kw in texto_lower for kw in ['ativar', 'ligar', 'ativa', 'ative']):
                            acao = 'ativar'
                        elif any(kw in texto_lower for kw in ['desativar', 'desligar', 'desative', 'off']):
                            acao = 'desativar'
                        else:
                            acao = 'configurar'

                        # Extrair horario (se houver)
                        hora_match = re.search(r'(\d{1,2})[h:](\d{2})?', texto_msg)
                        hora = None

                        if hora_match:
                            hora_h = int(hora_match.group(1))
                            hora_m = int(hora_match.group(2)) if hora_match.group(2) else 0
                            hora = f"{hora_h:02d}:{hora_m:02d}"

                        # Executar acao
                        if acao == 'ativar':
                            sucesso, msg, config = NotificationConfigService.update_checkin_noturno_config(
                                usuario_id, ativo=True, hora=hora if hora else None
                            )
                        elif acao == 'desativar':
                            sucesso, msg, config = NotificationConfigService.update_checkin_noturno_config(
                                usuario_id, ativo=False
                            )
                        elif acao == 'configurar':
                            sucesso, msg, config = NotificationConfigService.update_checkin_noturno_config(
                                usuario_id, ativo=True, hora=hora
                            )
                        else:
                            sucesso = False
                            msg = "Ação não reconhecida"
                            config = None

                        if sucesso and config:
                            resposta_para_usuario = f"✅ {msg}\n\n"
                            resposta_para_usuario += f"🌙 *Check-in Noturno - Status atual:*\n"
                            resposta_para_usuario += f"• Ativo: {'Sim' if config['checkin_noturno_ativo'] else 'Não'}\n"
                            resposta_para_usuario += f"• Horário: {config['checkin_noturno_hora'].strftime('%H:%M')}\n\n"
                            resposta_para_usuario += "💡 O check-in envia uma lista de contas pendentes dos últimos 7 dias.\n"
                            resposta_para_usuario += "Você pode confirmar todas de uma vez ou parcialmente.\n\n"
                            resposta_para_usuario += "⏰ Horário permitido: 18:00 às 23:00"
                        else:
                            resposta_para_usuario = f"❌ {msg}"

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Se NAO for resumo matinal nem check-in, processar outras notificacoes (CODIGO EXISTENTE)
                    config_data = gemini_service.extract_notification_config(texto_msg, usuario_id)

                    tipo = config_data.get('tipo')
                    acao = config_data.get('acao')
                    hora = config_data.get('hora')
                    dias_antes = config_data.get('dias_antes')

                    if tipo == 'agenda_diaria':
                        if acao == 'ativar':
                            sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                                usuario_id, ativa=True
                            )
                        elif acao == 'desativar':
                            sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                                usuario_id, ativa=False
                            )
                        elif acao == 'configurar':
                            sucesso, msg = NotificationConfigService.update_agenda_diaria_config(
                                usuario_id, ativa=True, hora=hora
                            )
                        else:
                            sucesso = False
                            msg = "Ação não reconhecida"

                        if sucesso:
                            # Buscar config atual
                            config = NotificationConfigService.get_or_create_config(usuario_id)
                            resposta_para_usuario = f"✅ {msg}\n\n"
                            resposta_para_usuario += f"📱 *Agenda Diária - Status atual:*\n"
                            resposta_para_usuario += f"• Ativa: {'Sim' if config['agenda_diaria_ativa'] else 'Não'}\n"
                            resposta_para_usuario += f"• Horário: {config['agenda_diaria_hora'].strftime('%H:%M')}\n"
                        else:
                            resposta_para_usuario = f"❌ {msg}"

                    elif tipo == 'contas_vencer':
                        if acao == 'ativar':
                            sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                                usuario_id, ativa=True
                            )
                        elif acao == 'desativar':
                            sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                                usuario_id, ativa=False
                            )
                        elif acao == 'configurar':
                            sucesso, msg = NotificationConfigService.update_contas_vencer_config(
                                usuario_id, ativa=True, dias_antes=dias_antes, hora=hora
                            )
                        else:
                            sucesso = False
                            msg = "Ação não reconhecida"

                        if sucesso:
                            config = NotificationConfigService.get_or_create_config(usuario_id)
                            resposta_para_usuario = f"✅ {msg}\n\n"
                            resposta_para_usuario += f"📱 *Contas a Vencer - Status atual:*\n"
                            resposta_para_usuario += f"• Ativa: {'Sim' if config['contas_vencer_ativa'] else 'Não'}\n"
                            resposta_para_usuario += f"• Dias antes: {config['contas_vencer_dias_antes']}\n"
                            resposta_para_usuario += f"• Horário: {config['contas_vencer_hora'].strftime('%H:%M')}\n"
                        else:
                            resposta_para_usuario = f"❌ {msg}"

                    else:
                        resposta_para_usuario = "🤔 Não entendi qual tipo de notificação você quer configurar."

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                #==== INTENCAO: Configurar Localizacao ====
                elif intent == 'Configurar Localização':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Configurar Localização',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Analise Inteligente ====
                elif intent == 'Análise Inteligente':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Análise Inteligente',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Comparacao Mensal ====
                elif intent == 'Comparação Mensal':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Comparação Mensal',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Previsao de Gastos ====
                elif intent == 'Previsão de Gastos':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Previsão de Gastos',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Grafico de Gastos ====
                elif intent == 'Gráfico de Gastos':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Gráfico de Gastos',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Solicitar API Key ====
                elif intent == 'Solicitar API Key':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Solicitar API Key',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Configurar Relatorio Mensal ====
                elif intent == 'Configurar Relatório Mensal':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Configurar Relatório Mensal',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Configurar Endereco ====
                elif intent == 'Configurar Endereço':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Configurar Endereço',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Listar Enderecos ====
                elif intent == 'Listar Endereços':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Listar Endereços',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Deletar Endereco ====
                elif intent == 'Deletar Endereço':
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Deletar Endereço',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                elif intent == "Menu de Ajuda":
                    from app.routes.webhooks.intents import route_intent

                    result = route_intent(
                        intent_name='Menu de Ajuda',
                        usuario_id=usuario_id,
                        mensagem=texto_msg,
                        conn=conn,
                        numero_whatsapp=numero_limpo
                    )
                    return jsonify({"status": "sucesso", "resposta": result["message"]}), 200

                #==== INTENCAO: Upload Drive ====
                elif intent == 'Upload Drive':
                    from app.routes.webhooks.intents import INTENT_REGISTRY

                    try:
                        # Verificar se recebemos dados de midia do bot
                        media_data = data.get('media_data')  # base64 ou bytes
                        media_type = data.get('media_type')  # ex: image/jpeg
                        media_filename = data.get('media_filename', 'arquivo')

                        # Usar a classe UploadDriveIntent registrada
                        intent_class = INTENT_REGISTRY.get('Upload Drive')

                        if not intent_class:
                            raise Exception("Intent 'Upload Drive' não encontrada no registry")

                        # Instanciar a intent com parametros + dados de midia
                        intent_instance = intent_class(
                            usuario_id=usuario_id,
                            mensagem=texto_msg,
                            conn=conn,
                            numero_whatsapp=numero_limpo,
                            media_data=media_data,
                            media_type=media_type,
                            media_filename=media_filename
                        )

                        # Executar usando handle() e extrair message
                        result = intent_instance.handle()
                        resposta_para_usuario = result["message"]

                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    except Exception as e:
                        print(f"[UPLOAD-DRIVE] Erro: {e}")
                        traceback.print_exc()
                        resposta_para_usuario = "❌ Erro ao fazer upload para o Google Drive."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                else:
                    return jsonify({"status": "sucesso", "resposta": "🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'."}), 200

        except Exception as e:
            print(f"[WHATSAPP] Erro: {e}")

            # Verificar se e erro de quota do Gemini (429)
            error_str = str(e)
            if '429' in error_str or 'quota' in error_str.lower() or 'rate limit' in error_str.lower():
                return jsonify({
                    "status": "erro",
                    "resposta": (
                        "⚠️ *Limite de IA Excedido*\n\n"
                        "O sistema atingiu o limite diário de processamento de IA.\n\n"
                        "🔧 *Soluções:*\n"
                        "• Aguarde alguns minutos e tente novamente\n"
                        "• Use comandos diretos (ex: 'gastei 50 em comida')\n"
                        "• Configure sua própria chave de API do Gemini\n\n"
                        "⏰ O limite é renovado automaticamente."
                    )
                }), 429

            return jsonify({"status": "erro", "resposta": "Erro ao processar."}), 500


# Instancia singleton
_handler = WhatsAppHandler()


def handle_whatsapp_webhook() -> Tuple[Any, int]:
    """Funcao de entrada para o webhook."""
    return _handler.handle()
