"""
Formatador de notificações de faturas de cartão de crédito.
Centraliza formatação de mensagens WhatsApp para eventos de invoice.
"""

from typing import Dict
from datetime import date


class InvoiceNotificationFormatter:
    """Formatador de notificações de faturas."""

    @staticmethod
    def format_invoice_closed(invoice: Dict) -> str:
        """
        Formata notificação de fatura fechada.

        Args:
            invoice: {
                'nome_conta': str,
                'data_fechamento': date,
                'data_vencimento': date,
                'valor_total': float
            }

        Returns:
            str: Mensagem WhatsApp formatada
        """
        valor_formatado = f"{invoice['valor_total']:.2f}".replace('.', ',')

        msg = (
            f"💳 *FATURA FECHADA*\n\n"
            f"Cartão: *{invoice['nome_conta']}*\n"
            f"Fechamento: {invoice['data_fechamento'].strftime('%d/%m/%Y')}\n"
            f"Vencimento: {invoice['data_vencimento'].strftime('%d/%m/%Y')}\n"
            f"Valor Total: *R$ {valor_formatado}*\n\n"
            f"ℹ️ A partir de agora, novas compras entrarão na próxima fatura."
        )

        return msg

    @staticmethod
    def format_due_date_warning(invoice: Dict) -> str:
        """
        Formata alerta de vencimento próximo.

        Args:
            invoice: {
                'nome_conta': str,
                'data_vencimento': date,
                'valor_total': float,
                'dias_ate_vencimento': int
            }
        """
        valor_formatado = f"{invoice['valor_total']:.2f}".replace('.', ',')
        dias = invoice['dias_ate_vencimento']

        emoji = "⚠️" if dias <= 1 else "🔔"
        dias_texto = "AMANHÃ" if dias == 1 else f"em {dias} dias"

        msg = (
            f"{emoji} *VENCIMENTO PRÓXIMO*\n\n"
            f"Cartão: *{invoice['nome_conta']}*\n"
            f"Vence: {dias_texto} ({invoice['data_vencimento'].strftime('%d/%m/%Y')})\n"
            f"Valor: *R$ {valor_formatado}*\n\n"
            f"💡 Não se esqueça de garantir saldo para o pagamento!"
        )

        return msg

    @staticmethod
    def format_overdue_alert(invoice: Dict) -> str:
        """
        Formata alerta de fatura vencida.

        Args:
            invoice: {
                'nome_conta': str,
                'data_vencimento': date,
                'valor_total': float,
                'dias_atrasado': int
            }
        """
        valor_formatado = f"{invoice['valor_total']:.2f}".replace('.', ',')
        dias = invoice['dias_atrasado']

        msg = (
            f"🚨 *FATURA VENCIDA*\n\n"
            f"Cartão: *{invoice['nome_conta']}*\n"
            f"Venceu há: *{dias} dia(s)*\n"
            f"Data de Vencimento: {invoice['data_vencimento'].strftime('%d/%m/%Y')}\n"
            f"Valor: *R$ {valor_formatado}*\n\n"
            f"⚠️ Pague o quanto antes para evitar juros e multas!"
        )

        return msg


__all__ = ['InvoiceNotificationFormatter']
