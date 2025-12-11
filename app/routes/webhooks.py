# app/routes/webhooks.py (COM SISTEMA DE CONFIRMAÇÃO)
from flask import Blueprint, jsonify, request
from werkzeug.exceptions import BadRequest
from sqlalchemy import exc as sqlalchemy_exc
from datetime import date
from app.services.period_query_service import PeriodQueryService
from app.services.fixed_bills_service import FixedBillsService

from sqlalchemy import text

from app import db_engine, gemini_model
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL, WEBHOOK_SIGNATURE_KEY
from app.utils import formatar_moeda, ensure_db_connection, verify_hmac_signature, compare_keys_safe, sanitize_for_log, sanitize_input

from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service
from app.services import user_service
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.services.nightly_checkin_service import NightlyCheckinService
from app.services.redis_service import redis_service
from app.services.transaction_feedback_service import gerar_feedback_transacao

from flask import redirect, request, render_template_string
from app.services.google_calendar_oauth_service import GoogleCalendarOAuthService
from app.services import finance_service, notification_service
from app.config import BOT_WHATSAPP_URL, API_SECRET_KEY
from app.services.calendar_query_service import CalendarQueryService

from app.services.calendar_management_service import CalendarManagementService
from app.services.notification_config_service import NotificationConfigService
from app.services.event_confirmation_service import EventConfirmationService
from datetime import date, timedelta, datetime
from zoneinfo import ZoneInfo

webhooks_bp = Blueprint('webhooks', __name__)


