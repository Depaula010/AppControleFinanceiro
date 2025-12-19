# app/application/use_cases/invoices/pay_invoice.py
"""
Use Case: Pagar Fatura

Encapsula a lógica de pagamento de fatura de cartão de crédito.
"""

from dataclasses import dataclass
from datetime import date
from typing import Optional


@dataclass
class PayInvoiceInput:
    """Dados de entrada para pagamento de fatura."""
    usuario_id: int
    conta_id_origem: int  # Conta corrente que vai pagar
    conta_id_cartao: int  # Cartão de crédito
    valor: float  # Valor a pagar
    data_pagamento: date


@dataclass
class PayInvoiceOutput:
    """Resultado do pagamento de fatura."""
    success: bool
    transaction_id_pagamento: Optional[int] = None
    transaction_id_recebimento: Optional[int] = None
    message: str = ""


class PayInvoiceUseCase:
    """
    Use case para pagamento de fatura de cartão.
    
    Responsabilidades:
    - Validar que conta origem não é cartão
    - Criar par de transações (pagamento + recebimento)
    - Marcar fatura como paga (se valor total)
    """
    
    def execute(self, input_data: PayInvoiceInput) -> PayInvoiceOutput:
        """
        Processa pagamento de fatura.
        
        Args:
            input_data: Dados do pagamento
            
        Returns:
            PayInvoiceOutput com resultado
        """
        from app.services.finance.transaction_service import create_fatura_payment
        from app.infrastructure.database.connection import get_db_connection
        
        # Validações
        if input_data.conta_id_origem == input_data.conta_id_cartao:
            return PayInvoiceOutput(
                success=False,
                message="Conta de origem não pode ser o próprio cartão"
            )
        
        if input_data.valor <= 0:
            return PayInvoiceOutput(
                success=False,
                message="Valor do pagamento deve ser positivo"
            )
        
        try:
            with get_db_connection() as conn:
                id_pagamento, id_recebimento = create_fatura_payment(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id_origem=input_data.conta_id_origem,
                    conta_id_cartao=input_data.conta_id_cartao,
                    valor=input_data.valor,
                    data_transacao=input_data.data_pagamento
                )
                
                conn.commit()
                
                return PayInvoiceOutput(
                    success=True,
                    transaction_id_pagamento=id_pagamento,
                    transaction_id_recebimento=id_recebimento,
                    message=f"Fatura paga com sucesso"
                )
                
        except Exception as e:
            return PayInvoiceOutput(
                success=False,
                message=f"Erro ao pagar fatura: {str(e)}"
            )


__all__ = [
    'PayInvoiceInput',
    'PayInvoiceOutput',
    'PayInvoiceUseCase',
]
