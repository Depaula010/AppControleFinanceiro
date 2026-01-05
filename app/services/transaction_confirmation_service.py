from app.services.redis_service import redis_service
from app.services import finance_service
from app.utils import formatar_moeda
import uuid

class TransactionConfirmationService:
    """Gerencia o fluxo de confirmação de transações"""
    
    @staticmethod
    def create_pending_transaction(numero_whatsapp, transacao_data):
        """
        Cria uma transação pendente no Redis.
        
        Args:
            numero_whatsapp: Número do usuário
            transacao_data: Dict com todos os dados da transação
            
        Returns:
            transaction_id: ID único da transação pendente
        """
        # Gera ID único para esta transação
        transaction_id = str(uuid.uuid4())[:8]
        
        # Chave no Redis: pending_tx:{numero}:{transaction_id}
        redis_key = f"pending_tx:{numero_whatsapp}:{transaction_id}"
        
        # Salva no Redis com TTL de 5 minutos
        success = redis_service.set_with_ttl(
            redis_key,
            transacao_data,
            ttl_seconds=300  # 5 minutos
        )
        
        if success:
            # CRÍTICO: Salvar referência para última transação pendente
            # Isso permite que o usuário responda "ok" sem precisar do ID
            last_pending_key = f"last_pending:{numero_whatsapp}"
            redis_service.set_with_ttl(
                last_pending_key,
                transaction_id,
                ttl_seconds=300  # Mesmo TTL da transação
            )
            print(f"[CONFIRM] Transação pendente criada: {transaction_id}")
            print(f"[CONFIRM] Chave last_pending salva: {last_pending_key} -> {transaction_id}")
            return transaction_id
        else:
            print(f"[CONFIRM] ERRO ao criar transação pendente")
            return None
    
    @staticmethod
    def get_pending_transaction(numero_whatsapp, transaction_id):
        """Recupera uma transação pendente"""
        redis_key = f"pending_tx:{numero_whatsapp}:{transaction_id}"
        return redis_service.get(redis_key)
    
    @staticmethod
    def delete_pending_transaction(numero_whatsapp, transaction_id):
        """Remove uma transação pendente e sua referência last_pending"""
        redis_key = f"pending_tx:{numero_whatsapp}:{transaction_id}"
        last_pending_key = f"last_pending:{numero_whatsapp}"
        
        # Deletar ambas as chaves
        redis_service.delete(redis_key)
        redis_service.delete(last_pending_key)
        
        print(f"[CONFIRM] Transação {transaction_id} e last_pending deletadas")
        return True
    
    @staticmethod
    def format_confirmation_message(transacao_data, categorias_disponiveis, transaction_id):
        """
        Formata a mensagem de confirmação para o usuário.

        Args:
            transacao_data: Dados da transação
            categorias_disponiveis: Lista de categorias do usuário
            transaction_id: ID da transação pendente

        Returns:
            Mensagem formatada para WhatsApp
        """
        tipo = transacao_data['tipo_transacao']
        valor = transacao_data['valor_original']
        categoria_sugerida_id = transacao_data['categoria_id']

        # Extrair campos novos (com fallback para compatibilidade)
        local = transacao_data.get('local')
        descricao = transacao_data.get('descricao')
        conta_nome = transacao_data.get('conta_nome')
        conta_tipo = transacao_data.get('conta_tipo')
        tipo_pagamento = transacao_data.get('tipo_pagamento')
        fatura_id = transacao_data.get('fatura_id')

        # Informações de parcelamento
        num_parcelas = transacao_data.get('num_parcelas')
        valor_total = transacao_data.get('valor_total')

        # Fallback: se não tiver 'local', usar 'descricao' antiga
        if not local:
            # Formato antigo
            local = transacao_data.get('descricao')
            descricao = None

        # Encontra o nome da categoria sugerida
        categoria_sugerida_nome = "Desconhecida"
        for cat in categorias_disponiveis:
            if cat['id'] == categoria_sugerida_id:
                categoria_sugerida_nome = f"{cat['nome_macro']} → {cat['nome_sub']}"
                break

        valor_fmt = formatar_moeda(valor)
        
        # Emoji para tipo de transação
        emoji_tipo = "💰" if tipo == "Renda" else "💸"

        # Emoji para tipo de pagamento
        if tipo_pagamento == 'credito':
            emoji_pagamento = "💳"
        elif tipo_pagamento == 'debito':
            emoji_pagamento = "💰"
        elif tipo_pagamento == 'pix':
            emoji_pagamento = "📱"
        elif tipo_pagamento == 'dinheiro':
            emoji_pagamento = "💵"
        else:
            emoji_pagamento = "💸"

        # TÍTULO mais limpo (sem emoji duplicado)
        mensagem = f"💰 *CONFIRME SUA TRANSAÇÃO*\n\n"

        # INFORMAÇÕES PRINCIPAIS (ordem de prioridade)
        mensagem += f"📍 *{local}*\n"
        
        if descricao:
            mensagem += f"📝 {descricao}\n"

        # Mostrar valor (com informação de parcelamento se houver)
        if num_parcelas and num_parcelas > 1:
            valor_total_fmt = formatar_moeda(valor_total)
            mensagem += f"💵 *{valor_total_fmt}* ({num_parcelas}x de {valor_fmt})\n"
        else:
            mensagem += f"💵 *{valor_fmt}*\n"

        mensagem += f"{emoji_tipo} {tipo}\n"

        # Informações da conta (mais conciso)
        if conta_nome:
            if tipo_pagamento == 'credito' and fatura_id:
                # É cartão de crédito
                if num_parcelas and num_parcelas > 1:
                    mensagem += f"� {conta_nome} (1ª parcela na fatura atual)\n"
                else:
                    mensagem += f"� {conta_nome} (na fatura)\n"
            else:
                # Outras formas de pagamento
                tipo_pag_label = {
                    'debito': 'Débito',
                    'pix': 'PIX',
                    'dinheiro': 'Dinheiro'
                }.get(tipo_pagamento, tipo_pagamento)
                
                mensagem += f"🏦 {conta_nome} ({tipo_pag_label})\n"

        mensagem += f"📂 {categoria_sugerida_nome}\n\n"

        # OPÇÕES (mais direto e claro)
        mensagem += "━━━━━━━━━━━━━━━━━━━━\n"
        mensagem += "🔹 *OPÇÕES:*\n\n"
        mensagem += "✅ *OK* para confirmar\n"
        mensagem += "✏️ *TROCAR* para mudar categoria\n"
        mensagem += "❌ *CANCELAR* para descartar\n\n"
        mensagem += f"_ID: {transaction_id} | Expira em 5 min_"

        return mensagem
    
    @staticmethod
    def format_category_selection_message(categorias_disponiveis):
        """Formata mensagem com lista de categorias numeradas"""
        mensagem = "📂 *ESCOLHA A CATEGORIA CORRETA:*\n\n"
        
        # Agrupa por macro-categoria
        macros = {}
        for cat in categorias_disponiveis:
            macro = cat['nome_macro']
            if macro not in macros:
                macros[macro] = []
            macros[macro].append(cat)
        
        idx = 1
        categoria_map = {}  # {número: categoria_id}
        
        for macro_nome, subs in macros.items():
            mensagem += f"*{macro_nome}:*\n"
            for sub in subs:
                mensagem += f"  {idx}. {sub['nome_sub']}\n"
                categoria_map[str(idx)] = sub['id']
                idx += 1
            mensagem += "\n"
        
        mensagem += "━━━━━━━━━━━━━━━━━━━━\n"
        mensagem += "Digite o *número* da categoria desejada\n"
        mensagem += "ou *CANCELAR* para descartar."
        
        # Salvar o mapa no Redis também (para validação)
        return mensagem, categoria_map
    
    @staticmethod
    def process_confirmation_response(numero_whatsapp, user_message, transaction_id=None):
        """
        Processa a resposta do usuário (CONFIRMAR, TROCAR, CANCELAR, ou número).
        
        Returns:
            (status, mensagem_resposta, dados_para_salvar_ou_none)
            status: 'confirmed', 'awaiting_category', 'cancelled', 'error', 'saved'
        """
        user_message_clean = user_message.strip().upper()
        
        # Se não tem transaction_id, buscar a última pendente do usuário
        if not transaction_id:
            # Busca a mais recente (em produção, você pode melhorar isso)
            # Por enquanto, vamos exigir que o usuário responda em sequência
            return ('error', "Não encontrei nenhuma transação pendente para você. Ela pode ter expirado (5 min).", None)
        
        # Recuperar transação do Redis
        transacao_data = TransactionConfirmationService.get_pending_transaction(
            numero_whatsapp, 
            transaction_id
        )
        
        if not transacao_data:
            return ('error', "⏱️ Esta transação expirou ou não existe mais. Registre novamente.", None)
        
        # Verificar se está aguardando escolha de categoria
        if transacao_data.get('awaiting_category_selection'):
            # Usuário enviou um número
            try:
                escolha = int(user_message_clean)
                categoria_map = transacao_data.get('categoria_map', {})
                
                if str(escolha) in categoria_map:
                    novo_categoria_id = categoria_map[str(escolha)]
                    transacao_data['categoria_id'] = novo_categoria_id
                    transacao_data['awaiting_category_selection'] = False
                    
                    # Retornar para salvar
                    TransactionConfirmationService.delete_pending_transaction(numero_whatsapp, transaction_id)
                    return ('saved', None, transacao_data)
                else:
                    return ('error', "Número inválido. Escolha um número da lista ou digite CANCELAR.", None)
                    
            except ValueError:
                if user_message_clean == "CANCELAR":
                    TransactionConfirmationService.delete_pending_transaction(numero_whatsapp, transaction_id)
                    return ('cancelled', "❌ Transação cancelada.", None)
                else:
                    return ('error', "Digite um número válido da lista ou CANCELAR.", None)
        
        # Processar comandos principais
        if user_message_clean in ["CONFIRMAR", "OK", "SIM", "CONFIRMA"]:
            # Confirmar e salvar
            TransactionConfirmationService.delete_pending_transaction(numero_whatsapp, transaction_id)
            return ('saved', None, transacao_data)
        
        elif user_message_clean in ["TROCAR", "MUDAR", "ALTERAR"]:
            # Marcar como "aguardando seleção" e retornar lista
            transacao_data['awaiting_category_selection'] = True
            
            # Atualizar no Redis
            redis_key = f"pending_tx:{numero_whatsapp}:{transaction_id}"
            redis_service.set_with_ttl(redis_key, transacao_data, ttl_seconds=300)
            
            return ('awaiting_category', None, transacao_data)
        
        elif user_message_clean in ["CANCELAR", "NÃO", "NAO", "CANCELA"]:
            # Cancelar
            TransactionConfirmationService.delete_pending_transaction(numero_whatsapp, transaction_id)
            return ('cancelled', "❌ Transação cancelada.", None)
        
        else:
            return ('error', "Comando não reconhecido. Digite: CONFIRMAR, TROCAR ou CANCELAR.", None)