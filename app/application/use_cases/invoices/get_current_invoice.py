# app/application/use_cases/invoices/get_current_invoice.py
"""
Use Case: Obter Fatura Atual

Encapsula a consulta de faturas abertas de cartões de crédito.
"""

from dataclasses import dataclass, field
from datetime import date
from typing import Optional, List


@dataclass
class GetCurrentInvoiceInput:
    """Dados de entrada para consulta de faturas."""
    usuario_id: int
    conta_id_cartao: Optional[int] = None  # None = todos os cartões


@dataclass
class InvoiceInfo:
    """Informações de uma fatura."""
    nome_cartao: str
    valor_fatura: float
    data_vencimento: date
    status: str


@dataclass
class GetCurrentInvoiceOutput:
    """Resultado da consulta de faturas."""
    success: bool
    invoices: List[InvoiceInfo] = field(default_factory=list)
    total: float = 0.0
    message: str = ""


class GetCurrentInvoiceUseCase:
    """
    Use case para consulta de faturas abertas.
    
    Responsabilidades:
    - Garantir que existem faturas para o período atual
    - Buscar faturas abertas
    - Calcular total de faturas
    """
    
    def execute(self, input_data: GetCurrentInvoiceInput) -> GetCurrentInvoiceOutput:
        """
        Consulta faturas abertas.
        
        Args:
            input_data: Filtros da consulta
            
        Returns:
            GetCurrentInvoiceOutput com faturas encontradas
        """
        from app.services.finance.invoice_service import get_fatura_valor
        from app.infrastructure.database.connection import get_db_connection
        
        try:
            with get_db_connection() as conn:
                faturas = get_fatura_valor(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id_cartao=input_data.conta_id_cartao
                )
                
                invoices = [
                    InvoiceInfo(
                        nome_cartao=f['nome_cartao'],
                        valor_fatura=f['valor_fatura'],
                        data_vencimento=f['data_vencimento'],
                        status=f['status']
                    )
                    for f in faturas
                ]
                
                total = sum(i.valor_fatura for i in invoices)
                
                return GetCurrentInvoiceOutput(
                    success=True,
                    invoices=invoices,
                    total=total,
                    message=f"{len(invoices)} fatura(s) encontrada(s)"
                )
                
        except Exception as e:
            return GetCurrentInvoiceOutput(
                success=False,
                message=f"Erro ao consultar faturas: {str(e)}"
            )


__all__ = [
    'GetCurrentInvoiceInput',
    'InvoiceInfo',
    'GetCurrentInvoiceOutput',
    'GetCurrentInvoiceUseCase',
]
