# app/routes/webhooks.py (COM SISTEMA DE CONFIRMAÇÃO)
from flask import Blueprint, jsonify, request
from sqlalchemy import exc as sqlalchemy_exc
from datetime import date

from app import db_engine
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app.utils import formatar_moeda

from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service
from app.services import user_service
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.services.redis_service import redis_service

webhooks_bp = Blueprint('webhooks', __name__)


@webhooks_bp.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """Rota do Gatilho Android com CONFIRMAÇÃO"""
    
    if not db_engine or not gemini_service.gemini_model:
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


@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """Webhook WhatsApp com CONFIRMAÇÃO e suporte a cadastro"""
    
    if not db_engine or not gemini_service.gemini_model:
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

        # 3. NOVO: Verificar se está respondendo a uma confirmação
        # Tentar identificar transaction_id na mensagem ou buscar última pendente
        # (Simplificado: buscar por padrão "ID: XXXX" na última msg ou assumir contexto)
        
        # Por simplicidade, vamos checar se existe alguma pendente recente
        # Em produção, você pode enviar o transaction_id na mensagem ou usar contexto
        
        # Verificar se mensagem parece uma resposta de confirmação
        msg_upper = texto_msg.strip().upper()
        if any(word in msg_upper for word in ['CONFIRMAR', 'OK', 'TROCAR', 'CANCELAR']) or msg_upper.isdigit():
            # Provável resposta de confirmação
            # Buscar última transação pendente (você pode melhorar isso)
            # Por ora, vamos precisar que o usuário responda em sequência
            # Alternativa: Armazenar "última_transaction_id" no Redis por usuário
            
            # Implementação simplificada:
            # Vamos criar uma chave especial no Redis: last_pending:{numero}
            last_tx_key = f"last_pending:{numero_limpo}"
            last_transaction_id = redis_service.get(last_tx_key)
            
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
                        finance_service.create_transaction(
                            conn,
                            dados['usuario_id'],
                            dados['conta_id'],
                            dados['categoria_id'],
                            dados['fatura_id'],
                            dados['descricao'],
                            dados['valor_db'],
                            dados['tipo_transacao'],
                            date.fromisoformat(dados['data_transacao'])
                        )
                        
                        nome_cat = finance_service.get_category_name_by_id(conn, dados['categoria_id'])
                        conn.commit()
                    
                    # Limpar last_pending
                    redis_service.delete(last_tx_key)
                    
                    resposta = f"✅ *Transação Salva com Sucesso!*\n\n"
                    resposta += f"📝 {dados['descricao']}\n"
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
                
                cats_list = finance_service.get_user_categories(conn, usuario_id, intent)
                id_outros = finance_service.get_fallback_category_id(conn, intent)
                id_categoria = gemini_service.categorize_transaction(cats_list, trans_desc, intent, id_outros)
                
                conta_nome = 'Banco Inter' if intent == 'Renda' else 'Carteira'
                conta_id = finance_service.get_account_by_name(conn, usuario_id, conta_nome, fallback=True)
                valor_db = valor_dec if intent == 'Renda' else (valor_dec * -1)
                
                # Criar pendente
                transacao_data = {
                    'usuario_id': usuario_id,
                    'conta_id': conta_id,
                    'categoria_id': id_categoria,
                    'fatura_id': None,
                    'descricao': trans_desc,
                    'valor_db': valor_db,
                    'valor_original': valor_dec,
                    'tipo_transacao': intent,
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
            
            # Outros intents (Transferência, Consultas, etc.) - SEM confirmação
            # ... (manter código anterior)
            
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
            
            # ... (outros intents)
            
            else:
                return jsonify({"status": "sucesso", "resposta": "🤔 Não entendi. Tente 'gastei 50' ou 'meus potes'."}), 200

    except Exception as e:
        print(f"[WHATSAPP] Erro: {e}")
        return jsonify({"status": "erro", "resposta": "Erro ao processar."}), 500