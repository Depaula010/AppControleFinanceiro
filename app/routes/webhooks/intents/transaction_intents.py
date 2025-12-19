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
            self.usuario_id
        )

        return {
            "valor": params.get("valor"),
            "descricao": params.get("descricao", "Renda"),
            "data": params.get("data"),  # date object ou None (hoje)
            "conta": params.get("conta"),  # nome da conta
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
        # Buscar ou escolher conta
        conta_nome = self.params.get("conta")
        if conta_nome:
            conta_id = finance_service.get_account_by_name(
                self.conn,
                self.usuario_id,
                conta_nome,
                fallback=True
            )
        else:
            # Escolher conta padrão para renda
            conta_id_renda, _ = finance_service.get_user_default_accounts(
                self.conn,
                self.usuario_id
            )
            conta_id = conta_id_renda

        if not conta_id:
            return {
                "error": "❌ Conta não encontrada. Por favor, especifique a conta."
            }

        # Criar transação pendente
        confirmation_service = TransactionConfirmationService()

        pending_id = confirmation_service.create_pending_transaction(
            usuario_id=self.usuario_id,
            tipo="renda",
            valor=self.params["valor"],
            descricao=self.params["descricao"],
            data=self.params.get("data"),
            conta_id=conta_id
        )

        return {
            "pending_id": pending_id,
            "valor": self.params["valor"],
            "descricao": self.params["descricao"],
            "conta_id": conta_id,
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Buscar nome da conta
        contas = finance_service.get_user_accounts(self.conn, self.usuario_id)
        conta = next((c for c in contas if c["id"] == data["conta_id"]), None)
        conta_nome = conta["nome_conta"] if conta else "Desconhecida"

        msg = "💰 *Renda a confirmar:*\n\n"
        msg += f"📝 Descrição: {data['descricao']}\n"
        msg += f"💵 Valor: {formatar_moeda(data['valor'])}\n"
        msg += f"🏦 Conta: {conta_nome}\n\n"
        msg += "Responda:\n"
        msg += "• *confirmar* - para registrar\n"
        msg += "• *cancelar* - para descartar"

        return msg


class DespesaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Despesa'.

    Registra uma despesa com sistema de confirmação e categorização.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da despesa usando Gemini."""
        params = gemini_service.extract_expense_params(
            self.mensagem,
            self.usuario_id
        )

        return {
            "valor": params.get("valor"),
            "descricao": params.get("descricao", "Despesa"),
            "data": params.get("data"),
            "conta": params.get("conta"),
            "categoria": params.get("categoria"),  # Pode ser None
            "parcelamento": params.get("parcelamento"),  # Núm. parcelas ou None
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
        # Buscar ou escolher conta
        conta_nome = self.params.get("conta")
        if conta_nome:
            conta_id = finance_service.get_account_by_name(
                self.conn,
                self.usuario_id,
                conta_nome,
                fallback=True
            )
        else:
            # Escolher conta padrão para despesa
            _, conta_id_despesa = finance_service.get_user_default_accounts(
                self.conn,
                self.usuario_id
            )
            conta_id = conta_id_despesa

        if not conta_id:
            return {
                "error": "❌ Conta não encontrada. Por favor, especifique a conta."
            }

        # Criar transação pendente
        confirmation_service = TransactionConfirmationService()

        pending_id = confirmation_service.create_pending_transaction(
            usuario_id=self.usuario_id,
            tipo="despesa",
            valor=self.params["valor"],
            descricao=self.params["descricao"],
            data=self.params.get("data"),
            conta_id=conta_id,
            categoria=self.params.get("categoria"),
            parcelamento=self.params.get("parcelamento")
        )

        return {
            "pending_id": pending_id,
            "valor": self.params["valor"],
            "descricao": self.params["descricao"],
            "conta_id": conta_id,
            "parcelamento": self.params.get("parcelamento"),
            "needs_confirmation": True
        }

    def format_response(self, data: Dict[str, Any]) -> str:
        """Formata mensagem de confirmação."""
        if "error" in data:
            return data["error"]

        # Buscar nome da conta
        contas = finance_service.get_user_accounts(self.conn, self.usuario_id)
        conta = next((c for c in contas if c["id"] == data["conta_id"]), None)
        conta_nome = conta["nome_conta"] if conta else "Desconhecida"

        msg = "💸 *Despesa a confirmar:*\n\n"
        msg += f"📝 Descrição: {data['descricao']}\n"
        msg += f"💵 Valor: {formatar_moeda(data['valor'])}\n"
        msg += f"🏦 Conta: {conta_nome}\n"

        # Parcelamento se presente
        if data.get("parcelamento"):
            msg += f"📊 Parcelas: {data['parcelamento']}x de {formatar_moeda(data['valor'] / data['parcelamento'])}\n"

        msg += "\nResponda:\n"
        msg += "• *confirmar* - para registrar\n"
        msg += "• *cancelar* - para descartar"

        return msg


class TransferenciaIntent(ConfirmationRequiredIntent):
    """
    Handler para intent 'Transferência'.

    Transfere valor entre duas contas do usuário.
    """

    def extract_params(self) -> Dict[str, Any]:
        """Extrai parâmetros da transferência usando Gemini."""
        params = gemini_service.extract_transfer_params(
            self.mensagem,
            self.usuario_id
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

        # Validar que origem != destino
        if self.params.get("conta_origem") == self.params.get("conta_destino"):
            return "❌ A conta de origem deve ser diferente da conta de destino."

        return None

    def execute(self) -> Dict[str, Any]:
        """Cria transferência pendente de confirmação."""
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
            self.usuario_id
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
        if not self.params.get("valor") or self.params["valor"] <= 0:
            return "❌ Não consegui identificar o valor do pagamento."

        if not self.params.get("cartao"):
            return "❌ Não consegui identificar qual cartão. Especifique o cartão de crédito."

        return None

    def execute(self) -> Dict[str, Any]:
        """Cria pagamento pendente de confirmação."""
        # Buscar ID do cartão (conta tipo crédito)
        cartao_id = finance_service.get_account_by_name(
            self.conn,
            self.usuario_id,
            self.params["cartao"],
            fallback=True,
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
