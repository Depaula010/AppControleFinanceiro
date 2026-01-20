# app/routes/webhooks/intents/transaction_intents.py
"""
Intent handlers para operações de transação.

Exemplos implementados:
- RendaIntent: Registrar renda
- DespesaIntent: Registrar despesa

TODO: Implementar outros transaction intents quando necessário:
- TransferenciaIntent
- PagamentoFaturaIntent
- etc.
"""

from typing import Dict, Any
from app.services import finance_service, gemini_service, notification_service
from app.services.transaction_confirmation_service import TransactionConfirmationService
from app.utils import formatar_moeda
from .base_intent import ConfirmationRequiredIntent


class RendaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Renda'.

    Registra uma renda (salário, freelance, etc.) com sistema de confirmação.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da renda usando Gemini."""
        params = gemini_service.extract_income_params(
            self.mensagem,
            self.usuario_id,
            self.conn  # Passa conexão para buscar agendamentos LEMBRETE_VARIAVEL
        )

        return {
            "valor": params.get("valor"),
            "descricao": params.get("descricao", "Renda"),
            "data": params.get("data"),  # date object ou None (hoje)
            "conta": params.get("conta"),  # nome da conta
            "conta_id_agendamento": params.get("conta_id_agendamento"),  # ID da conta do agendamento
            "subcategoria_id_agendamento": params.get("subcategoria_id_agendamento"),  # ID da subcategoria do agendamento
            "descricao_agendamento": params.get("descricao_agendamento"),  # Descrição do agendamento
        }

    def validate(self) -> str | None:
        """Valida parâmetros extraídos."""
        if not self.params.get("valor") or self.params["valor"] <= 0:
            return "❌ Não consegui identificar o valor da renda. Por favor, informe o valor."

        # Valor mínimo razoável (evitar erros de extração)
        if self.params["valor"] < 0.01:
            return "❌ Valor muito baixo. Verifique se informou corretamente."

        return None  # Válido

    def execute(self) -> Dict[str, Any]:
        """Cria transação pendente de confirmação."""
        from datetime import date

        # Buscar ou escolher conta usando função centralizada
        conta_id, conta_nome, conta_tipo, _ = finance_service.resolve_account_for_transaction(
            conn=self.conn,
            usuario_id=self.usuario_id,
            tipo_transacao='Renda',
            mensagem=self.mensagem,
            conta_nome_param=self.params.get("conta"),
            conta_id_agendamento=self.params.get("conta_id_agendamento")
        )

        if not conta_id:
            return {
                "error": "❌ Conta não encontrada. Por favor, especifique a conta."
            }

        # Buscar lista de categorias (sempre necessária para formatar mensagem)
        cats_list = finance_service.get_user_categories(self.conn, self.usuario_id, 'Renda')
        
        # Buscar categoria
        # PRIORIDADE: Se encontrou agendamento, usar categoria do agendamento
        subcategoria_id_agendamento = self.params.get("subcategoria_id_agendamento")
        
        if subcategoria_id_agendamento:
            id_categoria = subcategoria_id_agendamento
            print(f"[RENDA-INTENT] Usando categoria do agendamento: {id_categoria}")
        else:
            # Não tem agendamento, deixar Gemini categorizar
            id_outros = finance_service.get_fallback_category_id(self.conn, 'Renda')
            id_categoria = gemini_service.categorize_transaction(
                cats_list, self.params["descricao"], 'Renda', id_outros, self.usuario_id
            )

        # Preparar estrutura de dados que TransactionConfirmationService espera
        data_transacao = self.params.get("data") or date.today()

        transacao_data = {
            'usuario_id': self.usuario_id,
            'conta_id': conta_id,
            'conta_nome': conta_nome,
            'conta_tipo': conta_tipo,
            'categoria_id': id_categoria,
            'fatura_id': None,
            'descricao': self.params["descricao"],
            'descricao_final': self.params.get("descricao_agendamento"),  # Usar descrição do agendamento se disponível
            'valor_db': self.params["valor"],  # Renda é positivo
            'valor_original': self.params["valor"],
            'valor_total': None,
            'num_parcelas': None,
            'tipo_transacao': 'Renda',
            'tipo_pagamento': 'debito',
            'data_transacao': str(data_transacao),
            'origem': 'whatsapp'
        }

        # Validar que temos numero_whatsapp
        if not self.numero_whatsapp:
            return {"error": "❌ Erro interno: número WhatsApp não disponível"}

        # Criar transação pendente no Redis
        tx_id = TransactionConfirmationService.create_pending_transaction(
            self.numero_whatsapp,
            transacao_data
        )

        if not tx_id:
            return {"error": "❌ Erro ao criar transação pendente"}

        return {
            "tx_id": tx_id,
            "transacao_data": transacao_data,
            "cats_list": cats_list,
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Usar o serviço existente para formatar
        msg_confirm = TransactionConfirmationService.format_confirmation_message(
            data["transacao_data"],
            data["cats_list"],
            data["tx_id"]
        )

        return msg_confirm


class DespesaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Despesa'.

    Registra uma despesa com sistema de confirmação e categorização.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da despesa usando Gemini."""
        params = gemini_service.extract_expense_params(
            self.mensagem,
            self.usuario_id,
            self.conn  # Passa conexão para buscar agendamentos LEMBRETE_VARIAVEL
        )

        return {
            "valor": params.get("valor"),
            "descricao": params.get("descricao", "Despesa"),
            "data": params.get("data"),
            "conta": params.get("conta"),
            "categoria": params.get("categoria"),  # Pode ser None
            "parcelamento": params.get("parcelamento"),  # Núm. parcelas ou None
            "conta_id_agendamento": params.get("conta_id_agendamento"),  # ID da conta do agendamento
            "subcategoria_id_agendamento": params.get("subcategoria_id_agendamento"),  # ID da subcategoria do agendamento
            "descricao_agendamento": params.get("descricao_agendamento"),  # Descrição do agendamento
        }

    def validate(self) -> str | None:
        """Valida parâmetros extraídos."""
        if not self.params.get("valor") or self.params["valor"] <= 0:
            return "❌ Não consegui identificar o valor da despesa. Por favor, informe o valor."

        if self.params["valor"] < 0.01:
            return "❌ Valor muito baixo. Verifique se informou corretamente."

        # Validar parcelamento se presente
        if self.params.get("parcelamento"):
            parcelas = self.params["parcelamento"]
            if not isinstance(parcelas, int) or parcelas < 2 or parcelas > 48:
                return "❌ Número de parcelas inválido (deve ser entre 2 e 48)."

        return None

    def execute(self) -> Dict[str, Any]:
        """Cria transação pendente de confirmação."""
        from datetime import date

        # Buscar ou escolher conta usando função centralizada
        conta_id, conta_nome, conta_tipo, _ = finance_service.resolve_account_for_transaction(
            conn=self.conn,
            usuario_id=self.usuario_id,
            tipo_transacao='Despesa',
            mensagem=self.mensagem,
            conta_nome_param=self.params.get("conta"),
            conta_id_agendamento=self.params.get("conta_id_agendamento")
        )

        if not conta_id:
            return {
                "error": "❌ Conta não encontrada. Por favor, especifique a conta."
            }

        # Buscar lista de categorias (sempre necessária para formatar mensagem)
        cats_list = finance_service.get_user_categories(self.conn, self.usuario_id, 'Despesa')
        
        # Buscar categoria
        # PRIORIDADE: Se encontrou agendamento, usar categoria do agendamento
        subcategoria_id_agendamento = self.params.get("subcategoria_id_agendamento")
        
        if subcategoria_id_agendamento:
            id_categoria = subcategoria_id_agendamento
            print(f"[DESPESA-INTENT] Usando categoria do agendamento: {id_categoria}")
        else:
            # Não tem agendamento, deixar Gemini categorizar
            id_outros = finance_service.get_fallback_category_id(self.conn, 'Despesa')
            id_categoria = gemini_service.categorize_transaction(
                cats_list, self.params["descricao"], 'Despesa', id_outros, self.usuario_id
            )

        # Preparar estrutura de dados
        data_transacao = self.params.get("data") or date.today()
        valor_original = self.params["valor"]

        # Detectar parcelamento
        num_parcelas = self.params.get("parcelamento")
        valor_total = None
        valor_db = -abs(valor_original)  # Despesa é negativo no banco

        if num_parcelas and num_parcelas > 1:
            valor_total = valor_original
            valor_original = valor_original / num_parcelas  # Valor da parcela
            valor_db = -abs(valor_original)

        # Determinar tipo de pagamento e fatura
        tipo_pagamento = 'debito'
        fatura_id = None

        # Se a conta é cartão de crédito, usar crédito
        if conta_tipo and 'crédito' in conta_tipo.lower():
            tipo_pagamento = 'credito'
            # Buscar fatura em aberto para esta conta
            fatura_id = finance_service.get_or_create_fatura(
                self.conn, conta_id, data_transacao, self.usuario_id
            )
            # CRÍTICO: Commit para persistir a fatura antes de salvar no Redis
            self.conn.commit()

        transacao_data = {
            'usuario_id': self.usuario_id,
            'conta_id': conta_id,
            'conta_nome': conta_nome,
            'conta_tipo': conta_tipo,
            'categoria_id': id_categoria,
            'fatura_id': fatura_id,
            'descricao': self.params["descricao"],
            'descricao_final': self.params.get("descricao_agendamento"),  # Usar descrição do agendamento se disponível
            'valor_db': valor_db,
            'valor_original': valor_original,
            'valor_total': valor_total,
            'num_parcelas': num_parcelas,
            'tipo_transacao': 'Despesa',
            'tipo_pagamento': tipo_pagamento,
            'data_transacao': str(data_transacao),
            'origem': 'whatsapp'
        }

        # Validar que temos numero_whatsapp
        if not self.numero_whatsapp:
            return {"error": "❌ Erro interno: número WhatsApp não disponível"}

        # Criar transação pendente no Redis
        tx_id = TransactionConfirmationService.create_pending_transaction(
            self.numero_whatsapp,
            transacao_data
        )

        if not tx_id:
            return {"error": "❌ Erro ao criar transação pendente"}

        return {
            "tx_id": tx_id,
            "transacao_data": transacao_data,
            "cats_list": cats_list,
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Usar o serviço existente para formatar
        msg_confirm = TransactionConfirmationService.format_confirmation_message(
            data["transacao_data"],
            data["cats_list"],
            data["tx_id"]
        )

        return msg_confirm


class TransferenciaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Transferência'.

    Transfere valor entre duas contas do usuário.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da transferência usando Gemini."""
        params = gemini_service.extract_transfer_params(
            self.mensagem,
            self.usuario_id,
            self.conn
        )

        return {
            "valor": params.get("valor"),
            "conta_origem": params.get("conta_origem"),
            "conta_destino": params.get("conta_destino"),
            "descricao": params.get("descricao", "Transferência"),
            "data": params.get("data"),  # date object ou None (hoje)
        }

    def validate(self) -> str | None:
        """Valida parâmetros extraídos."""
        if not self.params.get("valor") or self.params["valor"] <= 0:
            return "❌ Não consegui identificar o valor da transferência."

        if not self.params.get("conta_origem"):
            return "❌ Não consegui identificar a conta de origem. Especifique de qual conta transferir."

        if not self.params.get("conta_destino"):
            return "❌ Não consegui identificar a conta de destino. Especifique para qual conta transferir."

        # Nota: Validação de conta igual movida para execute() após resolver os IDs
        return None

    def execute(self) -> Dict[str, Any]:
        """Cria transferência pendente de confirmação."""
        print(f"[TRANSFER-EXECUTE] Iniciando execute() com params: {self.params}")
        
        # Buscar IDs das contas
        conta_origem_id = finance_service.get_account_by_name(
            self.conn,
            self.usuario_id,
            self.params["conta_origem"],
            fallback=True
        )

        conta_destino_id = finance_service.get_account_by_name(
            self.conn,
            self.usuario_id,
            self.params["conta_destino"],
            fallback=True
        )

        if not conta_origem_id or not conta_destino_id:
            return {
                "error": "❌ Uma ou ambas as contas não foram encontradas."
            }

        # Correção Bug #2: Valida se é a mesma conta APÓS resolver os IDs
        # Antes comparava strings ("Inter" != "Banco Inter"), agora compara IDs
        if conta_origem_id == conta_destino_id:
            return {
                "error": "❌ As contas de origem e destino devem ser diferentes."
            }

        # Correção Bug #1: Verifica saldo antes de criar a transferência
        print(f"[TRANSFER-VALIDATION] Buscando saldo da conta origem ID: {conta_origem_id}")
        saldos = finance_service.get_saldo_contas(self.conn, self.usuario_id, conta_origem_id)
        print(f"[TRANSFER-VALIDATION] Saldos retornados: {saldos}")
        saldo_origem = saldos[0]["saldo"] if saldos else 0  # Corrigido: usar "saldo" ao invés de "saldo_atual"
        print(f"[TRANSFER-VALIDATION] Saldo origem: R$ {saldo_origem:.2f}")
        print(f"[TRANSFER-VALIDATION] Valor solicitado: R$ {self.params['valor']:.2f}")

        if saldo_origem < self.params["valor"]:
            print(f"[TRANSFER-VALIDATION] ❌ SALDO INSUFICIENTE! Bloqueando transferência.")
            saldo_fmt = f"R$ {saldo_origem:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")
            valor_fmt = f"R$ {self.params['valor']:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")
            return {
                "error": f"❌ Saldo insuficiente na conta de origem.\n\n💰 Saldo disponível: {saldo_fmt}\n💸 Valor solicitado: {valor_fmt}"
            }
        
        print(f"[TRANSFER-VALIDATION] ✅ Saldo suficiente. Prosseguindo com transferência.")

        # Criar transferência pendente
        confirmation_service = TransactionConfirmationService()

        pending_id = confirmation_service.create_pending_transaction(
            usuario_id=self.usuario_id,
            tipo="transferencia",
            valor=self.params["valor"],
            descricao=self.params["descricao"],
            data=self.params.get("data"),
            conta_id=conta_origem_id,  # Origem
            conta_destino_id=conta_destino_id  # Destino
        )

        return {
            "pending_id": pending_id,
            "valor": self.params["valor"],
            "descricao": self.params["descricao"],
            "conta_origem_id": conta_origem_id,
            "conta_destino_id": conta_destino_id,
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Buscar nomes das contas
        contas = finance_service.get_user_accounts(self.conn, self.usuario_id)
        conta_origem = next((c for c in contas if c["id"] == data["conta_origem_id"]), None)
        conta_destino = next((c for c in contas if c["id"] == data["conta_destino_id"]), None)

        origem_nome = conta_origem["nome_conta"] if conta_origem else "Desconhecida"
        destino_nome = conta_destino["nome_conta"] if conta_destino else "Desconhecida"

        msg = "🔄 *Transferência a confirmar:*\n\n"
        msg += f"📝 Descrição: {data['descricao']}\n"
        msg += f"💵 Valor: {formatar_moeda(data['valor'])}\n\n"
        msg += f"📤 De: {origem_nome}\n"
        msg += f"📥 Para: {destino_nome}\n\n"
        msg += "Responda:\n"
        msg += "• *confirmar* - para realizar transferência\n"
        msg += "• *cancelar* - para descartar"

        return msg


class PagamentoFaturaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Pagamento Fatura'.

    Registra pagamento de fatura de cartão de crédito.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros do pagamento usando Gemini."""
        params = gemini_service.extract_invoice_payment_params(
            self.mensagem,
            self.usuario_id,
            self.conn
        )

        return {
            "valor": params.get("valor"),
            "cartao": params.get("cartao"),  # Nome do cartão
            "conta_pagamento": params.get("conta_pagamento"),  # Conta de onde pagar
            "descricao": params.get("descricao", "Pagamento de fatura"),
            "data": params.get("data"),  # date object ou None (hoje)
        }

    def validate(self) -> str | None:
        """Valida parâmetros extraídos."""
        # Correção Bug #3: Permite valor None (usará valor total da fatura)
        valor = self.params.get("valor")

        if valor is not None:
            try:
                valor_float = float(valor)
                if valor_float <= 0:
                    return "❌ Valor deve ser positivo."
                self.params["valor"] = valor_float  # Normaliza o valor
            except (ValueError, TypeError):
                return "❌ Valor inválido."

        if not self.params.get("cartao"):
            return "❌ Não consegui identificar qual cartão. Especifique o cartão de crédito."

        return None

    def execute(self) -> Dict[str, Any]:
        """Cria pagamento pendente de confirmação."""
        # Buscar ID do cartão (conta tipo crédito)
        # Correção Bug #4: Remove fallback=True para falhar explicitamente se cartão não encontrado
        # Antes retornava qualquer conta, agora só retorna se for cartão de crédito específico
        cartao_id = finance_service.get_account_by_name(
            self.conn,
            self.usuario_id,
            self.params["cartao"],
            fallback=False,
            tipo_conta="Cartão de Crédito"
        )

        if not cartao_id:
            return {
                "error": f"❌ Cartão '{self.params['cartao']}' não encontrado."
            }

        # Buscar conta de pagamento (ou usar padrão)
        if self.params.get("conta_pagamento"):
            conta_pag_id = finance_service.get_account_by_name(
                self.conn,
                self.usuario_id,
                self.params["conta_pagamento"],
                fallback=True
            )
        else:
            # Usar conta corrente padrão
            conta_id_renda, _ = finance_service.get_user_default_accounts(
                self.conn,
                self.usuario_id
            )
            conta_pag_id = conta_id_renda

        if not conta_pag_id:
            return {
                "error": "❌ Conta de pagamento não encontrada."
            }

        # Criar pagamento pendente
        confirmation_service = TransactionConfirmationService()

        pending_id = confirmation_service.create_pending_transaction(
            usuario_id=self.usuario_id,
            tipo="pagamento_fatura",
            valor=self.params["valor"],
            descricao=self.params["descricao"],
            data=self.params.get("data"),
            conta_id=conta_pag_id,  # Conta de onde sai o dinheiro
            cartao_id=cartao_id  # Cartão cuja fatura está sendo paga
        )

        return {
            "pending_id": pending_id,
            "valor": self.params["valor"],
            "descricao": self.params["descricao"],
            "cartao_id": cartao_id,
            "conta_pagamento_id": conta_pag_id,
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Buscar nomes das contas
        contas = finance_service.get_user_accounts(self.conn, self.usuario_id)
        cartao = next((c for c in contas if c["id"] == data["cartao_id"]), None)
        conta_pag = next((c for c in contas if c["id"] == data["conta_pagamento_id"]), None)

        cartao_nome = cartao["nome_conta"] if cartao else "Desconhecido"
        conta_pag_nome = conta_pag["nome_conta"] if conta_pag else "Desconhecida"

        msg = "💳 *Pagamento de Fatura a confirmar:*\n\n"
        msg += f"📝 Descrição: {data['descricao']}\n"
        msg += f"💵 Valor: {formatar_moeda(data['valor'])}\n\n"
        msg += f"💳 Cartão: {cartao_nome}\n"
        msg += f"🏦 Pagar com: {conta_pag_nome}\n\n"
        msg += "Responda:\n"
        msg += "• *confirmar* - para registrar pagamento\n"
        msg += "• *cancelar* - para descartar"

        return msg


__all__ = [
    'RendaIntent',
    'DespesaIntent',
    'TransferenciaIntent',
    'PagamentoFaturaIntent',
]
