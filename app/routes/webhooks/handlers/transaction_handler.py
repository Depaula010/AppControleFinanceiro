# app/routes/webhooks/handlers/transaction_handler.py
"""
TransactionHandler - Processa webhooks de transacoes.

Rotas:
- /webhook-automate: Gatilho Android
- /api/transacao: API direta iPhone
- /webhook-sms-payment: Pagamento via SMS
"""

import unicodedata
from typing import Tuple, Any
from flask import request, jsonify
from werkzeug.exceptions import BadRequest
from datetime import date

from app import db_engine, gemini_model
from app.config import API_SECRET_KEY, BOT_WHATSAPP_URL
from app.utils import formatar_moeda, ensure_db_connection, sanitize_for_log, sanitize_input

from app.services import finance_service
from app.services import gemini_service
from app.services import notification_service
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.services.redis_service import redis_service
from app.services.fixed_bills_service import FixedBillsService
from app.services.finance.budget_validation_service import validate_budget

from ..shared.responses import WebhookResponse


class TransactionHandler:
    """Handler para webhooks de transacoes."""

    def __init__(self):
        self.response = WebhookResponse()

    def handle_automate(self) -> Tuple[Any, int]:
        """
        Rota do Gatilho Android com CONFIRMACAO.
        Processa notificacoes do app Automate e extrai transacoes com IA.
        """
        if not db_engine or not gemini_model:
            return jsonify({"status": "erro", "mensagem": "Servico nao configurado"}), 503

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
                return jsonify({"status": "erro", "mensagem": "API key invalida"}), 401

            usuario_id, numero_whatsapp_usuario = user_info
            print(f"[AUTOMATE] Usuario: {usuario_id}")

            # 2. Extrair com IA
            transacao_gemini = gemini_service.extract_from_notification(texto_notificacao, usuario_id)
            tipo_transacao = transacao_gemini.get('tipo_fluxo', 'Despesa')
            transacao_descricao = transacao_gemini.get('descricao_bruta')
            valor_decimal = float(transacao_gemini.get('valor_decimal', 0))
            data_hoje = date.today()

            with db_engine.connect() as conn:
                # 3. Buscar dados necessarios
                contas_usuario = finance_service.get_user_accounts(conn, usuario_id)

                conta_id_transacao = None
                if tipo_transacao == 'Despesa' and any(kw in texto_notificacao.lower() for kw in ['cartao', 'compra', 'credit']):
                    conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Cartao de Credito'), None)
                if not conta_id_transacao:
                    conta_id_transacao = next((c[0] for c in contas_usuario if c[2] == 'Conta Corrente'), contas_usuario[0][0])

                categories_json_list = finance_service.get_user_categories(conn, usuario_id, tipo_transacao)
                id_outros_fallback = finance_service.get_fallback_category_id(conn, tipo_transacao)

                # 4. Categorizar
                id_categoria_final = gemini_service.categorize_transaction(
                    categories_json_list, transacao_descricao, tipo_transacao, id_outros_fallback, usuario_id
                )

                # 5. NOVO: Criar transacao PENDENTE no Redis
                fatura_id_transacao = None
                conta_tipo_result = next((c[2] for c in contas_usuario if c[0] == conta_id_transacao), None)
                if conta_tipo_result == 'Cartao de Credito':
                    # Garantir que existe fatura para o periodo atual
                    finance_service.ensure_current_invoice_exists(conn, usuario_id, conta_id_transacao)

                    fatura_id_transacao = finance_service.get_or_create_fatura(conn, conta_id_transacao, data_hoje, usuario_id)

                    # CRITICO: Commit para persistir a fatura antes de salvar no Redis
                    conn.commit()

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

                # Verificar se Redis esta disponivel
                if not redis_service.is_connected():
                    # Fallback: Salvar direto sem confirmacao
                    print("[AUTOMATE] Redis indisponivel. Salvando direto.")
                    finance_service.create_transaction(
                        conn, usuario_id, conta_id_transacao, id_categoria_final,
                        fatura_id_transacao, transacao_descricao, valor_para_db,
                        tipo_transacao, data_hoje
                    )
                    conn.commit()

                    nome_cat = finance_service.get_category_name_by_id(conn, id_categoria_final)
                    mensagem = f"Transacao salva!\n\nDescricao: {transacao_descricao}\nValor: {formatar_moeda(valor_decimal)}\nCategoria: {nome_cat}"
                    notification_service.enviar_notificacao_whatsapp(
                        numero_whatsapp_usuario, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                    )
                    return jsonify({"status": "sucesso"}), 200

                # Redis disponivel: Criar pendente
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

                # Enviar mensagem de confirmacao
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
                    "mensagem": "Aguardando confirmacao do usuario",
                    "transaction_id": transaction_id
                }), 200

        except Exception as e:
            print(f"[AUTOMATE] Erro: {e}")
            return jsonify({"status": "erro", "mensagem": str(e)}), 500

    def handle_api_transacao(self) -> Tuple[Any, int]:
        """
        Endpoint direto para registro de transacoes via iPhone/automacoes.

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
            return jsonify({"status": "erro", "mensagem": "Servico nao configurado"}), 503

        # Adiciona o log para todas as requisicoes
        raw_data = request.get_data(as_text=True)
        print(f"[API-TRANSACAO] Conteudo recebido: {raw_data}")

        try:
            try:
                data = request.get_json()
                if data is None:
                    raise BadRequest("Request body is not JSON or is empty")
            except BadRequest as e:
                print(f"[API-TRANSACAO] ERRO: {e}")
                return jsonify({"erro": "JSON invalido ou ausente"}), 400

            # Log do payload recebido (sanitizado para nao expor dados sensiveis)
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
                # Remover acentos (ex: "credito" -> "credito", "debito" -> "debito")
                tipo_pagamento = unicodedata.normalize('NFKD', tipo_pagamento).encode('ascii', 'ignore').decode('ascii')
            else:
                tipo_pagamento = None

            # Validacoes de campos obrigatorios com detalhamento
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
                erro_msg = f"Campos obrigatorios faltando: {', '.join(campos_faltando)}"
                print(f"[API-TRANSACAO] ERRO: {erro_msg}")
                print(f"[API-TRANSACAO] Dados recebidos: user_api_key={bool(user_api_key)}, valor={valor}, local={local}, conta={conta_nome}, tipo_pagamento={tipo_pagamento}")
                return jsonify({
                    "status": "erro",
                    "mensagem": erro_msg
                }), 400

            # Validar valor numerico positivo e razoavel
            try:
                valor = float(valor)
                if valor <= 0:
                    raise ValueError("Valor deve ser positivo")
                if valor > 1000000000:  # 1 bilhao - limite razoavel
                    raise ValueError("Valor muito alto (max: 1 bilhao)")
            except (ValueError, TypeError) as e:
                return jsonify({
                    "status": "erro",
                    "mensagem": f"Valor invalido: {str(e)}"
                }), 400

            # Validar tipo_pagamento
            tipos_validos = ['credito', 'debito', 'pix', 'dinheiro']
            if tipo_pagamento not in tipos_validos:
                return jsonify({
                    "status": "erro",
                    "mensagem": f"tipo_pagamento invalido. Use: {', '.join(tipos_validos)}"
                }), 400

            # Normalizar descricao (pode ser None)
            if descricao:
                descricao = descricao.strip()
                if len(descricao) == 0:
                    descricao = None

            print(f"[API-TRANSACAO] Recebido: {valor} - {local} ({conta_nome} / {tipo_pagamento})")

            # 1. Autenticar usuario
            user_info = finance_service.get_user_by_api_key(user_api_key)
            if not user_info:
                return jsonify({"status": "erro", "mensagem": "API key invalida"}), 401

            usuario_id, numero_whatsapp_usuario = user_info
            print(f"[API-TRANSACAO] Usuario: {usuario_id}")

            with db_engine.connect() as conn:
                # 2. Buscar conta pelo nome
                conta_detalhes = finance_service.get_account_details_by_name(conn, usuario_id, conta_nome)

                if not conta_detalhes:
                    return jsonify({
                        "status": "erro",
                        "mensagem": f"Conta '{conta_nome}' nao encontrada. Verifique o nome exato."
                    }), 400

                conta_id = conta_detalhes['id']
                conta_tipo = conta_detalhes['tipo']
                conta_nome_real = conta_detalhes['nome']

                print(f"[API-TRANSACAO] Conta encontrada: {conta_nome_real} (ID: {conta_id}, Tipo: {conta_tipo})")

                # 3. Preparar texto para categorizacao IA
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

                # 5. Detectar se precisa vincular a fatura
                fatura_id = None
                if tipo_pagamento == 'credito':
                    # Vincular a fatura
                    data_hoje = date.today()

                    # Garantir que existe fatura para o periodo atual
                    finance_service.ensure_current_invoice_exists(conn, usuario_id, conta_id)

                    fatura_id = finance_service.get_or_create_fatura(conn, conta_id, data_hoje, usuario_id)
                    print(f"[API-TRANSACAO] Fatura ID: {fatura_id}")

                    # CRITICO: Commit para persistir a fatura antes de salvar no Redis
                    # Sem isso, a fatura e rolada back quando a conexao fecha
                    conn.commit()

                # 6. Preparar descricao final para salvar
                descricao_final = local
                if descricao:
                    descricao_final = f"{local} - {descricao}"

                # 6.5. Validar limite de pote de gastos
                budget_warning = None
                budget_validation = validate_budget(
                    conn=conn,
                    usuario_id=usuario_id,
                    subcategoria_id=id_categoria,
                    valor_transacao=valor,
                    data_transacao=date.today()
                )
                if budget_validation.requer_confirmacao:
                    budget_warning = budget_validation.mensagem
                    print(f"[API-TRANSACAO] Aviso de budget: ultrapassaria limite de pote")

                # 7. Preparar dados para transacao pendente
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
                    'origem': 'api_endpoint',
                    'budget_warning': budget_warning  # Aviso de limite de pote (se houver)
                }

                # 8. Verificar se Redis esta disponivel
                if not redis_service.is_connected():
                    # Fallback: Salvar direto sem confirmacao
                    print("[API-TRANSACAO] Redis indisponivel. Salvando direto.")
                    finance_service.create_transaction(
                        conn, usuario_id, conta_id, id_categoria,
                        fatura_id, descricao_final, valor_db,
                        'Despesa', date.today()
                    )
                    conn.commit()

                    nome_cat = finance_service.get_category_name_by_id(conn, id_categoria)
                    mensagem = f"Transacao salva!\n\n{descricao_final}\n{formatar_moeda(valor)}\n{nome_cat}"
                    notification_service.enviar_notificacao_whatsapp(
                        numero_whatsapp_usuario, mensagem, BOT_WHATSAPP_URL, API_SECRET_KEY
                    )

                    return jsonify({
                        "status": "success",
                        "message": "Transacao salva com sucesso (Redis indisponivel)",
                        "categoria_sugerida": nome_cat
                    }), 200

                # 9. Criar transacao pendente no Redis
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
                        "message": "Transacao salva (erro no Redis)",
                        "transaction_id": None
                    }), 200

                # 10. Salvar como "ultima pendente" para contexto
                redis_service.set_with_ttl(f"last_pending:{numero_whatsapp_usuario}", transaction_id, 300)

                # 11. Formatar e enviar mensagem de confirmacao
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
                    "message": "Transacao pendente de confirmacao no WhatsApp",
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

    def handle_sms_payment(self) -> Tuple[Any, int]:
        """
        Endpoint especifico para pagamentos via Sms (iPhone Automation).

        Payload esperado:
        {
            "user_api_key": "...",
            "descricao_pagamento": "Conta de Agua",
            "valor_pago": 150.50,
            "conta_pagamento": "swile",
            "data_pagamento": "2024-01-15" (opcional, padrao: hoje)
        }
        """
        try:
            ensure_db_connection()
        except Exception as e:
            return jsonify({
                "status": "erro",
                "resposta": "Banco de dados temporariamente indisponivel"
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

            # Autenticar usuario
            user_info = finance_service.get_user_by_api_key(user_api_key)
            if not user_info:
                return jsonify({"status": "erro", "mensagem": "API key invalida"}), 401

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
                    agendamento_id, desc_original, valor_previsto, dia_venc, tipo_agend, categoria, conta_id_agendamento = match

                    # Usar a conta_id do agendamento (nao buscar outra conta)
                    # A conta correta ja esta configurada no agendamento

                    # Quitar a conta fixa
                    transaction_id = FixedBillsService.settle_fixed_bill(
                        conn, usuario_id, agendamento_id, valor, data_pagamento,
                        conta_pagamento_id=conta_id_agendamento
                    )

                    conn.commit()

                    # Notificar usuario
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
                    # Nao encontrou conta fixa, registrar como despesa normal
                    # (usar fluxo normal de categorizacao)

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
                        f"Pagamento Registrado!\n\n"
                        f"{descricao}\n"
                        f"{formatar_moeda(valor)}\n"
                        f"{conta_pagamento}\n\n"
                        f"_Nao encontrei uma conta fixa correspondente, entao registrei como despesa avulsa._"
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
            print(f"[SMS-PAYMENT] Erro: {e}")
            return jsonify({"status": "erro", "mensagem": str(e)}), 500


# Instancia singleton
_handler = TransactionHandler()


def handle_automate_webhook() -> Tuple[Any, int]:
    """Funcao de entrada para o webhook do Automate (Android)."""
    return _handler.handle_automate()


def handle_api_transacao() -> Tuple[Any, int]:
    """Funcao de entrada para a API de transacao direta."""
    return _handler.handle_api_transacao()


def handle_sms_payment() -> Tuple[Any, int]:
    """Funcao de entrada para pagamento via SMS."""
    return _handler.handle_sms_payment()