@webhooks_bp.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """Rota do Gatilho Android com CONFIRMAÇÃO"""
    
    if not db_engine or not gemini_model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503
    
    try:
        data = request.json
        texto_notificacao = data.get('texto')
        user_api_key = data.get('user_api_key')
        
        if not texto_notificacao or not user_api_key:
            return jsonify({"status": "erro", "mensagem": "Dados faltando"}), 400
        
        print(f"[AUTOMATE] Recebido: {texto_notificacao}")
        
        # 1. Autenticar
        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({"status": "erro", "mensagem": "API key inválida"}), 401
        
        usuario_id, numero_whatsapp_usuario = user_info
        print(f"[AUTOMATE] Usuário: {usuario_id}")

        # 2. Extrair com IA
        transacao_gemini = gemini_service.extract_from_notification(texto_notificacao, usuario_id)
        tipo_transacao = transacao_gemini.get('tipo_fluxo', 'Despesa')
        transacao_descricao = transacao_gemini.get('descricao_bruta')
        valor_decimal = float(transacao_gemini.get('valor_decimal', 0))
        data_hoje = date.today()

        with db_engine.connect() as conn:
            # 3. Buscar dados necessários
            contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
            
            conta_id_transacao = None
            if tipo_transacao == 'Despesa' and any(kw in texto_notificacao.lower() for kw in ['cartão', 'compra', 'credit']):
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Cartão de Crédito'), None)
            if not conta_id_transacao:
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Conta Corrente'), contas_usuario[0][0])

            categories_json_list = finance_service.get_user_categories(conn, usuario_id, tipo_transacao)
            id_outros_fallback = finance_service.get_fallback_category_id(conn, tipo_transacao)

            # 4. Categorizar
            id_categoria_final = gemini_service.categorize_transaction(
                categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback, usuario_id
            )
            
            # 5. NOVO: Criar transação PENDENTE no Redis
            fatura_id_transacao = None
            conta_tipo_result = next((c[2] for c in contas_usuario if c[0] == conta_id_transacao), None)
            if conta_tipo_result == 'Cartão de Crédito':
                fatura_id_transacao = finance_service.get_or_create_fatura(conn, conta_id_transacao, data_hoje, usuario_id)
            
            valor_para_db = valor_decimal if tipo_transacao == 'Renda' else (valor_decimal * -1)
            
            # Preparar dados para salvar depois
            transacao_data = {
                'usuario_id': usuario_id,
                'conta_id': conta_id_transacao,
                'categoria_id': id_categoria_final,
                'fatura_id': fatura_id_transacao,
                'descricao': transacao_descricao,
                'valor_db': valor_para_db,
                'valor_original': valor_decimal,
                'tipo_transacao': tipo_transacao,
                'data_transacao': str(data_hoje),
                'origem': 'automate'
            }
            
            # Verificar se Redis está disponível
            if not redis_service.is_connected():
                # Fallback: Salvar direto sem confirmação
                print("[AUTOMATE] Redis indisponível. Salvando direto.")
                finance_service.create_transaction(
                    conn, usuario_id, conta_id_transacao, id_categoria_final, 
                    fatura_id_transacao, transacao_descricao, valor_para_db, 
                    tipo_transacao, data_hoje
                )
                conn.commit()
                
                nome_cat = finance_service.get_category_name_by_id(conn, id_categoria_final)
                mensagem = f"✅ Transação salva!\n\nDescrição: {transacao_descricao}\nValor: {formatar_moeda(valor_decimal)}\nCategoria: {nome_cat}"
                notification_service.enviar_notificacao_whatsapp(
                    numero_whatsapp_usuario, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                )
                return jsonify({"status": "sucesso"}), 200
            
            # Redis disponível: Criar pendente
            transaction_id = TransactionConfirmationService.create_pending_transaction(
                numero_whatsapp_usuario,
                transacao_data
            )
            
            if not transaction_id:
                # Erro no Redis, salvar direto
                finance_service.create_transaction(
                    conn, usuario_id, conta_id_transacao, id_categoria_final,
                    fatura_id_transacao, transacao_descricao, valor_para_db,
                    tipo_transacao, data_hoje
                )
                conn.commit()
                return jsonify({"status": "sucesso"}), 200
            
            # Enviar mensagem de confirmação
            mensagem_confirmacao = TransactionConfirmationService.format_confirmation_message(
                transacao_data,
                categories_json_list,
                transaction_id
            )
            
            notification_service.enviar_notificacao_whatsapp(
                numero_whatsapp_usuario,
                mensagem_confirmacao,
                BOT_WHATSAPP_URL,
                API_SECRET_KEY
            )
            
            return jsonify({
                "status": "sucesso",
                "mensagem": "Aguardando confirmação do usuário",
                "transaction_id": transaction_id
            }), 200

    except Exception as e:
        print(f"[AUTOMATE] Erro: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500


@webhooks_bp.route('/api/transacao', methods=['POST'])
def handle_api_transacao():
    """
    Endpoint direto para registro de transações via iPhone/automações.

    Payload esperado:
    {
        "user_api_key": "...",
        "valor": 15.90,
        "local": "sorveteria",
        "descricao": "2 sorvetes" (opcional),
        "conta": "Nubank",
        "tipo_pagamento": "credito"  // credito/debito/pix/dinheiro
    }
    """
    if not db_engine or not gemini_model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503

    # Adiciona o log para todas as requisições
    raw_data = request.get_data(as_text=True)
    print(f"[API-TRANSACAO] Conteúdo recebido: {raw_data}")

    try:
        try:
            data = request.get_json()
            if data is None:
                raise BadRequest("Request body is not JSON or is empty")
        except BadRequest as e:
            print(f"[API-TRANSACAO] ERRO: {e}")
            return jsonify({"erro": "JSON inválido ou ausente"}), 400

        # Log do payload recebido (sanitizado para não expor dados sensíveis)
        print(f"[API-TRANSACAO] Payload JSON decodificado: {sanitize_for_log(data)}")

        user_api_key = data.get('user_api_key')
        valor = data.get('valor')

        # Sanitizar inputs de texto para prevenir XSS/injection
        local = sanitize_input(data.get('local', ''), max_length=100) if data.get('local') else None
        descricao = sanitize_input(data.get('descricao', ''), max_length=200, allow_special_chars=True) if data.get('descricao') else None
        conta_nome = sanitize_input(data.get('conta', ''), max_length=50) if data.get('conta') else None
        tipo_pagamento_raw = data.get('tipo_pagamento', '')

        # Normalizar tipo_pagamento
        if tipo_pagamento_raw:
            tipo_pagamento = sanitize_input(tipo_pagamento_raw, max_length=20).strip().lower()
            # Normalizar variações comuns
            tipo_pagamento = tipo_pagamento.replace('é', 'e').replace('í', 'i')
        else:
            tipo_pagamento = None

        # Validações de campos obrigatórios com detalhamento
        campos_faltando = []
        if not user_api_key:
            campos_faltando.append('user_api_key')
        if not valor:
            campos_faltando.append('valor')
        if not local:
            campos_faltando.append('local')
        if not conta_nome:
            campos_faltando.append('conta')
        if not tipo_pagamento:
            campos_faltando.append('tipo_pagamento')

        if campos_faltando:
            erro_msg = f"Campos obrigatórios faltando: {', '.join(campos_faltando)}"
            print(f"[API-TRANSACAO] ERRO: {erro_msg}")
            print(f"[API-TRANSACAO] Dados recebidos: user_api_key={bool(user_api_key)}, valor={valor}, local={local}, conta={conta_nome}, tipo_pagamento={tipo_pagamento}")
            return jsonify({
                "status": "erro",
                "mensagem": erro_msg
            }), 400

        # Validar valor numérico positivo e razoável
        try:
            valor = float(valor)
            if valor <= 0:
                raise ValueError("Valor deve ser positivo")
            if valor > 1000000000:  # 1 bilhão - limite razoável
                raise ValueError("Valor muito alto (máx: 1 bilhão)")
        except (ValueError, TypeError) as e:
            return jsonify({
                "status": "erro",
                "mensagem": f"Valor inválido: {str(e)}"
            }), 400

        # Validar tipo_pagamento
        tipos_validos = ['credito', 'debito', 'pix', 'dinheiro']
        if tipo_pagamento not in tipos_validos:
            return jsonify({
                "status": "erro",
                "mensagem": f"tipo_pagamento inválido. Use: {', '.join(tipos_validos)}"
            }), 400

        # Normalizar descricao (pode ser None)
        if descricao:
            descricao = descricao.strip()
            if len(descricao) == 0:
                descricao = None

        print(f"[API-TRANSACAO] Recebido: {valor} - {local} ({conta_nome} / {tipo_pagamento})")

        # 1. Autenticar usuário
        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({"status": "erro", "mensagem": "API key inválida"}), 401

        usuario_id, numero_whatsapp_usuario = user_info
        print(f"[API-TRANSACAO] Usuário: {usuario_id}")

        with db_engine.connect() as conn:
            # 2. Buscar conta pelo nome
            conta_detalhes = finance_service.get_account_details_by_name(conn, usuario_id, conta_nome)

            if not conta_detalhes:
                return jsonify({
                    "status": "erro",
                    "mensagem": f"Conta '{conta_nome}' não encontrada. Verifique o nome exato."
                }), 400

            conta_id = conta_detalhes['id']
            conta_tipo = conta_detalhes['tipo']
            conta_nome_real = conta_detalhes['nome']

            print(f"[API-TRANSACAO] Conta encontrada: {conta_nome_real} (ID: {conta_id}, Tipo: {conta_tipo})")

            # 3. Preparar texto para categorização IA
            texto_para_ia = local
            if descricao:
                texto_para_ia = f"{local} - {descricao}"

            # 4. Categorizar com IA
            categorias = finance_service.get_user_categories(conn, usuario_id, 'Despesa')
            id_categoria_outros = finance_service.get_fallback_category_id(conn, 'Despesa')

            id_categoria = gemini_service.categorize_transaction(
                categorias,
                texto_para_ia,
                'Despesa',
                id_categoria_outros,
                usuario_id
            )

            print(f"[API-TRANSACAO] Categoria IA: {id_categoria}")

            # 5. Detectar se precisa vincular à fatura
            fatura_id = None
            if tipo_pagamento == 'credito':
                # Vincular à fatura
                data_hoje = date.today()
                fatura_id = finance_service.get_or_create_fatura(conn, conta_id, data_hoje, usuario_id)
                print(f"[API-TRANSACAO] Fatura ID: {fatura_id}")

            # 6. Preparar descrição final para salvar
            descricao_final = local
            if descricao:
                descricao_final = f"{local} - {descricao}"

            # 7. Preparar dados para transação pendente
            valor_db = valor * -1  # Negativo para despesa

            transacao_data = {
                'usuario_id': usuario_id,
                'conta_id': conta_id,
                'conta_nome': conta_nome_real,
                'conta_tipo': conta_tipo,
                'categoria_id': id_categoria,
                'fatura_id': fatura_id,
                'local': local,
                'descricao': descricao,  # Guardar separado para mensagem
                'descricao_final': descricao_final,  # Combinado para salvar
                'valor_db': valor_db,
                'valor_original': valor,
                'tipo_transacao': 'Despesa',
                'tipo_pagamento': tipo_pagamento,
                'data_transacao': str(date.today()),
                'origem': 'api_endpoint'
            }

            # 8. Verificar se Redis está disponível
            if not redis_service.is_connected():
                # Fallback: Salvar direto sem confirmação
                print("[API-TRANSACAO] Redis indisponível. Salvando direto.")
                finance_service.create_transaction(
                    conn, usuario_id, conta_id, id_categoria,
                    fatura_id, descricao_final, valor_db,
                    'Despesa', date.today()
                )
                conn.commit()

                nome_cat = finance_service.get_category_name_by_id(conn, id_categoria)
                mensagem = f"✅ Transação salva!\n\n📍 {descricao_final}\n💵 {formatar_moeda(valor)}\n🏷️ {nome_cat}"
                notification_service.enviar_notificacao_whatsapp(
                    numero_whatsapp_usuario, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                )

                return jsonify({
                    "status": "success",
                    "message": "Transação salva com sucesso (Redis indisponível)",
                    "categoria_sugerida": nome_cat
                }), 200

            # 9. Criar transação pendente no Redis
            transaction_id = TransactionConfirmationService.create_pending_transaction(
                numero_whatsapp_usuario,
                transacao_data
            )

            if not transaction_id:
                # Erro no Redis, salvar direto
                finance_service.create_transaction(
                    conn, usuario_id, conta_id, id_categoria,
                    fatura_id, descricao_final, valor_db,
                    'Despesa', date.today()
                )
                conn.commit()

                return jsonify({
                    "status": "success",
                    "message": "Transação salva (erro no Redis)",
                    "transaction_id": None
                }), 200

            # 10. Salvar como "última pendente" para contexto
            redis_service.set_with_ttl(f"last_pending:{numero_whatsapp_usuario}", transaction_id, 300)

            # 11. Formatar e enviar mensagem de confirmação
            mensagem_confirmacao = TransactionConfirmationService.format_confirmation_message(
                transacao_data,
                categorias,
                transaction_id
            )

            notification_service.enviar_notificacao_whatsapp(
                numero_whatsapp_usuario,
                mensagem_confirmacao,
                BOT_WHATSAPP_URL,
                API_SECRET_KEY
            )

            # 12. Buscar nome da categoria para response
            nome_categoria = finance_service.get_category_name_by_id(conn, id_categoria)

            return jsonify({
                "status": "success",
                "message": "Transação pendente de confirmação no WhatsApp",
                "transaction_id": transaction_id,
                "categoria_sugerida": nome_categoria,
                "conta_utilizada": conta_nome_real,
                "vinculado_fatura": fatura_id is not None
            }), 200

    except Exception as e:
        print(f"[API-TRANSACAO] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({"status": "erro", "mensagem": str(e)}), 500


@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """Webhook WhatsApp com CONFIRMAÇÃO e suporte a cadastro"""
    
    try:
        ensure_db_connection()
    except Exception as e:
        return jsonify({
            "status": "erro",
            "resposta": "Banco de dados temporariamente indisponível"
        }), 503
    
    if not db_engine or not gemini_model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503

    # 1. Validar assinatura HMAC (primeira camada de segurança)
    webhook_signature = request.headers.get('X-Webhook-Signature', '').strip()
    if webhook_signature:
        payload = request.get_data()
        if not verify_hmac_signature(payload, webhook_signature, WEBHOOK_SIGNATURE_KEY):
            print("[SECURITY] ⚠️  Assinatura HMAC inválida no webhook WhatsApp")
            return jsonify({"status": "erro", "resposta": "Assinatura inválida"}), 401
    else:
        print("[SECURITY] ⚠️  Webhook sem assinatura HMAC (modo compatibilidade)")

    # 2. Autenticar API key (segunda camada de segurança)
    secret_key_recebida = request.headers.get('x-api-key', '').strip()
    if not secret_key_recebida or not compare_keys_safe(secret_key_recebida, API_SECRET_KEY):
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
        
        # 2.2. Usuário não cadastrado
        if not user_info:
            user_service.start_registration(numero_limpo)
            msg = "👋 Bem-vindo! Vamos fazer um cadastro rápido.\n\n*Como você gostaria de ser chamado?*"
            return jsonify({"status": "sucesso", "resposta": msg}), 200
        
        usuario_id = user_info[0]

        # 3. NOVO: Verificar confirmações/cancelamentos de EVENTOS
        msg_lower = texto_msg.strip().lower()

        # Verificar se quer calcular tempo de deslocamento
        wants_travel_time = 'calcular' in msg_lower and 'rota' in msg_lower

        palavras_confirmacao_evento = ['sim', 'confirmar', 'confirma', 'ok', 's']
        palavras_cancelamento_evento = ['não', 'nao', 'cancelar', 'cancela', 'desistir', 'n']

        # Verificar se é resposta simples de confirmação/cancelamento (mas não "sim, calcular")
        if not wants_travel_time and (msg_lower in palavras_confirmacao_evento or msg_lower in palavras_cancelamento_evento):
            # Buscar evento pendente
            event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)

            print(f"[EVENT-CONFIRM] Detectado '{msg_lower}' | Event ID: {event_id} | Has data: {event_data is not None}")

            if event_data:
                if msg_lower in palavras_confirmacao_evento:
                    # Confirmar e criar evento (com ou sem cálculo de tempo prévio)
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

            # Verificar se evento tem localização
            if not event_data.get('localizacao'):
                resposta_para_usuario = "❌ Este evento não tem localização definida, não posso calcular tempo de deslocamento."
                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # Buscar endereços cadastrados
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
                # Só 1 endereço: usar automaticamente
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
                # 2+ endereços: perguntar qual usar
                msg = "🚗 *Qual endereço usar como origem?*\n\n"

                for addr in user_addresses:
                    label = addr['label']
                    endereco = addr['endereco']
                    emoji = UserAddressService.LABEL_EMOJIS.get(label, '📍')
                    label_nome = UserAddressService.LABEL_NAMES.get(label, label.capitalize())

                    msg += f"{emoji} *{label_nome}*: {endereco}\n"

                msg += f"\n💬 Responda com: _'casa'_, _'trabalho'_ ou _'outro'_"

                return jsonify({"status": "sucesso", "resposta": msg}), 200

        # 3.0. Verificar se está respondendo a um CHECK-IN NOTURNO (PRIORIDADE 2)
        checkin_active_key = f"nightly_checkin_active:{numero_limpo}"
        checkin_active = redis_service.get(checkin_active_key)

        if checkin_active:
            checkin_id = checkin_active  # O valor é o checkin_id
            print(f"[CHECKIN-RESPONSE] Check-in ativo detectado: {checkin_id}")

            # Verificar Escape Hatch (palavras-chave que quebram o check-in)
            if any(kw in texto_msg.lower() for kw in NightlyCheckinService.ESCAPE_KEYWORDS):
                print(f"[CHECKIN-ESCAPE] Escape hatch detectado: '{texto_msg}'")
                redis_service.delete(checkin_active_key)
                # Continuar para classificação normal de intent
                # Não retorna aqui - deixa cair para o processamento normal
            else:
                # Processar resposta de check-in
                print(f"[CHECKIN-RESPONSE] Processando resposta: '{texto_msg}'")
                status, resposta = NightlyCheckinService.process_response(
                    numero_limpo, texto_msg, checkin_id
                )

                # A flag já foi removida dentro do process_response
                return jsonify({"status": "sucesso", "resposta": resposta}), 200

        # 3.1. Verificar se está respondendo a uma confirmação de TRANSAÇÃO
        # Tentar identificar transaction_id na mensagem ou buscar última pendente
        # (Simplificado: buscar por padrão "ID: XXXX" na última msg ou assumir contexto)

        # Por simplicidade, vamos checar se existe alguma pendente recente
        # Em produção, você pode enviar o transaction_id na mensagem ou usar contexto

        # Verificar se mensagem parece uma resposta de confirmação de TRANSAÇÃO
        # IMPORTANTE: Só processar se NÃO for confirmação de evento (eventos têm prioridade)
        msg_upper = texto_msg.strip().upper()

        # Bloquear transaction handler APENAS para palavras EXCLUSIVAS de evento
        # Palavras como "ok", "cancelar" são ambíguas - devem verificar contexto (TX pendente)
        palavras_exclusivas_evento = ['sim', 's', 'não', 'nao', 'n', 'desistir']
        is_exclusive_event_word = msg_lower in palavras_exclusivas_evento

        if not is_exclusive_event_word and (any(word in msg_upper for word in ['CONFIRMAR', 'OK', 'TROCAR', 'CANCELAR']) or msg_upper.isdigit()):
            # Provável resposta de confirmação de TRANSAÇÃO
            print(f"[TX-CONFIRM-CHECK] Detectada possível confirmação de transação: '{texto_msg}'")

            # Buscar última transação pendente
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

                        # Se fatura_id é 'PENDING', criar/buscar fatura agora
                        fatura_id_final = dados.get('fatura_id')
                        if fatura_id_final == 'PENDING':
                            conta_id = dados['conta_id']
                            usuario_id_tx = dados['usuario_id']
                            data_tx = date.fromisoformat(dados['data_transacao'])
                            fatura_id_final = finance_service.get_or_create_fatura(conn, conta_id, data_tx, usuario_id_tx)
                            print(f"[CONFIRM-SAVE] Fatura criada/encontrada: {fatura_id_final}")

                        # Usar descricao_final se disponível, senão usar descricao
                        descricao_para_salvar = dados.get('descricao_final', dados.get('descricao'))

                        # Criar transação e capturar o ID
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
                    
                    # Atualizar transação com o mapa
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
                # Palavra-chave de confirmação detectada, mas sem transação pendente
                print(f"[CONFIRM-CHECK] Palavra de confirmação detectada, mas nenhuma transação pendente encontrada")
                # Continuar para fluxo normal (pode ser uma pergunta normal que contém "ok")

        # Safety check para "cancelar" sem contexto
        if msg_lower in ['cancelar', 'cancela'] and len(texto_msg.strip().split()) == 1:
            # Usuário enviou apenas "cancelar" - verificar se há algo pendente
            has_pending_tx = redis_service.get(f"last_pending:{numero_limpo}") is not None
            event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)
            has_pending_event = event_data is not None

            if not has_pending_tx and not has_pending_event:
                print(f"[SAFETY-CHECK] Usuário enviou 'cancelar' mas nada está pendente")
                return jsonify({
                    "status": "sucesso",
                    "resposta": "❌ Não encontrei nada pendente para cancelar.\n\nSe quer deletar um evento específico, diga qual:\nExemplo: 'Deletar academia de hoje'"
                }), 200

        # 4. Verificar PRIMEIRO se é pagamento de conta (antes de classificar intent)
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
                itens_sem_valor = []  # Itens que não achou no banco E não tem valor

                for item_desc in itens_lista:
                    # Tentar encontrar conta fixa correspondente
                    match = FixedBillsService.find_matching_bill(conn, usuario_id, item_desc)

                    if match:
                        # ENCONTROU CONTA FIXA → Quitar
                        agendamento_id, desc_original, valor_previsto, dia_venc, tipo_agend, categoria, conta_id_agendamento = match

                        # Determinar valor a usar (mesma lógica para FIXO e LEMBRETE_VARIAVEL)
                        # Prioridade: valor informado pelo usuário > valor_previsto
                        if valor_total and len(itens_lista) == 1:
                            # UMA conta + valor informado → usar valor informado (ambos os tipos)
                            valor_pagar = valor_total
                        else:
                            # Múltiplas contas OU sem valor → usar valor_previsto como fallback
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
                            # Continua para próximos itens

                    else:
                        # NÃO ENCONTROU CONTA FIXA → Criar despesa

                        # Determinar valor da despesa
                        if valor_total and len(itens_lista) == 1:
                            # UMA despesa + valor informado
                            valor_despesa = valor_total
                        elif valor_total and len(itens_lista) > 1:
                            # Múltiplas despesas + valor total → precisa dividir
                            # Opção: Pedir valor individual (por ora, ignora)
                            itens_sem_valor.append({
                                'nome': item_desc,
                                'tipo': 'despesa'
                            })
                            continue
                        else:
                            # Sem valor → não pode criar despesa
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

                            # Escolher conta usando função centralizada (fuzzy matching + conta padrão do usuário)
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

                            # Criar transação
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
                            import traceback
                            print(f"[PROCESSAR-PAGAMENTO] ❌ ERRO ao criar despesa {item_desc}:")
                            print(f"[PROCESSAR-PAGAMENTO] Tipo do erro: {type(e).__name__}")
                            print(f"[PROCESSAR-PAGAMENTO] Mensagem: {str(e)}")
                            print(f"[PROCESSAR-PAGAMENTO] Traceback:")
                            traceback.print_exc()

                conn.commit()

                # PASSO 3: Formatar resposta unificada
                if not contas_fixas_quitadas and not despesas_criadas and itens_sem_valor:
                    # Nenhuma ação realizada → pedir valores
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
                            # Conta FIXA - mostrar diferença se houver
                            diferenca = abs(c['valor_previsto'] - c['valor'])
                            if diferenca > 1:
                                sinal = "+" if c['valor'] > c['valor_previsto'] else "-"
                                resposta_para_usuario += (
                                    f"\n💡 Valor previsto era {formatar_moeda(c['valor_previsto'])} "
                                    f"({sinal}{formatar_moeda(diferenca)})"
                                )

                    else:
                        # Múltiplas contas
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
                        # Múltiplas despesas
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

                # Se teve algumas ações mas ainda tem itens pendentes
                if itens_sem_valor and (contas_fixas_quitadas or despesas_criadas):
                    resposta_para_usuario += "\n\n⚠️ *Itens não processados* (faltou valor):\n"
                    for item in itens_sem_valor:
                        resposta_para_usuario += f"• {item['nome']}\n"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

        # 5. Classificar intenção (fluxo normal, se não foi pagamento)
        intent = gemini_service.get_message_intent(texto_msg, usuario_id)
        data_hoje = date.today()

        with db_engine.connect() as conn:
            conn.begin()

            # ===== HANDLER 1: Renda com CONFIRMAÇÃO =====
            if intent == 'Renda':
                trans_data = gemini_service.extract_transaction_details(texto_msg, intent, usuario_id)
                trans_desc = trans_data.get('descricao_bruta')
                valor_decimal_raw = trans_data.get('valor_decimal')

                # Validar se o valor foi extraído
                if valor_decimal_raw is None or valor_decimal_raw == 0:
                    resposta_para_usuario = (
                        f"🤔 Para registrar esta renda, preciso do valor.\n\n"
                        f"Exemplo: *recebi 500 de {trans_desc or 'salário'}*"
                    )
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                valor_dec = float(valor_decimal_raw)

                cats_list = finance_service.get_user_categories(conn, usuario_id, intent)
                id_outros = finance_service.get_fallback_category_id(conn, intent)
                id_categoria = gemini_service.categorize_transaction(cats_list, trans_desc, intent, id_outros, usuario_id)

                # Escolher conta usando função centralizada (fuzzy matching + conta padrão do usuário)
                conta_id, conta_nome, conta_tipo, origem = finance_service.choose_account_for_transaction(
                    conn, usuario_id, texto_msg, intent
                )

                valor_db = valor_dec

                # Criar pendente
                transacao_data = {
                    'usuario_id': usuario_id,
                    'conta_id': conta_id,
                    'conta_nome': conta_nome,
                    'conta_tipo': conta_tipo,
                    'categoria_id': id_categoria,
                    'fatura_id': None,
                    'descricao': trans_desc,
                    'valor_db': valor_db,
                    'valor_original': valor_dec,
                    'valor_total': None,
                    'num_parcelas': None,
                    'tipo_transacao': intent,
                    'tipo_pagamento': 'debito',
                    'data_transacao': str(data_hoje),
                    'origem': 'whatsapp'
                }

                if redis_service.is_connected():
                    tx_id = TransactionConfirmationService.create_pending_transaction(numero_limpo, transacao_data)
                    redis_service.set_with_ttl(f"last_pending:{numero_limpo}", tx_id, 300)
                    msg_confirm = TransactionConfirmationService.format_confirmation_message(
                        transacao_data, cats_list, tx_id
                    )
                    return jsonify({"status": "sucesso", "resposta": msg_confirm}), 200
                else:
                    # Fallback sem Redis
                    finance_service.create_transaction(
                        conn, usuario_id, conta_id, id_categoria, None,
                        trans_desc, valor_db, intent, data_hoje
                    )
                    conn.commit()
                    nome_cat = finance_service.get_category_name_by_id(conn, id_categoria)
                    resp = f"✅ {intent} salva!\n{trans_desc}\n{formatar_moeda(valor_dec)}\n{nome_cat}"
                    return jsonify({"status": "sucesso", "resposta": resp}), 200

            # ===== HANDLER 2: Despesa com CONFIRMAÇÃO =====
            elif intent == 'Despesa':
                trans_data = gemini_service.extract_transaction_details(texto_msg, intent, usuario_id)
                trans_desc = trans_data.get('descricao_bruta')
                valor_decimal_raw = trans_data.get('valor_decimal')

                # Validar se o valor foi extraído
                if valor_decimal_raw is None or valor_decimal_raw == 0:
                    resposta_para_usuario = (
                        f"🤔 Para registrar esta despesa, preciso do valor.\n\n"
                        f"Exemplo: *gastei 50 com {trans_desc or 'café'}*"
                    )
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                valor_dec = float(valor_decimal_raw)

                # Detectar parcelamento
                parcelamento_info = None
                num_parcelas = None
                valor_parcela = valor_dec

                parcelamento_info = gemini_service.extract_parcelamento_info(texto_msg, usuario_id)
                if parcelamento_info.get('parcelado'):
                    num_parcelas = parcelamento_info.get('num_parcelas')
                    if num_parcelas and num_parcelas > 1:
                        valor_parcela = valor_dec / num_parcelas
                        trans_desc = parcelamento_info.get('descricao_limpa', trans_desc)
                        print(f"[PARCELAMENTO] Detectado: {num_parcelas}x de {valor_parcela:.2f}")

                cats_list = finance_service.get_user_categories(conn, usuario_id, intent)
                id_outros = finance_service.get_fallback_category_id(conn, intent)
                id_categoria = gemini_service.categorize_transaction(cats_list, trans_desc, intent, id_outros, usuario_id)

                # Escolher conta usando função centralizada (fuzzy matching + conta padrão do usuário)
                conta_id, conta_nome, conta_tipo, origem = finance_service.choose_account_for_transaction(
                    conn, usuario_id, texto_msg, intent
                )

                # Se for cartão de crédito, marcar para criar fatura
                fatura_id = 'PENDING' if conta_tipo == 'Cartão de Crédito' else None

                # Para parcelamento, o valor_db é o valor da parcela
                if num_parcelas and num_parcelas > 1:
                    valor_db = valor_parcela * -1
                else:
                    valor_db = valor_dec * -1

                # Criar pendente
                transacao_data = {
                    'usuario_id': usuario_id,
                    'conta_id': conta_id,
                    'conta_nome': conta_nome,
                    'conta_tipo': conta_tipo,
                    'categoria_id': id_categoria,
                    'fatura_id': fatura_id,
                    'descricao': trans_desc,
                    'valor_db': valor_db,
                    'valor_original': valor_parcela if (num_parcelas and num_parcelas > 1) else valor_dec,
                    'valor_total': valor_dec if (num_parcelas and num_parcelas > 1) else None,
                    'num_parcelas': num_parcelas,
                    'tipo_transacao': intent,
                    'tipo_pagamento': 'credito' if fatura_id else 'debito',
                    'data_transacao': str(data_hoje),
                    'origem': 'whatsapp'
                }

                if redis_service.is_connected():
                    tx_id = TransactionConfirmationService.create_pending_transaction(numero_limpo, transacao_data)
                    redis_service.set_with_ttl(f"last_pending:{numero_limpo}", tx_id, 300)
                    msg_confirm = TransactionConfirmationService.format_confirmation_message(
                        transacao_data, cats_list, tx_id
                    )
                    return jsonify({"status": "sucesso", "resposta": msg_confirm}), 200
                else:
                    # Fallback sem Redis
                    finance_service.create_transaction(
                        conn, usuario_id, conta_id, id_categoria, None,
                        trans_desc, valor_db, intent, data_hoje
                    )
                    conn.commit()
                    nome_cat = finance_service.get_category_name_by_id(conn, id_categoria)
                    resp = f"✅ {intent} salva!\n{trans_desc}\n{formatar_moeda(valor_dec)}\n{nome_cat}"
                    return jsonify({"status": "sucesso", "resposta": resp}), 200
                
            # ===== INTENÇÃO: Consulta Reserva =====
            elif intent == 'Consulta Reserva':
                media_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)
                resposta_para_usuario = "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
                resposta_para_usuario += f"💰 Gasto mensal equivalente: *{formatar_moeda(media_mensal)}*\n"
                resposta_para_usuario += f"🎯 Reserva ideal ({meses} meses): *{formatar_moeda(reserva_ideal)}*\n\n"
                resposta_para_usuario += "💡 _Digite *'detalhes da reserva'* para ver quais contas estão incluídas no cálculo_"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Consulta Detalhes Reserva =====
            elif intent == 'Consulta Detalhes Reserva':
                # Primeiro, buscar quantos meses o usuário configurou
                media_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)

                # Buscar todos os agendamentos incluídos na reserva com cálculo dinâmico
                sql_detalhes = text("""
                    SELECT
                        a.descricao,
                        a.valor_previsto,
                        a.periodicidade,
                        s.nome_sub as categoria,
                        CASE
                            WHEN a.periodicidade = 'MENSAL' THEN a.valor_previsto * :meses
                            WHEN a.periodicidade = 'ANUAL' THEN a.valor_previsto * 1
                            WHEN a.periodicidade = 'SEMANAL' THEN a.valor_previsto * (:meses * 4.33)
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
                resposta_para_usuario += f"• Gasto mensal: *{formatar_moeda(media_mensal)}*\n"
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

                    # Mostrar cálculo normalizado
                    if periodicidade == 'MENSAL':
                        resposta_para_usuario += f" → {formatar_moeda(impacto)} (×{meses})\n"
                    elif periodicidade == 'ANUAL':
                        resposta_para_usuario += f" → {formatar_moeda(impacto)} (integral)\n"
                    elif periodicidade == 'SEMANAL':
                        semanas = int(meses * 4.33)
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

            # ===== INTENÇÃO: Consulta Saldo =====
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

            # ===== INTENÇÃO: Listar Contas =====
            elif intent == 'Listar Contas':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)

                if not contas_raw:
                    resposta_para_usuario = "❌ Você não tem contas cadastradas."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                resposta_para_usuario = "📋 *Suas Contas Cadastradas:*\n\n"

                # Agrupar por tipo
                contas_por_tipo = {}
                for conta in contas_raw:
                    conta_id, nome, tipo = conta[0], conta[1], conta[2]
                    if tipo not in contas_por_tipo:
                        contas_por_tipo[tipo] = []
                    contas_por_tipo[tipo].append(nome)

                # Formatar resposta
                for tipo, nomes in contas_por_tipo.items():
                    icone = "💳" if tipo == "Cartão de Crédito" else "🏦" if tipo == "Conta Corrente" else "💰"
                    resposta_para_usuario += f"{icone} *{tipo}*\n"
                    for nome in nomes:
                        resposta_para_usuario += f"   • {nome}\n"
                    resposta_para_usuario += "\n"

                resposta_para_usuario += f"_Total: {len(contas_raw)} conta(s)_"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Ajustar Saldo Inicial =====
            elif intent == 'Ajustar Saldo Inicial':
                import re

                # Extrair valor da mensagem
                match_valor = re.search(r'(\d+(?:[.,]\d+)?)', texto_msg.replace('.', '').replace(',', '.'))
                if not match_valor:
                    resposta_para_usuario = "🤔 Não consegui identificar o valor. Exemplo: 'ajustar saldo inicial Banco Inter 5000'"
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                valor = float(match_valor.group(1))

                # Tentar identificar a conta na mensagem
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                conta_encontrada = None

                for conta in contas_raw:
                    conta_id, nome_conta, tipo_conta = conta[0], conta[1], conta[2]
                    # Busca case-insensitive
                    if nome_conta.lower() in texto_msg.lower():
                        conta_encontrada = (conta_id, nome_conta, tipo_conta)
                        break

                if not conta_encontrada:
                    resposta_para_usuario = "🤔 Não consegui identificar qual conta. Contas disponíveis:\n\n"
                    for conta in contas_raw:
                        resposta_para_usuario += f"• {conta[1]}\n"
                    resposta_para_usuario += "\nTente: 'ajustar saldo inicial [nome da conta] [valor]'"
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                conta_id, nome_conta, tipo_conta = conta_encontrada

                # Atualizar saldo inicial
                sucesso = finance_service.update_saldo_inicial(conn, usuario_id, conta_id, valor)

                if sucesso:
                    icone = "💳" if tipo_conta == "Cartão de Crédito" else "🏦" if tipo_conta == "Conta Corrente" else "💰"
                    resposta_para_usuario = f"✅ *SALDO INICIAL ATUALIZADO* ✅\n\n"
                    resposta_para_usuario += f"{icone} *{nome_conta}*\n"
                    resposta_para_usuario += f"💵 Novo saldo inicial: *{formatar_moeda(valor)}*\n\n"
                    resposta_para_usuario += f"_O saldo atual já reflete esta mudança._"
                else:
                    resposta_para_usuario = "❌ Erro ao atualizar o saldo inicial. Tente novamente."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Consulta por Período =====
            elif intent == 'Consulta Período':

                # Extrair período da mensagem
                period_data = gemini_service.extract_period_query(texto_msg, usuario_id)
                period_type = period_data.get('period_type', 'hoje')
                categoria_filtro = period_data.get('categoria')

                # Calcular datas
                data_inicio, data_fim, desc_periodo = PeriodQueryService.get_period_dates(period_type)

                if categoria_filtro:
                    # Consulta com filtro de categoria
                    total, transacoes_raw = PeriodQueryService.query_by_category_and_period(
                        conn, usuario_id, categoria_filtro, data_inicio, data_fim
                    )

                    if total == 0:
                        resposta_para_usuario = f"✅ Você não gastou nada com '{categoria_filtro}' {desc_periodo}! 🎉"
                    else:
                        resposta_para_usuario = f"💸 *GASTOS COM {categoria_filtro.upper()}* {desc_periodo.upper()}\n\n"
                        resposta_para_usuario += f"💰 Total: *{formatar_moeda(total)}*\n\n"
                        resposta_para_usuario += "📋 Transações:\n"

                        for trans in transacoes_raw[:10]:  # Limitar a 10
                            desc, valor, data_trans = trans
                            valor_abs = abs(float(valor))
                            data_fmt = data_trans.strftime('%d/%m')
                            resposta_para_usuario += f"• {desc}: {formatar_moeda(valor_abs)} ({data_fmt})\n"

                        if len(transacoes_raw) > 10:
                            resposta_para_usuario += f"\n... e mais {len(transacoes_raw) - 10} transação(ões)"
                else:
                    # Consulta geral do período
                    total, transacoes = PeriodQueryService.query_expenses_by_period(
                        conn, usuario_id, data_inicio, data_fim
                    )

                    resposta_para_usuario = PeriodQueryService.format_period_query_response(
                        total, transacoes, desc_periodo
                    )

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
            
            # ===== INTENÇÃO: Consulta Potes =====
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
                        resp += f"🯠*{p[0]}*\n  Gasto: {formatar_moeda(gasto)}\n  Limite: {formatar_moeda(limite)}\n  Resta: {formatar_moeda(rest)}\n\n"
                return jsonify({"status": "sucesso", "resposta": resp}), 200
                        
            # ===== INTENÇÃO: Consulta Contas Fixas Pendentes =====
            elif intent == 'Consulta Contas Fixas':
                resposta_para_usuario = FixedBillsService.list_pending_bills_formatted(conn, usuario_id)
                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Vencimentos Hoje =====
            elif intent == 'Vencimentos Hoje':
                from datetime import datetime
                from zoneinfo import ZoneInfo
                from app.services.finance_service import get_vencimentos_periodo, format_vencimentos_message

                try:
                    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
                    hoje = datetime.now(TIMEZONE_BR).date()

                    # Buscar vencimentos de hoje
                    vencimentos = get_vencimentos_periodo(
                        conn=conn,
                        usuario_id=usuario_id,
                        data_inicio=hoje,
                        data_fim=hoje
                    )

                    # Formatar resposta
                    resposta_para_usuario = format_vencimentos_message(
                        vencimentos=vencimentos,
                        periodo="HOJE",
                        data_referencia=hoje
                    )

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                except Exception as e:
                    print(f"[VENCIMENTOS-HOJE] Erro: {e}")
                    resposta_para_usuario = "❌ Erro ao consultar vencimentos de hoje."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Vencimentos Amanhã =====
            elif intent == 'Vencimentos Amanhã':
                from datetime import datetime, timedelta
                from zoneinfo import ZoneInfo
                from app.services.finance_service import get_vencimentos_periodo, format_vencimentos_message

                try:
                    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
                    hoje = datetime.now(TIMEZONE_BR).date()
                    amanha = hoje + timedelta(days=1)

                    # Buscar vencimentos de amanhã
                    vencimentos = get_vencimentos_periodo(
                        conn=conn,
                        usuario_id=usuario_id,
                        data_inicio=amanha,
                        data_fim=amanha
                    )

                    # Formatar resposta
                    resposta_para_usuario = format_vencimentos_message(
                        vencimentos=vencimentos,
                        periodo="AMANHÃ",
                        data_referencia=amanha
                    )

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                except Exception as e:
                    print(f"[VENCIMENTOS-AMANHÃ] Erro: {e}")
                    resposta_para_usuario = "❌ Erro ao consultar vencimentos de amanhã."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Vencimentos Essa Semana =====
            elif intent == 'Vencimentos Essa Semana':
                from datetime import datetime, timedelta
                from zoneinfo import ZoneInfo
                from app.services.finance_service import get_vencimentos_periodo, format_vencimentos_message

                try:
                    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
                    hoje = datetime.now(TIMEZONE_BR).date()
                    fim_semana = hoje + timedelta(days=7)

                    # Buscar vencimentos dos próximos 7 dias
                    vencimentos = get_vencimentos_periodo(
                        conn=conn,
                        usuario_id=usuario_id,
                        data_inicio=hoje,
                        data_fim=fim_semana
                    )

                    # Formatar resposta
                    resposta_para_usuario = format_vencimentos_message(
                        vencimentos=vencimentos,
                        periodo="NOS PRÓXIMOS 7 DIAS",
                        data_referencia=hoje
                    )

                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                except Exception as e:
                    print(f"[VENCIMENTOS-SEMANA] Erro: {e}")
                    resposta_para_usuario = "❌ Erro ao consultar vencimentos da semana."
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Transferência =====
            elif intent == 'Transferência':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                transf_data = gemini_service.extract_transfer_details(texto_msg, contas_list, usuario_id)
                valor_dec = float(transf_data.get('valor_decimal', 0))
                nome_origem = transf_data.get('conta_origem')
                nome_destino = transf_data.get('conta_destino')
                
                if not valor_dec or not nome_origem or not nome_destino:
                    raise Exception("Gemini não conseguiu extrair os dados da transferência (valor, origem, destino).")

                conta_id_origem = finance_service.get_account_by_name(conn, usuario_id, nome_origem)
                conta_id_destino = finance_service.get_account_by_name(conn, usuario_id, nome_destino)
                
                if not conta_id_origem or not conta_id_destino:
                    raise Exception(f"Não foi possível encontrar as contas ({nome_origem} -> {nome_destino}).")
                
                nome_orig, nome_dest = finance_service.create_transfer_pair(
                    conn, usuario_id, conta_id_origem, conta_id_destino, valor_dec, data_hoje
                )

                conn.commit()

                valor_fmt = formatar_moeda(valor_dec)
                resposta_para_usuario = f"✅ Transferência salva!\n\nValor: {valor_fmt}\nDe: {nome_orig}\nPara: {nome_dest}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Pagamento Fatura =====
            elif intent == 'Pagamento Fatura':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                fatura_data = gemini_service.extract_fatura_payment_details(texto_msg, contas_list, usuario_id)

                # Validar se conseguiu extrair os dados necessários
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

            #==== INTENÇÃO: Consulta Valor Fatura =====
            elif intent == 'Consulta Valor Fatura':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                fatura_query = gemini_service.extract_fatura_query(texto_msg, contas_list, usuario_id)
                nome_cartao = fatura_query.get('conta_cartao')

                conta_id_cartao = None
                if nome_cartao:
                    conta_id_cartao = finance_service.get_account_by_name(conn, usuario_id, nome_cartao)
                    if not conta_id_cartao:
                        resposta_para_usuario = f"🤔 Não encontrei um cartão chamado '{nome_cartao}'."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # Buscar valor(es) da(s) fatura(s)
                faturas = finance_service.get_fatura_valor(conn, usuario_id, conta_id_cartao)

                if not faturas:
                    if nome_cartao:
                        resposta_para_usuario = f"✅ Você não tem faturas em aberto no cartão '{nome_cartao}'! 🎉"
                    else:
                        resposta_para_usuario = "✅ Você não tem nenhuma fatura em aberto! 🎉"
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                # Formatar resposta
                if len(faturas) == 1:
                    fatura = faturas[0]
                    data_venc = fatura['data_vencimento'].strftime('%d/%m/%Y')
                    resposta_para_usuario = f"💳 *Fatura {fatura['nome_cartao']}*\n\n"
                    resposta_para_usuario += f"💰 Valor atual: *{formatar_moeda(fatura['valor_fatura'])}*\n"
                    resposta_para_usuario += f"📅 Vencimento: {data_venc}\n"
                    resposta_para_usuario += f"📊 Status: {fatura['status']}"
                else:
                    resposta_para_usuario = "💳 *Suas Faturas em Aberto:*\n\n"
                    total_geral = 0
                    for fatura in faturas:
                        data_venc = fatura['data_vencimento'].strftime('%d/%m')
                        resposta_para_usuario += f"🔹 *{fatura['nome_cartao']}*\n"
                        resposta_para_usuario += f"   💰 {formatar_moeda(fatura['valor_fatura'])} (Venc: {data_venc})\n\n"
                        total_geral += fatura['valor_fatura']

                    resposta_para_usuario += f"💵 *Total Geral:* {formatar_moeda(total_geral)}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #=== INTENÇÃO: Consulta Categoria Específica =====
            elif intent == 'Consulta Categoria Específica':
                cat_data = gemini_service.extract_category_query(texto_msg, usuario_id)
                nome_categoria_consulta = cat_data.get('nome_categoria')
                if not nome_categoria_consulta:
                    raise Exception("Gemini não conseguiu extrair o nome da categoria.")

                valor_gasto = finance_service.get_category_spending(conn, usuario_id, nome_categoria_consulta)

                resposta_para_usuario = f"ℹ️ *Consulta de Categoria (Este Mês)*\n\n"
                resposta_para_usuario += f"Você gastou *{formatar_moeda(valor_gasto)}* com '{nome_categoria_consulta}'."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Criar Evento ====
            elif intent == 'Criar Evento':
                print(f"[WHATSAPP] Intenção de Criar Evento detectada")

                event_data = gemini_service.extract_event_creation_details(texto_msg, usuario_id)
                
                titulo = event_data.get('titulo')
                data_str = event_data.get('data')
                hora_inicio = event_data.get('hora_inicio')
                hora_fim = event_data.get('hora_fim')
                descricao = event_data.get('descricao')
                localizacao = event_data.get('localizacao')
                
                if not titulo or not data_str:
                    return jsonify({
                        "status": "sucesso",
                        "resposta": "❌ Não consegui identificar o título ou data do evento. Tente algo como: 'Criar evento Academia amanhã às 7h'"
                    }), 200
                
                # Processar data (usando timezone do Brasil)
                TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
                hoje_br = datetime.now(TIMEZONE_BR).date()

                if data_str == 'hoje':
                    data_evento = hoje_br
                elif data_str == 'amanha':
                    data_evento = hoje_br + timedelta(days=1)
                else:
                    try:
                        data_evento = date.fromisoformat(data_str)
                    except:
                        return jsonify({
                            "status": "sucesso",
                            "resposta": f"❌ Data inválida: {data_str}"
                        }), 200

                # Preparar dados do evento para confirmação
                event_data = {
                    "usuario_id": usuario_id,
                    "titulo": titulo,
                    "data_evento": data_evento.isoformat(),  # Salvar como string ISO
                    "hora_inicio": hora_inicio,
                    "hora_fim": hora_fim,
                    "descricao": descricao,
                    "localizacao": localizacao
                }

                # Criar evento pendente no Redis
                event_id = EventConfirmationService.create_pending_event(numero_limpo, event_data)

                if event_id:
                    # Formatar mensagem de confirmação
                    resposta_para_usuario = EventConfirmationService.format_confirmation_message(event_data)
                else:
                    resposta_para_usuario = "❌ Erro ao processar evento. Tente novamente."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
            
            #==== INTENÇÃO: Deletar Evento ====
            elif intent == 'Deletar Evento':
                print(f"[WHATSAPP] Intenção de Deletar Evento detectada")

                delete_data = gemini_service.extract_event_deletion_query(texto_msg, usuario_id)
                
                titulo_busca = delete_data.get('titulo_busca')
                quando = delete_data.get('quando')
                
                if not titulo_busca:
                    return jsonify({
                        "status": "sucesso",
                        "resposta": "❌ Não consegui identificar qual evento deletar. Tente algo como: 'Deletar academia de hoje'"
                    }), 200
                
                # Buscar eventos
                eventos_encontrados = CalendarManagementService.find_events_by_title(
                    usuario_id, titulo_busca, max_results=5
                )
                
                # Filtrar por quando se fornecido (usando timezone do Brasil)
                if quando:
                    TIMEZONE_BR = ZoneInfo("America/Sao_Paulo")
                    hoje_br = datetime.now(TIMEZONE_BR).date()
                    data_alvo = hoje_br if quando == 'hoje' else hoje_br + timedelta(days=1)
                    eventos_encontrados = [
                        e for e in eventos_encontrados 
                        if date.fromisoformat(e['start'].split('T')[0]) == data_alvo
                    ]
                
                if not eventos_encontrados:
                    resposta_para_usuario = f"🤔 Não encontrei eventos com '{titulo_busca}'"
                    if quando:
                        resposta_para_usuario += f" para {quando}"
                    resposta_para_usuario += ".\n\nTente buscar com outras palavras."
                    
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
                
                if len(eventos_encontrados) == 1:
                    # Deletar automaticamente
                    evento = eventos_encontrados[0]
                    sucesso, mensagem = CalendarManagementService.delete_event(
                        usuario_id,
                        evento['id'],
                        evento['calendar_id']
                    )
                    
                    resposta_para_usuario = f"✅ {mensagem}" if sucesso else f"❌ {mensagem}"
                else:
                    # Múltiplos eventos encontrados, pedir confirmação
                    resposta_para_usuario = f"📋 Encontrei {len(eventos_encontrados)} eventos:\n\n"
                    
                    for idx, evento in enumerate(eventos_encontrados, 1):
                        data_evento = datetime.fromisoformat(evento['start']).strftime('%d/%m às %H:%M') if 'T' in evento['start'] else date.fromisoformat(evento['start']).strftime('%d/%m')
                        resposta_para_usuario += f"{idx}. *{evento['summary']}*\n"
                        resposta_para_usuario += f"   📅 {data_evento}\n"
                        resposta_para_usuario += f"   📂 {evento['calendar_name']}\n"
                        resposta_para_usuario += f"   _ID: {evento['id']}_\n\n"
                    
                    resposta_para_usuario += "Para deletar um específico, envie:\n"
                    resposta_para_usuario += f"'Deletar evento {eventos_encontrados[0]['id']}'"
                
                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
            
            #==== INTENÇÃO: Consulta Agenda com Filtro de Horário ====
            elif intent == 'Consultar Agenda':
                print(f"[WHATSAPP] Intenção de Consulta Agenda detectada")

                # Importar serviço
                from app.services.calendar_query_service import CalendarQueryService

                # Extrair período
                calendar_data = gemini_service.extract_calendar_query(texto_msg, usuario_id)
                period_type = calendar_data.get('period_type', 'hoje')

                # NOVO: Extrair filtro de horário
                time_data = gemini_service.extract_time_filter_query(texto_msg, usuario_id)
                time_filter = time_data.get('time_filter')

                if time_filter:
                    print(f"[WHATSAPP] Filtro de horário: {time_filter}")
                    resposta_para_usuario = CalendarQueryService.query_agenda_with_time_filter(
                        usuario_id, period_type, time_filter
                    )
                else:
                    # Consulta normal sem filtro
                    resposta_para_usuario = CalendarQueryService.query_agenda(usuario_id, period_type)
                
                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Horários Livres ====
            elif intent == 'Horários Livres':
                print(f"[WHATSAPP] Intenção de Horários Livres detectada")

                # Importar services
                from app.services.free_time_finder_service import FreeTimeFinderService

                # Extrair período e contexto
                free_time_data = gemini_service.extract_free_time_query(texto_msg, usuario_id)
                period_type = free_time_data.get('period_type', 'hoje')
                duracao_minutos = free_time_data.get('duracao_minutos', 60)
                contexto = free_time_data.get('contexto')

                print(f"[WHATSAPP] Buscando horários livres: {period_type}, duração: {duracao_minutos}min")

                # Buscar horários livres
                result = FreeTimeFinderService.find_free_slots(
                    db_engine, usuario_id, period_type, duracao_minutos
                )

                # Formatar mensagem
                resposta_para_usuario = FreeTimeFinderService.format_free_slots_message(result, contexto)

                # BONUS: Sugestão da IA (se houver contexto)
                if contexto and result.get("slots_livres"):
                    sugestao_ai = FreeTimeFinderService.suggest_best_slot_with_ai(
                        result, contexto, result.get("insights_usuario", "")
                    )
                    if sugestao_ai:
                        resposta_para_usuario += f"\n\n{sugestao_ai}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Configurar Notificações ====
            elif intent == 'Configurar Notificações':
                print(f"[WHATSAPP] Intenção de Configurar Notificações detectada")

                # Verificar se é sobre RESUMO MATINAL especificamente
                texto_lower = texto_msg.lower()
                is_resumo_matinal = any(kw in texto_lower for kw in ['resumo', 'matinal', 'briefing', 'preparação do dia'])

                if is_resumo_matinal:
                    # HANDLER ESPECÍFICO PARA RESUMO MATINAL
                    print(f"[WHATSAPP] Configuração de Resumo Matinal detectada")

                    # Detectar ação (ativar/desativar/configurar)
                    if any(kw in texto_lower for kw in ['ativar', 'ligar', 'ativa', 'ative']):
                        acao = 'ativar'
                    elif any(kw in texto_lower for kw in ['desativar', 'desligar', 'desative']):
                        acao = 'desativar'
                    else:
                        acao = 'configurar'

                    # Extrair horário (se houver)
                    import re
                    hora_match = re.search(r'(\d{1,2})[h:](\d{2})?', texto_msg)
                    hora = None

                    if hora_match:
                        hora_h = int(hora_match.group(1))
                        hora_m = int(hora_match.group(2)) if hora_match.group(2) else 0
                        hora = f"{hora_h:02d}:{hora_m:02d}"

                    # Executar ação
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

                # HANDLER ESPECÍFICO PARA CHECK-IN NOTURNO
                is_checkin_noturno = any(kw in texto_lower for kw in ['check-in', 'checkin', 'check in', 'noturno', 'contas pendentes'])

                if is_checkin_noturno:
                    print(f"[WHATSAPP] Configuração de Check-in Noturno detectada")

                    # Detectar ação (ativar/desativar/configurar)
                    if any(kw in texto_lower for kw in ['ativar', 'ligar', 'ativa', 'ative']):
                        acao = 'ativar'
                    elif any(kw in texto_lower for kw in ['desativar', 'desligar', 'desative', 'off']):
                        acao = 'desativar'
                    else:
                        acao = 'configurar'

                    # Extrair horário (se houver)
                    import re
                    hora_match = re.search(r'(\d{1,2})[h:](\d{2})?', texto_msg)
                    hora = None

                    if hora_match:
                        hora_h = int(hora_match.group(1))
                        hora_m = int(hora_match.group(2)) if hora_match.group(2) else 0
                        hora = f"{hora_h:02d}:{hora_m:02d}"

                    # Executar ação
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

                # Se NÃO for resumo matinal nem check-in, processar outras notificações (CÓDIGO EXISTENTE)
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

            #==== INTENÇÃO: Configurar Localização ====
            elif intent == 'Configurar Localização':
                print(f"[WHATSAPP] Intenção de Configurar Localização detectada")

                from app.services.gemini_service import extract_location_config
                from app.services.location_service import LocationService

                try:
                    # Extrair cidade e estado com Gemini
                    location_data = extract_location_config(texto_msg, usuario_id)
                    cidade = location_data.get('cidade')
                    estado = location_data.get('estado')

                    if not cidade:
                        resposta_para_usuario = ("❌ Não consegui identificar a cidade.\n\n"
                                                "Por favor, envie no formato:\n"
                                                '"Configurar localização: São Paulo, SP"')
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Atualizar no banco
                    sucesso, mensagem = LocationService.update_user_location(
                        usuario_id,
                        cidade,
                        estado
                    )

                    if sucesso:
                        resposta_para_usuario = f"✅ {mensagem}\n\n"
                        resposta_para_usuario += "Agora você receberá informações de clima nos resumos matinais!"
                    else:
                        resposta_para_usuario = f"❌ {mensagem}"

                except Exception as e:
                    print(f"[WHATSAPP] Erro ao configurar localização: {e}")
                    resposta_para_usuario = ("❌ Erro ao configurar localização.\n\n"
                                            "Tente: 'Configurar localização: São Paulo, SP'")

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Análise Inteligente ====
            elif intent == 'Análise Inteligente':
                print(f"[WHATSAPP] Intenção de Análise Inteligente detectada")

                from app.services.analytics_service import generate_ai_insights

                try:
                    # Gerar insights com IA
                    insights = generate_ai_insights(usuario_id)
                    resposta_para_usuario = f"📊 *Análise Inteligente de Gastos*\n\n{insights}"

                except Exception as e:
                    print(f"[ANALYTICS] Erro ao gerar insights: {e}")
                    resposta_para_usuario = f"❌ Não consegui gerar a análise no momento. Erro: {str(e)}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Comparação Mensal ====
            elif intent == 'Comparação Mensal':
                print(f"[WHATSAPP] Intenção de Comparação Mensal detectada")

                from app.services.analytics_service import get_monthly_comparison

                try:
                    # Comparar mês atual com anterior
                    comparacao = get_monthly_comparison(usuario_id)
                    resposta_para_usuario = comparacao

                except Exception as e:
                    print(f"[ANALYTICS] Erro ao comparar meses: {e}")
                    resposta_para_usuario = f"❌ Não consegui fazer a comparação. Erro: {str(e)}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Previsão de Gastos ====
            elif intent == 'Previsão de Gastos':
                print(f"[WHATSAPP] Intenção de Previsão de Gastos detectada")

                from app.services.forecast_service import generate_forecast_insights

                try:
                    # Gerar previsão de gastos futuros
                    previsao = generate_forecast_insights(usuario_id)
                    resposta_para_usuario = f"📈 *Previsão de Gastos*\n\n{previsao}"

                except Exception as e:
                    print(f"[FORECAST] Erro ao gerar previsão: {e}")
                    resposta_para_usuario = f"❌ Não consegui gerar a previsão. Erro: {str(e)}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Gráfico de Gastos ====
            elif intent == 'Gráfico de Gastos':
                print(f"[WHATSAPP] Intenção de Gráfico de Gastos detectada")

                from app.services import chart_service

                try:
                    # Extrair tipo de gráfico solicitado
                    chart_info = gemini_service.extract_chart_type(texto_msg, usuario_id)
                    tipo_grafico = chart_info.get('tipo_grafico', 'pizza')

                    print(f"[CHART] Gerando gráfico tipo: {tipo_grafico}")

                    # Gerar gráfico apropriado
                    chart_bytes = None
                    caption = ""

                    if tipo_grafico == 'pizza':
                        periodo_dias = chart_info.get('periodo_dias', 30)
                        chart_bytes = chart_service.generate_pie_chart(usuario_id, periodo_dias)
                        caption = f"📊 Gastos por Categoria - Últimos {periodo_dias} dias"

                    elif tipo_grafico == 'barras':
                        num_meses = chart_info.get('num_meses', 6)
                        chart_bytes = chart_service.generate_bar_chart(usuario_id, num_meses)
                        caption = f"📊 Evolução Mensal - Últimos {num_meses} meses"

                    elif tipo_grafico == 'linha':
                        num_meses = chart_info.get('num_meses', 6)
                        chart_bytes = chart_service.generate_line_chart(usuario_id, num_meses)
                        caption = f"📈 Evolução do Saldo - Últimos {num_meses} meses"

                    # Verificar se gráfico foi gerado
                    if chart_bytes is None:
                        resposta_para_usuario = "❌ Não há dados suficientes para gerar o gráfico no período solicitado."
                        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                    # Enviar imagem via WhatsApp
                    sucesso = notification_service.enviar_imagem_whatsapp_bytes(
                        numero_remetente,
                        chart_bytes,
                        caption,
                        BOT_WHATSAPP_URL,
                        API_SECRET_KEY
                    )

                    if sucesso:
                        resposta_para_usuario = f"✅ {caption}"
                    else:
                        resposta_para_usuario = "❌ Não consegui enviar o gráfico. Tente novamente mais tarde."

                except Exception as e:
                    print(f"[CHART] Erro ao gerar gráfico: {e}")
                    import traceback
                    traceback.print_exc()
                    resposta_para_usuario = f"❌ Não consegui gerar o gráfico. Erro: {str(e)}"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Solicitar API Key ====
            elif intent == 'Solicitar API Key':
                print(f"[WHATSAPP] Intenção de Solicitar API Key detectada")

                try:
                    # Buscar API key do usuário
                    sql_api_key = text("SELECT api_key_automate, nome FROM Usuarios WHERE id = :uid")
                    result = conn.execute(sql_api_key, {"uid": usuario_id}).fetchone()

                    if result and result[0]:
                        api_key = result[0]
                        nome_usuario = result[1]

                        resposta_para_usuario = f"🔑 *Sua API Key*\n\n"
                        resposta_para_usuario += f"Olá {nome_usuario}!\n\n"
                        resposta_para_usuario += f"Sua chave de acesso:\n"
                        resposta_para_usuario += f"`{api_key}`\n\n"
                        resposta_para_usuario += f"⚠️ *Importante:*\n"
                        resposta_para_usuario += f"• Não compartilhe esta chave com ninguém\n"
                        resposta_para_usuario += f"• Use-a para configurar automações no iPhone\n"
                        resposta_para_usuario += f"• Esta chave dá acesso total à sua conta\n\n"
                        resposta_para_usuario += f"📱 *Para usar no iPhone:*\n"
                        resposta_para_usuario += f"1. Copie a chave acima\n"
                        resposta_para_usuario += f"2. No atalho, cole no campo `user_api_key`\n"
                        resposta_para_usuario += f"3. Teste enviando um gasto!\n\n"
                        resposta_para_usuario += f"💡 *Endpoint:*\n"
                        resposta_para_usuario += f"`POST /api/transacao`"
                    else:
                        resposta_para_usuario = "❌ Não encontrei sua API Key. Entre em contato com o suporte."

                except Exception as e:
                    print(f"[API-KEY] Erro ao buscar API Key: {e}")
                    resposta_para_usuario = f"❌ Erro ao buscar sua API Key. Tente novamente mais tarde."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Configurar Relatório Mensal ====
            elif intent == 'Configurar Relatório Mensal':
                print(f"[WHATSAPP] Intenção de Configurar Relatório Mensal detectada")

                from app.services.monthly_report_config_service import (
                    get_or_create_config,
                    update_config,
                    ativar_config,
                    desativar_config
                )

                try:
                    # Extrair configurações da mensagem
                    config_info = gemini_service.extract_monthly_report_config(texto_msg, usuario_id)
                    acao = config_info.get('acao')
                    momento_envio = config_info.get('momento_envio')
                    hora_envio = config_info.get('hora_envio')

                    print(f"[MONTHLY-REPORT-CONFIG] Ação: {acao}, Momento: {momento_envio}, Hora: {hora_envio}")

                    # Buscar configuração atual
                    config_atual = get_or_create_config(usuario_id)

                    if acao == 'consultar':
                        # Mostrar configuração atual
                        status = "✅ Ativo" if config_atual['ativo'] else "❌ Desativado"
                        momento = "Início do mês (dia 1)" if config_atual['momento_envio'] == 'INICIO_MES' else "Fim do mês (último dia)"
                        hora = config_atual['hora_envio'].strftime('%H:%M') if config_atual['hora_envio'] else "08:00"

                        resposta_para_usuario = "📊 *CONFIGURAÇÃO DO RELATÓRIO MENSAL*\n\n"
                        resposta_para_usuario += f"Status: {status}\n"
                        resposta_para_usuario += f"Momento: {momento}\n"
                        resposta_para_usuario += f"Horário: {hora}\n\n"
                        resposta_para_usuario += "_Para alterar, envie: 'configurar relatório mensal no início do mês às 10h'_"

                    elif acao == 'desativar':
                        # Desativar relatório
                        desativar_config(usuario_id)
                        resposta_para_usuario = "✅ Relatório mensal desativado com sucesso!\n\n"
                        resposta_para_usuario += "_Para reativar, envie: 'ativar relatório mensal'_"

                    elif acao == 'ativar':
                        # Ativar relatório (aplicar novas configurações se fornecidas)
                        params = {'ativo': True}
                        if momento_envio:
                            params['momento_envio'] = momento_envio
                        if hora_envio:
                            params['hora_envio'] = hora_envio

                        config_nova = update_config(usuario_id, **params)

                        momento_texto = "início do mês (dia 1)" if config_nova['momento_envio'] == 'INICIO_MES' else "fim do mês (último dia)"
                        hora_texto = config_nova['hora_envio'].strftime('%H:%M')

                        resposta_para_usuario = "✅ *Relatório mensal ativado!*\n\n"
                        resposta_para_usuario += f"📅 Momento: {momento_texto}\n"
                        resposta_para_usuario += f"🕐 Horário: {hora_texto}\n\n"
                        resposta_para_usuario += "📊 *O que você vai receber:*\n"
                        resposta_para_usuario += "• Gastos totais do mês\n"
                        resposta_para_usuario += "• Top 5 categorias\n"
                        resposta_para_usuario += "• Comparação com mês anterior\n"
                        resposta_para_usuario += "• Status dos potes de gastos\n"
                        resposta_para_usuario += "• Contas pagas vs pendentes\n"
                        resposta_para_usuario += "• Gráfico de pizza com categorias\n\n"
                        resposta_para_usuario += "_Você receberá automaticamente no horário configurado!_"

                    elif acao == 'configurar':
                        # Atualizar configurações
                        params = {}
                        if momento_envio:
                            params['momento_envio'] = momento_envio
                        if hora_envio:
                            params['hora_envio'] = hora_envio

                        if not params:
                            resposta_para_usuario = "❌ Não entendi o que você quer configurar.\n\n"
                            resposta_para_usuario += "Exemplos:\n"
                            resposta_para_usuario += "• 'quero receber no início do mês às 8h'\n"
                            resposta_para_usuario += "• 'mudar hora do relatório para 14:00'\n"
                            resposta_para_usuario += "• 'receber no fim do mês'"
                        else:
                            config_nova = update_config(usuario_id, **params)

                            momento_texto = "início do mês (dia 1)" if config_nova['momento_envio'] == 'INICIO_MES' else "fim do mês (último dia)"
                            hora_texto = config_nova['hora_envio'].strftime('%H:%M')

                            resposta_para_usuario = "✅ *Configuração atualizada!*\n\n"
                            resposta_para_usuario += f"📅 Momento: {momento_texto}\n"
                            resposta_para_usuario += f"🕐 Horário: {hora_texto}\n\n"
                            resposta_para_usuario += "O relatório será enviado automaticamente no horário configurado."

                    else:
                        resposta_para_usuario = "❌ Não entendi a ação desejada.\n\n"
                        resposta_para_usuario += "Exemplos:\n"
                        resposta_para_usuario += "• 'ativar relatório mensal'\n"
                        resposta_para_usuario += "• 'desativar relatório mensal'\n"
                        resposta_para_usuario += "• 'configurar relatório mensal às 10h'\n"
                        resposta_para_usuario += "• 'como está configurado meu relatório?'"

                except ValueError as ve:
                    print(f"[MONTHLY-REPORT-CONFIG] Erro de validação: {ve}")
                    resposta_para_usuario = f"❌ {str(ve)}\n\n"
                    resposta_para_usuario += "Exemplos válidos:\n"
                    resposta_para_usuario += "• Momento: 'início do mês' ou 'fim do mês'\n"
                    resposta_para_usuario += "• Horário: '08:00' ou '14:30'"

                except Exception as e:
                    print(f"[MONTHLY-REPORT-CONFIG] Erro: {e}")
                    import traceback
                    traceback.print_exc()
                    resposta_para_usuario = f"❌ Erro ao configurar relatório mensal. Tente novamente mais tarde."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Configurar Endereço ====
            elif intent == 'Configurar Endereço':
                print(f"[WHATSAPP] Intenção de Configurar Endereço detectada")

                from app.services.user_address_service import UserAddressService

                try:
                    # Extrair dados do endereço
                    addr_data = gemini_service.extract_address_config(texto_msg, usuario_id)
                    label = addr_data.get('label')
                    endereco = addr_data.get('endereco_completo')

                    if not label or not endereco:
                        resposta_para_usuario = (
                            "❌ Não entendi o endereço.\n\n"
                            "Use o formato:\n"
                            "*'Configurar endereço casa: Rua X, 123, Bairro, Cidade-SP'*\n\n"
                            "Tipos de endereço:\n"
                            "• Casa\n"
                            "• Trabalho\n"
                            "• Outro"
                        )
                    else:
                        # Salvar endereço
                        sucesso, mensagem = UserAddressService.save_favorite_address(
                            usuario_id, label, endereco
                        )
                        resposta_para_usuario = mensagem

                except Exception as e:
                    print(f"[CONFIG-ADDRESS] Erro: {e}")
                    import traceback
                    traceback.print_exc()
                    resposta_para_usuario = "❌ Erro ao configurar endereço. Tente novamente."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Listar Endereços ====
            elif intent == 'Listar Endereços':
                print(f"[WHATSAPP] Intenção de Listar Endereços detectada")

                from app.services.user_address_service import UserAddressService

                try:
                    mensagem = UserAddressService.format_address_list_message(usuario_id)
                    resposta_para_usuario = mensagem

                except Exception as e:
                    print(f"[LIST-ADDRESS] Erro: {e}")
                    resposta_para_usuario = "❌ Erro ao listar endereços. Tente novamente."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            #==== INTENÇÃO: Deletar Endereço ====
            elif intent == 'Deletar Endereço':
                print(f"[WHATSAPP] Intenção de Deletar Endereço detectada")

                from app.services.user_address_service import UserAddressService

                try:
                    # Extrair label do endereço a deletar
                    label_data = gemini_service.extract_address_label_from_deletion(texto_msg, usuario_id)
                    label = label_data.get('label', 'outro')

                    # Deletar endereço
                    sucesso, mensagem = UserAddressService.delete_address(usuario_id, label)
                    resposta_para_usuario = mensagem

                except Exception as e:
                    print(f"[DELETE-ADDRESS] Erro: {e}")
                    resposta_para_usuario = "❌ Erro ao deletar endereço. Tente novamente."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            elif intent == "Menu de Ajuda":
                resposta_para_usuario = """📚 *MENU DE FUNCIONALIDADES* 📚

*💰 GESTÃO FINANCEIRA*
• _"gastei 50 em comida"_ - Registrar despesa
• _"recebi 500"_ - Registrar renda
• _"qual meu saldo?"_ - Consultar saldo das contas
• _"quanto gastei hoje/semana/mês?"_ - Gastos por período
• _"meus potes"_ - Ver limite e gasto dos potes
• _"minhas contas fixas"_ - Listar contas recorrentes
• _"paguei água"_ - Quitar conta fixa
• _"paguei a internet e comprei pizza de 50"_ - Quitar múltiplas (híbrido)
• _"transferir 100 da carteira para banco"_ - Transferência
• _"paguei 500 da fatura do nubank"_ - Pagar fatura
• _"qual valor da fatura?"_ - Consultar valor da fatura
• _"quanto gastei com comida?"_ - Gasto por categoria
• _"quais contas tenho?"_ - Listar contas cadastradas
• _"quanto de reserva?"_ - Reserva de emergência (6x média)
• _"tenho conta que vence hoje?"_ - Vencimentos de hoje
• _"tenho conta que vence amanhã?"_ - Vencimentos de amanhã
• _"contas que vencem essa semana?"_ - Vencimentos dos próximos 7 dias

*📅 CALENDÁRIO & AGENDA*
• _"minha agenda amanhã"_ - Ver compromissos
• _"criar evento academia amanhã 7h"_ - Criar evento
• _"deletar reunião de hoje"_ - Remover evento
• _"quando estou livre amanhã?"_ - Horários disponíveis

*📊 ANÁLISES & RELATÓRIOS*
• _"analisar meus gastos"_ - Análise inteligente com IA
• _"comparar com mês anterior"_ - Comparação mensal
• _"quanto vou gastar este mês"_ - Previsão de gastos
• _"gráfico de gastos"_ - Gerar gráfico visual

*⚙️ CONFIGURAÇÕES*
• _"ativar resumo matinal"_ - Configurar notificações
• _"configurar localização São Paulo"_ - Definir cidade
• _"configurar relatório mensal"_ - Agendar relatório
• _"configurar endereço casa"_ - Adicionar endereço
• _"meus endereços"_ - Listar endereços
• _"remover endereço casa"_ - Deletar endereço

*🔑 ACESSO*
• _"minha api key"_ - Exibir chave de integração

💡 *Dica:* Use linguagem natural! Eu entendo suas mensagens."""

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            else:
                return jsonify({"status": "sucesso", "resposta": "🤔 Não entendi. Tente 'gastei 50' ou 'o que você pode fazer?'."}), 200

    except Exception as e:
        print(f"[WHATSAPP] Erro: {e}")

        # Verificar se é erro de quota do Gemini (429)
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
    

@webhooks_bp.route('/webhook-sms-payment', methods=['POST'])
def handle_sms_payment():
    '''
    Endpoint específico para pagamentos via Sms (iPhone Automation).
    
    Payload esperado:
    {
        "user_api_key": "...",
        "descricao_pagamento": "Conta de Água",
        "valor_pago": 150.50,
        "conta_pagamento": "swile",
        "data_pagamento": "2024-01-15" (opcional, padrão: hoje)
    }
    '''
    try:
        ensure_db_connection()
    except Exception as e:
        return jsonify({
            "status": "erro",
            "resposta": "Banco de dados temporariamente indisponível"
        }), 503    
    
    try:
        data = request.json
        user_api_key = data.get('user_api_key')
        descricao = data.get('descricao_pagamento')
        valor = data.get('valor_pago')
        conta_pagamento = data.get('conta_pagamento')
        data_pag = data.get('data_pagamento')
        
        if not all([user_api_key, descricao, valor, conta_pagamento]):
            return jsonify({"status": "erro", "mensagem": "Dados faltando"}), 400
        
        # Autenticar usuário
        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({"status": "erro", "mensagem": "API key inválida"}), 401
        
        usuario_id, numero_whatsapp = user_info
        
        # Data do pagamento
        if data_pag:
            data_pagamento = date.fromisoformat(data_pag)
        else:
            data_pagamento = date.today()
        
        with db_engine.connect() as conn:
            conn.begin()
            
            # Buscar conta fixa correspondente
            match = FixedBillsService.find_matching_bill(conn, usuario_id, descricao)
            
            if match:
                # Encontrou conta fixa correspondente
                agendamento_id, desc_original, valor_previsto, dia_venc, categoria = match
                
                # Buscar conta "Swile" (ou criar se não existir)
                sql_conta_pagamento = text("SELECT id FROM Contas WHERE usuario_id = :uid AND nome_conta ILIKE '%{conta}%' LIMIT 1")
                conta_id = conn.execute(sql_conta_pagamento, {"uid": usuario_id}).scalar_one_or_none()
                
                if not conta_id:
                    # Usar conta padrão
                    conta_id = None
                
                # Quitar a conta fixa
                transaction_id = FixedBillsService.settle_fixed_bill(
                    conn, usuario_id, agendamento_id, valor, data_pagamento,
                    conta_pagamento_id=conta_id,
                    observacao="Pago via {conta}"
                )
                
                conn.commit()
                
                # Notificar usuário
                mensagem = FixedBillsService.mark_bill_as_paid_response(
                    desc_original, valor, categoria
                )
                
                notification_service.enviar_notificacao_whatsapp(
                    numero_whatsapp, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                )
                
                return jsonify({
                    "status": "sucesso",
                    "mensagem": "Conta fixa quitada com sucesso!",
                    "conta_identificada": desc_original,
                    "transaction_id": transaction_id
                }), 200
                
            else:
                # Não encontrou conta fixa, registrar como despesa normal
                # (usar fluxo normal de categorização)
                
                # Extrair tipo e categoria com IA
                trans_data = gemini_service.extract_transaction_details(
                    f"Paguei {valor} de {descricao}",
                    'Despesa',
                    usuario_id
                )
                
                cats = finance_service.get_user_categories(conn, usuario_id, 'Despesa')
                id_outros = finance_service.get_fallback_category_id(conn, 'Despesa')
                
                id_categoria = gemini_service.categorize_transaction(
                    cats, descricao, 'Despesa', id_outros, usuario_id
                )
                
                conta_id = finance_service.get_account_by_name(conn, usuario_id, 'Carteira', fallback=True)
                
                finance_service.create_transaction(
                    conn, usuario_id, conta_id, id_categoria, None,
                    f"{descricao} ({conta_pagamento})", float(valor) * -1, 'Despesa', data_pagamento
                )
                
                conn.commit()
                
                mensagem = (
                    f"✅ Pagamento Registrado!\n\n"
                    f"📝 {descricao}\n"
                    f"💰 {formatar_moeda(valor)}\n"
                    f"💳 {conta_pagamento}\n\n"
                    f"_Não encontrei uma conta fixa correspondente, então registrei como despesa avulsa._"
                )
                
                notification_service.enviar_notificacao_whatsapp(
                    numero_whatsapp, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                )
                
                return jsonify({
                    "status": "sucesso",
                    "mensagem": "Despesa registrada",
                    "conta_identificada": None
                }), 200
        
    except Exception as e:
        print(f"[{conta_pagamento}] Erro: {e}")
        return jsonify({"status": "erro", "mensagem": str(e)}), 500
    
@webhooks_bp.route('/connect-calendar/<int:usuario_id>', methods=['GET'])
def connect_calendar(usuario_id):
    """
    Endpoint para iniciar processo de conexão OAuth2.
    Usuário acessa via link enviado pelo WhatsApp.
    """
    try:
        ensure_db_connection()
    except Exception as e:
        return jsonify({
            "status": "erro",
            "resposta": "Banco de dados temporariamente indisponível"
        }), 503
        
    try:
        # Verificar se usuário existe
        with db_engine.connect() as conn:
            sql = text("SELECT nome FROM Usuarios WHERE id = :uid")
            usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()
        
        if not usuario:
            return "❌ Usuário não encontrado", 404
        
        # Verificar se já está conectado
        if GoogleCalendarOAuthService.is_user_connected(usuario_id):
            return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Já Conectado</title>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    body { 
                        font-family: Arial, sans-serif; 
                        text-align: center; 
                        padding: 50px; 
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                        color: white;
                    }
                    .container {
                        background: white;
                        color: #333;
                        padding: 40px;
                        border-radius: 15px;
                        max-width: 500px;
                        margin: 0 auto;
                        box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    }
                    .icon { font-size: 64px; margin-bottom: 20px; }
                    h1 { color: #667eea; margin-bottom: 20px; }
                    .btn {
                        background: #667eea;
                        color: white;
                        padding: 15px 30px;
                        border-radius: 8px;
                        text-decoration: none;
                        display: inline-block;
                        margin-top: 20px;
                        font-weight: bold;
                    }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">✅</div>
                    <h1>Você já está conectado!</h1>
                    <p>Seu Google Calendar já está integrado.</p>
                    <p>Volte para o WhatsApp e pergunte:</p>
                    <p><strong>"Tenho compromisso hoje?"</strong></p>
                </div>
            </body>
            </html>
            """, 200
        
        # Gerar URL de autorização
        auth_url = GoogleCalendarOAuthService.get_authorization_url(usuario_id)
        
        # Redirecionar para Google
        return redirect(auth_url)
    
    except Exception as e:
        print(f"[OAUTH] Erro ao conectar: {e}")
        return f"❌ Erro ao conectar: {str(e)}", 500


@webhooks_bp.route('/oauth2callback', methods=['GET'])
def oauth2callback():
    """
    Callback do Google após autorização.
    Google redireciona usuário para cá com código de autorização.
    """
    try:
        # Pegar parâmetros
        code = request.args.get('code')
        state = request.args.get('state')
        error = request.args.get('error')
        
        # Verificar se usuário negou
        if error:
            return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>Autorização Negada</title>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>
                    body { 
                        font-family: Arial, sans-serif; 
                        text-align: center; 
                        padding: 50px; 
                        background: linear-gradient(135deg, #f093fb 0%, #f5576c 100%);
                        color: white;
                    }
                    .container {
                        background: white;
                        color: #333;
                        padding: 40px;
                        border-radius: 15px;
                        max-width: 500px;
                        margin: 0 auto;
                        box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    }
                    .icon { font-size: 64px; margin-bottom: 20px; }
                    h1 { color: #f5576c; margin-bottom: 20px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">❌</div>
                    <h1>Autorização Negada</h1>
                    <p>Você negou o acesso ao Google Calendar.</p>
                    <p>Para usar esta funcionalidade, você precisa autorizar o acesso.</p>
                    <p>Tente novamente quando quiser! 👋</p>
                </div>
            </body>
            </html>
            """, 200
        
        if not code or not state:
            return "❌ Erro: código ou state faltando", 400
        
        # Trocar código por tokens
        usuario_id = GoogleCalendarOAuthService.exchange_code_for_tokens(code, state)
        
        # Buscar dados do usuário
        with db_engine.connect() as conn:
            sql = text("SELECT nome, numero_whatsapp FROM Usuarios WHERE id = :uid")
            usuario = conn.execute(sql, {"uid": usuario_id}).fetchone()
        
        # Enviar notificação no WhatsApp
        if usuario and usuario.numero_whatsapp:
            mensagem = (
                f"✅ *Google Calendar Conectado!*\n\n"
                f"Olá {usuario.nome}! Seu calendário foi conectado com sucesso.\n\n"
                f"Agora você pode perguntar:\n"
                f"• Tenho compromisso hoje?\n"
                f"• Minha agenda amanhã\n"
                f"• Compromissos do final de semana\n\n"
                f"🔒 Seus dados estão seguros via OAuth2!"
            )
            
            notification_service.enviar_notificacao_whatsapp(
                usuario.numero_whatsapp,
                mensagem,
                BOT_WHATSAPP_URL,
                API_SECRET_KEY
            )
        
        # Página de sucesso
        return f"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Conexão Autorizada</title>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <style>
                body {{ 
                    font-family: Arial, sans-serif; 
                    text-align: center; 
                    padding: 50px; 
                    background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                    color: white;
                }}
                .container {{
                    background: white;
                    color: #333;
                    padding: 40px;
                    border-radius: 15px;
                    max-width: 500px;
                    margin: 0 auto;
                    box-shadow: 0 10px 30px rgba(0,0,0,0.3);
                    animation: slideIn 0.5s ease-out;
                }}
                @keyframes slideIn {{
                    from {{ transform: translateY(-50px); opacity: 0; }}
                    to {{ transform: translateY(0); opacity: 1; }}
                }}
                .icon {{ 
                    font-size: 80px; 
                    margin-bottom: 20px;
                    animation: bounce 1s infinite;
                }}
                @keyframes bounce {{
                    0%, 100% {{ transform: translateY(0); }}
                    50% {{ transform: translateY(-10px); }}
                }}
                h1 {{ 
                    color: #667eea; 
                    margin-bottom: 20px;
                    font-size: 28px;
                }}
                .success {{ color: #10b981; font-weight: bold; }}
                .info {{
                    background: #f0f9ff;
                    padding: 20px;
                    border-radius: 10px;
                    margin: 20px 0;
                    border-left: 4px solid #667eea;
                }}
                .commands {{
                    text-align: left;
                    margin: 20px 0;
                }}
                .command {{
                    background: #f9fafb;
                    padding: 10px;
                    margin: 8px 0;
                    border-radius: 5px;
                    border-left: 3px solid #10b981;
                }}
                .footer {{
                    margin-top: 30px;
                    font-size: 14px;
                    color: #6b7280;
                }}
            </style>
        </head>
        <body>
            <div class="container">
                <div class="icon">✅</div>
                <h1 class="success">Google Calendar Conectado!</h1>
                <p>Olá <strong>{usuario.nome if usuario else 'Usuário'}</strong>!</p>
                <p>Seu calendário foi conectado com sucesso.</p>
                
                <div class="info">
                    <h3 style="margin-top: 0; color: #667eea;">📱 Agora você pode:</h3>
                    <div class="commands">
                        <div class="command">• "Tenho compromisso hoje?"</div>
                        <div class="command">• "Minha agenda amanhã"</div>
                        <div class="command">• "Compromissos do final de semana"</div>
                        <div class="command">• "Agenda desta semana"</div>
                    </div>
                </div>
                
                <div class="footer">
                    <p>✅ Você já pode fechar esta janela</p>
                    <p>💬 Volte para o WhatsApp!</p>
                    <p style="margin-top: 20px;">
                        <small>🔒 Conexão segura via OAuth2 oficial do Google</small>
                    </p>
                </div>
            </div>
        </body>
        </html>
        """, 200
    
    except Exception as e:
        print(f"[OAUTH] Erro no callback: {e}")
        return f"""
        <!DOCTYPE html>
        <html>
        <head>
            <title>Erro</title>
            <meta charset="utf-8">
            <style>
                body {{ text-align: center; padding: 50px; font-family: Arial; }}
                .error {{ color: red; }}
            </style>
        </head>
        <body>
            <h1 class="error">❌ Erro</h1>
            <p>Ocorreu um erro ao processar a autorização:</p>
            <p><code>{str(e)}</code></p>
            <p>Tente novamente ou contate o suporte.</p>
        </body>
        </html>
        """, 500


@webhooks_bp.route('/disconnect-calendar/<int:usuario_id>', methods=['POST'])
def disconnect_calendar(usuario_id):
    """
    Permite usuário desconectar Google Calendar.
    Pode ser chamado via WhatsApp ou interface web.
    """
    try:
        # Autenticar (verificar se é o próprio usuário)
        # ... adicionar autenticação se necessário ...

        GoogleCalendarOAuthService.revoke_access(usuario_id)

        return jsonify({
            "status": "sucesso",
            "mensagem": "Google Calendar desconectado com sucesso"
        }), 200

    except Exception as e:
        print(f"[OAUTH] Erro ao desconectar: {e}")
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


# ================================================================
# ENDPOINTS DE RESERVA DE EMERGÊNCIA (Baseados em Agendamentos)
# ================================================================

@webhooks_bp.route('/api/agendamento/<int:agendamento_id>/reserva', methods=['PATCH'])
def toggle_incluir_reserva_agendamento(agendamento_id):
    """
    Altera o flag incluir_na_reserva de um agendamento específico.

    LÓGICA CORRETA:
    - Reserva de emergência = gastos essenciais MENSAIS × 6 meses
    - Este endpoint permite marcar quais contas fixas incluir (água, luz, aluguel, Netflix, etc.)

    Body JSON:
    {
        "incluir": true/false,
        "api_key": "user_api_key"
    }

    Exemplo:
    PATCH https://seu-backend.onrender.com/api/agendamento/123/reserva
    Body: {"incluir": true, "api_key": "abc123"}

    Resposta de sucesso:
    {
        "status": "sucesso",
        "mensagem": "Agendamento 'Netflix' incluído no cálculo de reserva",
        "agendamento": {
            "id": 123,
            "descricao": "Netflix",
            "valor_previsto": 49.90,
            "periodicidade": "MENSAL",
            "incluir_na_reserva": true
        },
        "impacto": {
            "gasto_mensal_anterior": 2500.00,
            "gasto_mensal_novo": 2549.90,
            "reserva_ideal_anterior": 15000.00,
            "reserva_ideal_nova": 15299.40
        }
    }
    """
    try:
        data = request.json
        incluir = data.get('incluir')
        user_api_key = data.get('api_key')

        # Validar campos obrigatórios
        if incluir is None or not user_api_key:
            return jsonify({
                "status": "erro",
                "mensagem": "Campos 'incluir' e 'api_key' são obrigatórios"
            }), 400

        # Autenticar usuário
        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({
                "status": "erro",
                "mensagem": "API key inválida"
            }), 401

        usuario_id, _ = user_info

        with db_engine.connect() as conn:
            conn.begin()

            # Calcular reserva ANTES da mudança
            gasto_anterior, reserva_anterior, _ = finance_service.get_reserva_status(conn, usuario_id)

            # Verificar se agendamento pertence ao usuário
            sql_check = text("""
                SELECT descricao, valor_previsto, periodicidade, tipo_agendamento
                FROM Agendamentos
                WHERE id = :aid AND usuario_id = :uid AND ativo = TRUE
            """)
            agend = conn.execute(sql_check, {
                "aid": agendamento_id,
                "uid": usuario_id
            }).fetchone()

            if not agend:
                return jsonify({
                    "status": "erro",
                    "mensagem": "Agendamento não encontrado ou inativo"
                }), 404

            descricao, valor_previsto, periodicidade, tipo_agend = agend

            # Atualizar flag
            sql_update = text("""
                UPDATE Agendamentos
                SET incluir_na_reserva = :incluir
                WHERE id = :aid AND usuario_id = :uid
            """)
            conn.execute(sql_update, {
                "incluir": incluir,
                "aid": agendamento_id,
                "uid": usuario_id
            })
            conn.commit()

            # Calcular reserva DEPOIS da mudança
            gasto_novo, reserva_nova, _ = finance_service.get_reserva_status(conn, usuario_id)

            status_text = "incluído" if incluir else "excluído"
            return jsonify({
                "status": "sucesso",
                "mensagem": f"Agendamento '{descricao}' {status_text} do cálculo de reserva",
                "agendamento": {
                    "id": agendamento_id,
                    "descricao": descricao,
                    "valor_previsto": float(valor_previsto or 0),
                    "periodicidade": periodicidade,
                    "tipo_agendamento": tipo_agend,
                    "incluir_na_reserva": incluir
                },
                "impacto": {
                    "gasto_mensal_anterior": gasto_anterior,
                    "gasto_mensal_novo": gasto_novo,
                    "reserva_ideal_anterior": reserva_anterior,
                    "reserva_ideal_nova": reserva_nova
                }
            }), 200

    except Exception as e:
        print(f"[RESERVA-TOGGLE] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500


@webhooks_bp.route('/api/agendamentos/reserva', methods=['GET'])
def listar_agendamentos_reserva():
    """
    Lista agendamentos do usuário com filtros para gerenciar a reserva de emergência.

    LÓGICA CORRETA:
    - Lista agendamentos fixos/recorrentes (contas mensais)
    - Mostra quais estão incluídos no cálculo da reserva
    - Exibe o impacto no cálculo da reserva ideal

    Query params:
        - api_key (obrigatório): API key do usuário
        - incluir_na_reserva (opcional): true/false - filtrar por flag
        - periodicidade (opcional): MENSAL, SEMANAL, QUINZENAL, ANUAL
        - categoria (opcional): nome da categoria para filtrar
        - limit (opcional): limite de resultados (padrão: 100)
        - offset (opcional): offset para paginação (padrão: 0)

    Exemplo 1 - Listar apenas agendamentos incluídos na reserva:
    GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&incluir_na_reserva=true

    Exemplo 2 - Listar apenas agendamentos mensais:
    GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&periodicidade=MENSAL

    Exemplo 3 - Listar agendamentos de uma categoria:
    GET https://seu-backend.onrender.com/api/agendamentos/reserva?api_key=abc123&categoria=Internet

    Resposta de sucesso:
    {
        "status": "sucesso",
        "total": 15,
        "agendamentos": [
            {
                "id": 123,
                "descricao": "Aluguel",
                "valor_previsto": 1500.00,
                "periodicidade": "MENSAL",
                "tipo_agendamento": "FIXO",
                "dia_execucao": 5,
                "categoria": "Moradia",
                "incluir_na_reserva": true
            },
            ...
        ],
        "resumo_reserva": {
            "gasto_mensal_essencial": 2500.00,
            "reserva_ideal_6_meses": 15000.00
        }
    }
    """
    try:
        # Autenticar
        user_api_key = request.args.get('api_key')
        if not user_api_key:
            return jsonify({
                "status": "erro",
                "mensagem": "Parâmetro 'api_key' é obrigatório"
            }), 400

        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({
                "status": "erro",
                "mensagem": "API key inválida"
            }), 401

        usuario_id, _ = user_info

        # Parâmetros de filtro
        incluir_na_reserva_param = request.args.get('incluir_na_reserva')
        periodicidade_param = request.args.get('periodicidade')
        categoria_param = request.args.get('categoria')
        limit = int(request.args.get('limit', 100))
        offset = int(request.args.get('offset', 0))

        # Construir query base
        sql_parts = []
        sql_parts.append("""
            SELECT
                a.id,
                a.descricao,
                a.valor_previsto,
                a.periodicidade,
                a.tipo_agendamento,
                a.dia_execucao,
                a.incluir_na_reserva,
                s.nome_sub as categoria,
                m.nome_macro as macro_categoria,
                g.nome_grupo as grupo
            FROM Agendamentos a
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
        """)

        params = {"uid": usuario_id}

        # Filtro por flag incluir_na_reserva
        if incluir_na_reserva_param is not None:
            incluir_bool = incluir_na_reserva_param.lower() == 'true'
            sql_parts.append("AND a.incluir_na_reserva = :incluir")
            params["incluir"] = incluir_bool

        # Filtro por periodicidade
        if periodicidade_param:
            periodicidade_upper = periodicidade_param.upper()
            if periodicidade_upper not in ['DIARIA', 'SEMANAL', 'QUINZENAL', 'MENSAL', 'ANUAL']:
                return jsonify({
                    "status": "erro",
                    "mensagem": "Periodicidade inválida. Use: DIARIA, SEMANAL, QUINZENAL, MENSAL ou ANUAL"
                }), 400
            sql_parts.append("AND a.periodicidade = :periodicidade")
            params["periodicidade"] = periodicidade_upper

        # Filtro por categoria
        if categoria_param:
            sql_parts.append("AND s.nome_sub ILIKE :categoria")
            params["categoria"] = f"%{categoria_param}%"

        # Ordenação e paginação
        sql_parts.append("ORDER BY a.valor_previsto DESC, a.descricao ASC")
        sql_parts.append("LIMIT :limit OFFSET :offset")
        params["limit"] = limit
        params["offset"] = offset

        sql_query = text(" ".join(sql_parts))

        # Query para contar total
        sql_count_parts = [
            """
            SELECT COUNT(*) as total
            FROM Agendamentos a
            JOIN SubCategoria s ON a.subcategoria_id = s.id
            WHERE a.usuario_id = :uid
              AND a.ativo = TRUE
            """
        ]

        if incluir_na_reserva_param is not None:
            sql_count_parts.append("AND a.incluir_na_reserva = :incluir")

        if periodicidade_param:
            sql_count_parts.append("AND a.periodicidade = :periodicidade")

        if categoria_param:
            sql_count_parts.append("AND s.nome_sub ILIKE :categoria")

        sql_count = text(" ".join(sql_count_parts))

        with db_engine.connect() as conn:
            # Buscar agendamentos
            results = conn.execute(sql_query, params).fetchall()

            # Contar total
            total = conn.execute(sql_count, params).scalar()

            agendamentos = []
            for row in results:
                agendamentos.append({
                    "id": row.id,
                    "descricao": row.descricao,
                    "valor_previsto": float(row.valor_previsto or 0),
                    "periodicidade": row.periodicidade,
                    "tipo_agendamento": row.tipo_agendamento,
                    "dia_execucao": row.dia_execucao,
                    "categoria": row.categoria,
                    "macro_categoria": row.macro_categoria,
                    "grupo": row.grupo,
                    "incluir_na_reserva": row.incluir_na_reserva
                })

            # Calcular resumo da reserva
            gasto_mensal, reserva_ideal, meses = finance_service.get_reserva_status(conn, usuario_id)

            return jsonify({
                "status": "sucesso",
                "total": total,
                "limit": limit,
                "offset": offset,
                "agendamentos": agendamentos,
                "resumo_reserva": {
                    "gasto_mensal_essencial": gasto_mensal,
                    "reserva_ideal": reserva_ideal,
                    "meses_configurados": meses
                }
            }), 200

    except Exception as e:
        print(f"[RESERVA-LIST] Erro: {e}")
        import traceback
        traceback.print_exc()
        return jsonify({
            "status": "erro",
            "mensagem": str(e)
        }), 500
