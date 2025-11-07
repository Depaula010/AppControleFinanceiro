# app/routes/webhooks.py
from flask import Blueprint, jsonify, request
from sqlalchemy import exc as sqlalchemy_exc
from datetime import date

# Importa os "Singletons" e Configs
from app import db_engine
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL

# Importa o Helper
from app.utils import formatar_moeda

# Importa TODOS os serviços
from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service

# Cria o Blueprint
webhooks_bp = Blueprint('webhooks', __name__)

@webhooks_bp.route('/webhook-automate', methods=['POST'])
def handle_automate_webhook():
    """ Rota do Gatilho Android (Notificações Automáticas) """
    if not db_engine or not gemini_service.gemini_model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503
    
    try:
        data = request.json
        texto_notificacao = data.get('texto')
        user_api_key = data.get('user_api_key') 
        
        if not texto_notificacao or not user_api_key:
            return jsonify({"status": "erro", "mensagem": "Chave 'texto' ou 'user_api_key' faltando"}), 400
        
        print(f"[AUTOMATE] Recebido: {texto_notificacao}")
        
        # 1. Autenticar (Camada de Serviço)
        user_info = finance_service.get_user_by_api_key(user_api_key)
        if not user_info:
            return jsonify({"status": "erro", "mensagem": "Chave de API de usuário inválida"}), 401
        
        usuario_id, numero_whatsapp_usuario = user_info
        print(f"[AUTOMATE] Usuário autenticado (ID: {usuario_id}). Processando...")

        # 2. Extrair com IA (Camada de Serviço)
        transacao_gemini = gemini_service.extract_from_notification(texto_notificacao)
        tipo_transacao = transacao_gemini.get('tipo_fluxo', 'Despesa')
        transacao_descricao = transacao_gemini.get('descricao_bruta')
        valor_decimal = float(transacao_gemini.get('valor_decimal', 0))
        data_hoje = date.today()

        # Inicia a transação de banco de dados
        with db_engine.connect() as conn:
            conn.begin() 
            
            # 3. Lógica de Negócios e Banco (Camada de Serviço)
            contas_usuario = finance_service.get_user_accounts(conn, usuario_id)
            
            # Lógica de negócio simples (qual conta usar?)
            conta_id_transacao = None
            if tipo_transacao == 'Despesa' and any(kw in texto_notificacao.lower() for kw in ['cartão', 'compra', 'credit']):
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Cartão de Crédito'), None)
            if not conta_id_transacao:
                conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Conta Corrente'), contas_usuario[0][0]) 

            categories_json_list = finance_service.get_user_categories(conn, usuario_id, tipo_transacao)
            id_outros_fallback = finance_service.get_fallback_category_id(conn, tipo_transacao)
            if id_outros_fallback is None:
                raise Exception("Erro interno: Categoria 'Outros' não encontrada")

            # 4. Categorizar com IA (Camada de Serviço)
            id_categoria_final = gemini_service.categorize_transaction(
                categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback
            )
            print(f"[GEMINI-2] ID de Categoria escolhido: {id_categoria_final}")

            # 5. Salvar no Banco (Camada de Serviço)
            fatura_id_transacao = None
            conta_tipo_result = next((c[2] for c in contas_usuario if c[0] == conta_id_transacao), None)
            if conta_tipo_result == 'Cartão de Crédito':
                fatura_id_transacao = finance_service.get_or_create_fatura(conn, conta_id_transacao, data_hoje, usuario_id)
            
            valor_para_db = valor_decimal if tipo_transacao == 'Renda' else (valor_decimal * -1)
            
            finance_service.create_transaction(
                conn, usuario_id, conta_id_transacao, id_categoria_final, fatura_id_transacao,
                transacao_descricao, valor_para_db, tipo_transacao, data_hoje
            )
            
            nome_categoria_salva = finance_service.get_category_name_by_id(conn, id_categoria_final)
            
            conn.commit()
            
        # 6. Notificar (Camada de Serviço)
        valor_formatado = formatar_moeda(valor_decimal)
        mensagem_notificacao = f"✅ Transação Automática Salva!\n\nDescrição: {transacao_descricao}\nValor: {valor_formatado} ({tipo_transacao})\nCategoria: {nome_categoria_salva}"
        if id_categoria_final == id_outros_fallback:
            mensagem_notificacao += f"\n\n*Atenção:* Não soube categorizar esta despesa. Salvei em 'Outros'."
        
        notification_service.enviar_notificacao_whatsapp(
            numero_whatsapp_usuario, mensagem_notificacao, BOT_WHATSAPP_URL, API_SECRET_KEY
        )

        return jsonify({"status": "sucesso", "transacao_salva": transacao_gemini, "categoria_id_escolhida": id_categoria_final}), 200

    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"Erro de Banco de Dados: {db_err}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        return jsonify({"status": "erro", "mensagem": f"Erro de Banco de Dados: {str(db_err)}"}), 500
    except Exception as e:
        print(f"Erro geral: {e}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        return jsonify({"status": "erro", "mensagem": str(e)}), 500


@webhooks_bp.route('/webhook-whatsapp', methods=['POST'])
def handle_whatsapp_webhook():
    """
    Recebe uma mensagem manual, classifica a INTENÇÃO, processa e salva/consulta.
    """
    if not db_engine or not gemini_service.gemini_model:
        return jsonify({"status": "erro", "mensagem": "Serviço não configurado"}), 503

    # 1. Autenticar API
    secret_key_recebida = request.headers.get('x-api-key', '').strip()
    if not secret_key_recebida or secret_key_recebida != API_SECRET_KEY:
        print("[WHATSAPP] ❌ VALIDAÇÃO DE CHAVE FALHOU!")
        return jsonify({"status": "erro", "resposta": "Chave de API inválida."}), 401
    
    print("[WHATSAPP] ✅ Chave validada com sucesso!")
    
    try:
        data = request.json
        texto_msg = data.get('texto')
        numero_remetente = data.get('numero_remetente')
        if not texto_msg or not numero_remetente:
            return jsonify({"status": "erro", "mensagem": "Faltando 'texto' ou 'numero_remetente'"}), 400

        numero_limpo = numero_remetente.split('@')[0]
        print(f"[WHATSAPP] Mensagem recebida de {numero_limpo}: {texto_msg}")

        # 2. Autenticar Usuário (Camada de Serviço)
        usuario_id = finance_service.get_user_by_whatsapp(numero_limpo)
        if usuario_id is None:
            return jsonify({"status": "erro", "resposta": "Usuário não autorizado."}), 401
        
        print(f"[WHATSAPP] Usuário autenticado (ID: {usuario_id}). Processando...")

        # 3. Classificar Intenção com IA (Camada de Serviço)
        intent = gemini_service.get_message_intent(texto_msg)
        
        data_hoje = date.today()
        resposta_para_usuario = "" 

        # Inicia a transação de banco
        with db_engine.connect() as conn:
            conn.begin() 
            
            # 4. Lógica de Negócios (orquestrando serviços)
            
            if intent == 'Renda' or intent == 'Despesa':
                trans_data = gemini_service.extract_transaction_details(texto_msg, intent)
                trans_desc = trans_data.get('descricao_bruta')
                valor_dec = float(trans_data.get('valor_decimal', 0))
                
                cats_list = finance_service.get_user_categories(conn, usuario_id, intent)
                id_outros = finance_service.get_fallback_category_id(conn, intent)
                
                id_categoria = gemini_service.categorize_transaction(cats_list, trans_desc, intent, id_outros)
                
                conta_nome_padrao = 'Banco Inter' if intent == 'Renda' else 'Carteira'
                conta_id = finance_service.get_account_by_name(conn, usuario_id, conta_nome_padrao, fallback=True)
                
                valor_db = valor_dec if intent == 'Renda' else (valor_dec * -1)
                
                finance_service.create_transaction(
                    conn, usuario_id, conta_id, id_categoria, None,
                    trans_desc, valor_db, intent, data_hoje
                )
                
                nome_cat = finance_service.get_category_name_by_id(conn, id_categoria)
                valor_fmt = formatar_moeda(valor_dec)
                resposta_para_usuario = f"✅ {intent} manual salva!\nDescrição: {trans_desc}\nValor: {valor_fmt}\nCategoria: {nome_cat}"

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
                
                valor_fmt = formatar_moeda(valor_dec)
                resposta_para_usuario = f"✅ Transferência salva!\n\nValor: {valor_fmt}\nDe: {nome_orig}\nPara: {nome_dest}"

            elif intent == 'Pagamento Fatura':
                contas_raw = finance_service.get_user_accounts(conn, usuario_id)
                contas_list = [{"nome": c[1], "tipo": c[2]} for c in contas_raw]

                fatura_data = gemini_service.extract_fatura_payment_details(texto_msg, contas_list)
                valor_dec = float(fatura_data.get('valor_decimal', 0))
                nome_origem = fatura_data.get('conta_origem')
                nome_cartao = fatura_data.get('conta_cartao')

                if not valor_dec or not nome_origem or not nome_cartao:
                    raise Exception("Gemini não conseguiu extrair os dados do pagamento (valor, origem, cartão).")
                
                conta_id_origem = finance_service.get_account_by_name(conn, usuario_id, nome_origem)
                conta_id_cartao = finance_service.get_account_by_name(conn, usuario_id, nome_cartao)

                if not conta_id_origem or not conta_id_cartao:
                    raise Exception(f"Não foi possível encontrar as contas ({nome_origem} -> {nome_cartao}).")
                
                nome_cartao_pago = finance_service.create_fatura_payment(
                    conn, usuario_id, conta_id_origem, conta_id_cartao, valor_dec, data_hoje
                )
                
                valor_fmt = formatar_moeda(valor_dec)
                resposta_para_usuario = f"✅ Pagamento da fatura '{nome_cartao_pago}' ({valor_fmt}) registrado com sucesso!"

            elif intent == 'Consulta Potes':
                potes_result = finance_service.get_pote_status(conn, usuario_id)
                resposta_para_usuario = "📊 *Status dos Seus Potes (Este Mês)* 📊\n\n"
                if not potes_result: 
                    resposta_para_usuario = "Você ainda não configurou nenhum 'Pote de Gasto' (Orçamento)."
                else:
                    for pote in potes_result:
                        valor_limite = float(pote[1]); valor_gasto = float(pote[2] or 0) * -1; valor_restante = valor_limite - valor_gasto
                        resposta_para_usuario += f"🍯 *{pote[0]}*:\n"
                        resposta_para_usuario += f"   - Gasto: *{formatar_moeda(valor_gasto)}*\n"
                        resposta_para_usuario += f"   - Limite: {formatar_moeda(valor_limite)}\n"
                        resposta_para_usuario += f"   - Restante: {formatar_moeda(valor_restante)}\n\n"

            elif intent == 'Consulta Reserva':
                media_mensal, reserva_ideal = finance_service.get_reserva_status(conn, usuario_id)
                resposta_para_usuario = "🆘 *Cálculo da Reserva de Emergência* 🆘\n\n"
                resposta_para_usuario += f"Sua média de gastos essenciais (últimos 3 meses) é: *{formatar_moeda(media_mensal)}* / mês\n"
                resposta_para_usuario += f"Sua reserva ideal (6x) é: *{formatar_moeda(reserva_ideal)}*"

            elif intent == 'Consulta Categoria Específica':
                cat_data = gemini_service.extract_category_query(texto_msg)
                nome_categoria_consulta = cat_data.get('nome_categoria')
                if not nome_categoria_consulta: 
                    raise Exception("Gemini não conseguiu extrair o nome da categoria da consulta.")
                
                valor_gasto = finance_service.get_category_spending(conn, usuario_id, nome_categoria_consulta)
                
                resposta_para_usuario = f"ℹ️ *Consulta de Categoria (Este Mês)*\n\n"
                resposta_para_usuario += f"Você gastou *{formatar_moeda(valor_gasto)}* com '{nome_categoria_consulta}'."

            else:
                resposta_para_usuario = f"🤔 Desculpe, não entendi. Tente 'gastei 50', 'transferi 100', 'paguei a fatura', 'meus potes' ou 'minha reserva'."

            conn.commit() 
        
        print(f"[DATABASE] Processamento MANUAL concluído (Usuário: {usuario_id})!")
        return jsonify({"status": "sucesso", "resposta": resposta_para_usuario}), 200

    except sqlalchemy_exc.SQLAlchemyError as db_err:
        print(f"[ERRO] Erro de Banco de Dados: {db_err}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        return jsonify({"status": "erro", "resposta": f"Erro de Banco de Dados. Tente novamente."}), 200
    except Exception as e:
        print(f"[ERRO] Erro geral: {e}")
        try: 
            with db_engine.connect() as conn: 
                conn.rollback()
        except: pass
        # Não envie o 'str(e)' para o usuário por segurança
        return jsonify({"status": "erro", "resposta": f"Ocorreu um erro ao processar sua solicitação. Tente novamente."}), 500