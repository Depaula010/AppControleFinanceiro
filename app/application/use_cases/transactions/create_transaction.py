# app/application/use_cases/transactions/create_transaction.py
"""
Use Case: Criar Transação

Encapsula a lógica de criação de transações simples (Renda/Despesa),
incluindo determinação automática de fatura para cartões de crédito.
"""

from dataclasses import dataclass
from datetime import date
from typing import Optional


@dataclass
class CreateTransactionInput:
    """Dados de entrada para criar uma transação."""
    usuario_id: int
    conta_id: int
    conta_tipo: str  # 'Conta Corrente', 'Cartão de Crédito', etc.
    subcategoria_id: int
    descricao: str
    valor: float  # Positivo para renda, negativo para despesa
    tipo_transacao: str  # 'Renda' ou 'Despesa'
    data_transacao: date
    fatura_id: Optional[int] = None  # Se None, será determinado automaticamente


@dataclass
class CreateTransactionOutput:
    """Resultado da criação de transação."""
    success: bool
    transaction_id: Optional[int] = None
    fatura_id: Optional[int] = None
    message: str = ""


class CreateTransactionUseCase:
    """
    Use case para criação de transações.
    
    Responsabilidades:
    - Determinar fatura automaticamente se conta for cartão de crédito
    - Validar dados de entrada
    - Orquestrar chamada ao serviço de transações
    """
    
    def execute(self, input_data: CreateTransactionInput) -> CreateTransactionOutput:
        """
        Executa a criação de uma transação.
        
        Args:
            input_data: Dados da transação a criar
            
        Returns:
            CreateTransactionOutput com resultado da operação
        """
        from app.services.finance.invoice_service import get_fatura_id_if_credit_card
        from app.services.finance.transaction_service import create_transaction
        from app.infrastructure.database.connection import get_db_connection
        
        try:
            with get_db_connection() as conn:
                # Determinar fatura_id se for cartão de crédito
                fatura_id = input_data.fatura_id
                if fatura_id is None and input_data.conta_tipo == 'Cartão de Crédito':
                    fatura_id = get_fatura_id_if_credit_card(
                        conn=conn,
                        conta_id=input_data.conta_id,
                        conta_tipo=input_data.conta_tipo,
                        data_transacao=input_data.data_transacao,
                        usuario_id=input_data.usuario_id
                    )
                
                # Garantir valor negativo para despesas
                valor = input_data.valor
                if input_data.tipo_transacao == 'Despesa' and valor > 0:
                    valor = -valor
                
                # Criar transação
                transaction_id = create_transaction(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id=input_data.conta_id,
                    subcategoria_id=input_data.subcategoria_id,
                    fatura_id=fatura_id,
                    descricao=input_data.descricao,
                    valor=valor,
                    tipo_transacao=input_data.tipo_transacao,
                    data_transacao=input_data.data_transacao
                )
                
                conn.commit()
                
                return CreateTransactionOutput(
                    success=True,
                    transaction_id=transaction_id,
                    fatura_id=fatura_id,
                    message=f"Transação criada com sucesso (ID: {transaction_id})"
                )
                
        except Exception as e:
            return CreateTransactionOutput(
                success=False,
                message=f"Erro ao criar transação: {str(e)}"
            )


__all__ = [
    'CreateTransactionInput',
    'CreateTransactionOutput',
    'CreateTransactionUseCase',
]
