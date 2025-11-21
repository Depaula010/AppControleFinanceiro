# app/routes/webhooks.py (COM SISTEMA DE CONFIRMAÇÃO)
from flask import Blueprint, jsonify, request
from sqlalchemy import exc as sqlalchemy_exc
from datetime import date
from app.services.period_query_service import PeriodQueryService
from app.services.fixed_bills_service import FixedBillsService

from sqlalchemy import text

from app import db_engine, gemini_model
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app.utils import formatar_moeda
from app.utils import ensure_db_connection

from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service
from app.services import user_service
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.services.redis_service import redis_service

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
        transacao_gemini = gemini_service.extract_from_notification(texto_notificacao)
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
                categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback
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

    try:
        data = request.json

        # Log do payload recebido (sem expor a API key completa)
        print(f"[API-TRANSACAO] Payload recebido: {data}")

        user_api_key = data.get('user_api_key')
        valor = data.get('valor')
        local = data.get('local')
        descricao = data.get('descricao')
        conta_nome = data.get('conta')
        tipo_pagamento = data.get('tipo_pagamento')

        # Normalizar strings (remover espaços e acentos)
        if conta_nome:
            conta_nome = conta_nome.strip()
        if local:
            local = local.strip()
        if tipo_pagamento:
            # Remover espaços e normalizar acentos
            tipo_pagamento = tipo_pagamento.strip().lower()
            # Normalizar variações comuns
            tipo_pagamento = tipo_pagamento.replace('é', 'e').replace('í', 'i')

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

        # Validar valor numérico positivo
        try:
            valor = float(valor)
            if valor <= 0:
                raise ValueError("Valor deve ser positivo")
        except (ValueError, TypeError):
            return jsonify({
                "status": "erro",
                "mensagem": "Valor inválido. Deve ser um número positivo."
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
                id_categoria_outros
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

    # 1. Autenticar API
    secret_key_recebida = request.headers.get('x-api-key', '').strip()
    if not secret_key_recebida or secret_key_recebida != API_SECRET_KEY:
        return jsonify({"status": "erro", "resposta": "Não autorizado"}), 401
    
    try:
        data = request.json
        texto_msg = data.get('texto')
        numero_remetente = data.get('numero_remetente')
        
        if not texto_msg or not numero_remetente:
            return jsonify({"status": "erro", "mensagem": "Dados faltando"}), 400

        numero_limpo = numero_remetente.split('@')[0]
        print(f"[WHATSAPP] Mensagem de {numero_limpo}: {texto_msg}")

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
        palavras_confirmacao_evento = ['sim', 'confirmar', 'confirma', 'ok', 's']
        palavras_cancelamento_evento = ['não', 'nao', 'cancelar', 'cancela', 'desistir', 'n']

        # Verificar se é resposta simples de confirmação/cancelamento
        if msg_lower in palavras_confirmacao_evento or msg_lower in palavras_cancelamento_evento:
            # Buscar evento pendente
            event_id, event_data = EventConfirmationService.get_latest_pending_event(numero_limpo)

            if event_data:
                if msg_lower in palavras_confirmacao_evento:
                    # Confirmar e criar evento
                    sucesso, mensagem, google_event_id = EventConfirmationService.confirm_and_create_event(numero_limpo, event_id)

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

        # 3.1. Verificar se está respondendo a uma confirmação de TRANSAÇÃO
        # Tentar identificar transaction_id na mensagem ou buscar última pendente
        # (Simplificado: buscar por padrão "ID: XXXX" na última msg ou assumir contexto)

        # Por simplicidade, vamos checar se existe alguma pendente recente
        # Em produção, você pode enviar o transaction_id na mensagem ou usar contexto

        # Verificar se mensagem parece uma resposta de confirmação
        msg_upper = texto_msg.strip().upper()
        if any(word in msg_upper for word in ['CONFIRMAR', 'OK', 'TROCAR', 'CANCELAR']) or msg_upper.isdigit():
            # Provável resposta de confirmação
            print(f"[CONFIRM-CHECK] Detectada possível confirmação: '{texto_msg}'")

            # Buscar última transação pendente
            last_tx_key = f"last_pending:{numero_limpo}"
            last_transaction_id = redis_service.get(last_tx_key)

            print(f"[CONFIRM-CHECK] Buscando {last_tx_key} -> {last_transaction_id}")

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

                        finance_service.create_transaction(
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

                        nome_cat = finance_service.get_category_name_by_id(conn, dados['categoria_id'])
                        conn.commit()
                    
                    # Limpar last_pending
                    redis_service.delete(last_tx_key)

                    # Formatar descrição para exibição
                    local = dados.get('local')
                    descricao = dados.get('descricao')

                    if local:
                        # Novo formato
                        texto_desc = f"📍 {local}"
                        if descricao:
                            texto_desc += f" - {descricao}"
                    else:
                        # Formato antigo (compatibilidade)
                        texto_desc = f"📝 {dados.get('descricao_final', dados.get('descricao'))}"

                    resposta = f"✅ *Transação Salva com Sucesso!*\n\n"
                    resposta += f"{texto_desc}\n"
                    resposta += f"💵 {formatar_moeda(dados['valor_original'])}\n"
                    resposta += f"🏷️ {nome_cat}"

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

        # 4. Classificar intenção (fluxo normal)
        intent = gemini_service.get_message_intent(texto_msg)
        data_hoje = date.today()

        with db_engine.connect() as conn:
            conn.begin()

            # Fluxo de Renda/Despesa com CONFIRMAÇÃO
            if intent == 'Renda' or intent == 'Despesa':
                trans_data = gemini_service.extract_transaction_details(texto_msg, intent)
                trans_desc = trans_data.get('descricao_bruta')
                valor_dec = float(trans_data.get('valor_decimal', 0))

                # Detectar parcelamento (apenas para despesas)
                parcelamento_info = None
                num_parcelas = None
                valor_parcela = valor_dec

                if intent == 'Despesa':
                    parcelamento_info = gemini_service.extract_parcelamento_info(texto_msg)
                    if parcelamento_info.get('parcelado'):
                        num_parcelas = parcelamento_info.get('num_parcelas')
                        if num_parcelas and num_parcelas > 1:
                            valor_parcela = valor_dec / num_parcelas
                            # Usar descrição limpa (sem info de parcelamento)
                            trans_desc = parcelamento_info.get('descricao_limpa', trans_desc)
                            print(f"[PARCELAMENTO] Detectado: {num_parcelas}x de {valor_parcela:.2f}")

                cats_list = finance_service.get_user_categories(conn, usuario_id, intent)
                id_outros = finance_service.get_fallback_category_id(conn, intent)
                id_categoria = gemini_service.categorize_transaction(cats_list, trans_desc, intent, id_outros)

                # Detectar se mencionou cartão de crédito
                fatura_id = None
                conta_nome = None
                conta_tipo = None

                if intent == 'Renda':
                    conta_nome = 'Banco Inter'
                    conta_id = finance_service.get_account_by_name(conn, usuario_id, conta_nome, fallback=True)
                    # Buscar tipo da conta
                    contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
                    conta_info = next((c for c in contas_usuario if c[0] == conta_id), None)
                    if conta_info:
                        conta_nome = conta_info[1]
                        conta_tipo = conta_info[2]
                else:  # Despesa
                    # Verificar se mencionou cartão
                    palavras_cartao = ['cartão', 'cartao', 'crédito', 'credito', 'card']
                    menciona_cartao = any(palavra in texto_msg.lower() for palavra in palavras_cartao)

                    if menciona_cartao:
                        # Buscar primeiro cartão de crédito do usuário
                        contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
                        conta_cartao = next((c for c in contas_usuario if c[2] == 'Cartão de Crédito'), None)

                        if conta_cartao:
                            conta_id = conta_cartao[0]
                            conta_nome = conta_cartao[1]
                            conta_tipo = conta_cartao[2]
                            # Não criar fatura agora - será criada quando confirmar
                            # Apenas marcamos que precisa criar
                            fatura_id = 'PENDING'  # Marcador especial
                            print(f"[WHATSAPP-DESPESA] Cartão detectado: {conta_nome}, fatura será criada após confirmação")
                        else:
                            # Não tem cartão, usar carteira
                            conta_nome = 'Carteira'
                            conta_id = finance_service.get_account_by_name(conn, usuario_id, conta_nome, fallback=True)
                            contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
                            conta_info = next((c for c in contas_usuario if c[0] == conta_id), None)
                            if conta_info:
                                conta_nome = conta_info[1]
                                conta_tipo = conta_info[2]
                    else:
                        # Não mencionou cartão, usar carteira
                        conta_nome = 'Carteira'
                        conta_id = finance_service.get_account_by_name(conn, usuario_id, conta_nome, fallback=True)
                        contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
                        conta_info = next((c for c in contas_usuario if c[0] == conta_id), None)
                        if conta_info:
                            conta_nome = conta_info[1]
                            conta_tipo = conta_info[2]

                # Para parcelamento, o valor_db é o valor da parcela (não o total)
                if num_parcelas and num_parcelas > 1:
                    valor_db = (valor_parcela * -1) if intent == 'Despesa' else valor_parcela
                else:
                    valor_db = (valor_dec * -1) if intent == 'Despesa' else valor_dec

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
                    
                    # Salvar como "última pendente"
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
                media_mensal, reserva_ideal = finance_service.get_reserva_status(conn, usuario_id)
                resposta_para_usuario = "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
                resposta_para_usuario += f"Média de gastos essenciais: *{formatar_moeda(media_mensal)}* / mês\n"
                resposta_para_usuario += f"Reserva ideal (6x): *{formatar_moeda(reserva_ideal)}*"

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

            # ===== INTENÇÃO: Consulta Saldo =====
            elif intent == 'Consulta Saldo':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                saldo_query = gemini_service.extract_saldo_query(texto_msg, contas_list)
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

            # ===== INTENÇÃO: Consulta por Período =====
            elif intent == 'Consulta Período':
                
                # Extrair período da mensagem
                period_data = gemini_service.extract_period_query(texto_msg)
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
            
            # ===== INTENÇÃO: Quitar Conta Fixa Manualmente =====
            elif intent == 'Quitar Conta Fixa':
                # Extrair dados do pagamento
                payment_data = gemini_service.extract_bill_payment(texto_msg)
                descricao_pag = payment_data.get('descricao')
                valor_pago = float(payment_data.get('valor', 0))

                if not descricao_pag or not valor_pago:
                    raise Exception("Não consegui identificar qual conta você pagou ou o valor.")

                # Buscar conta correspondente
                match = FixedBillsService.find_matching_bill(conn, usuario_id, descricao_pag)

                if not match:
                    resposta_para_usuario = (
                        f"🤔 Não encontrei uma conta fixa chamada '{descricao_pag}'.\n\n"
                        f"Você quis dizer:\n"
                        f"• \"Paguei a conta de água no valor de {formatar_moeda(valor_pago)}\"?\n\n"
                        f"Ou prefere ver suas contas pendentes? Digite: *minhas contas fixas*"
                    )
                    return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

                agendamento_id, desc_original, valor_previsto, dia_venc, categoria = match

                # Quitar a conta
                transaction_id = FixedBillsService.settle_fixed_bill(
                    conn, usuario_id, agendamento_id, valor_pago, date.today(),
                    observacao="Quitado manualmente"
                )

                conn.commit()

                resposta_para_usuario = FixedBillsService.mark_bill_as_paid_response(
                    desc_original, valor_pago, categoria
                )

                # Alertar se valor diferente do previsto
                if abs(float(valor_previsto) - valor_pago) > 1:
                    diferenca = valor_pago - float(valor_previsto)
                    resposta_para_usuario += f"\n\n💡 *Atenção:* O valor pago ({formatar_moeda(valor_pago)}) "
                    if diferenca > 0:
                        resposta_para_usuario += f"foi *{formatar_moeda(diferenca)}* maior que o previsto."
                    else:
                        resposta_para_usuario += f"foi *{formatar_moeda(abs(diferenca))}* menor que o previsto."

                return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200
            
            #==== INTENÇÃO: Transferência =====
            elif intent == 'Transferência':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]
                
                transf_data = gemini_service.extract_transfer_details(texto_msg, contas_list)
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

                fatura_data = gemini_service.extract_fatura_payment_details(texto_msg, contas_list)

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

                fatura_query = gemini_service.extract_fatura_query(texto_msg, contas_list)
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
                cat_data = gemini_service.extract_category_query(texto_msg)
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
                
                event_data = gemini_service.extract_event_creation_details(texto_msg)
                
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
                
                delete_data = gemini_service.extract_event_deletion_query(texto_msg)
                
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
                calendar_data = gemini_service.extract_calendar_query(texto_msg)
                period_type = calendar_data.get('period_type', 'hoje')

                # NOVO: Extrair filtro de horário
                time_data = gemini_service.extract_time_filter_query(texto_msg)
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
                free_time_data = gemini_service.extract_free_time_query(texto_msg)
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
                
                config_data = gemini_service.extract_notification_config(texto_msg)
                
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
                        resposta_para_usuario += f"📱 *Status atual:*\n"
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
                        resposta_para_usuario += f"📱 *Status atual:*\n"
                        resposta_para_usuario += f"• Ativa: {'Sim' if config['contas_vencer_ativa'] else 'Não'}\n"
                        resposta_para_usuario += f"• Dias antes: {config['contas_vencer_dias_antes']}\n"
                        resposta_para_usuario += f"• Horário: {config['contas_vencer_hora'].strftime('%H:%M')}\n"
                    else:
                        resposta_para_usuario = f"❌ {msg}"
                
                else:
                    resposta_para_usuario = "🤔 Não entendi qual tipo de notificação você quer configurar."

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
                    chart_info = gemini_service.extract_chart_type(texto_msg)
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

            else:
                return jsonify({"status": "sucesso", "resposta": "🤔 Não entendi. Tente 'gastei 50' ou 'meus potes'."}), 200

    except Exception as e:
        print(f"[WHATSAPP] Erro: {e}")
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
                    'Despesa'
                )
                
                cats = finance_service.get_user_categories(conn, usuario_id, 'Despesa')
                id_outros = finance_service.get_fallback_category_id(conn, 'Despesa')
                
                id_categoria = gemini_service.categorize_transaction(
                    cats, descricao, 'Despesa', id_outros
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
