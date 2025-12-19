# app/application/use_cases/transactions/create_transfer.py
"""
Use Case: Criar Transferência

Encapsula a lógica de transferência entre contas,
criando o par de transações (saída + entrada).
"""

from dataclasses import dataclass
from datetime import date
from typing import Optional


@dataclass
class CreateTransferInput:
    """Dados de entrada para criar uma transferência."""
    usuario_id: int
    conta_id_origem: int
    conta_id_destino: int
    valor: float  # Sempre positivo
    data_transacao: date
    descricao: Optional[str] = None


@dataclass
class CreateTransferOutput:
    """Resultado da criação de transferência."""
    success: bool
    transaction_id_saida: Optional[int] = None
    transaction_id_entrada: Optional[int] = None
    message: str = ""


class CreateTransferUseCase:
    """
    Use case para criação de transferências entre contas.
    
    Responsabilidades:
    - Validar que origem e destino são diferentes
    - Criar par de transações vinculadas
    - Garantir atomicidade da operação
    """
    
    def execute(self, input_data: CreateTransferInput) -> CreateTransferOutput:
        """
        Executa a transferência entre contas.
        
        Args:
            input_data: Dados da transferência
            
        Returns:
            CreateTransferOutput com resultado da operação
        """
        from app.services.finance.transaction_service import create_transfer_pair
        from app.infrastructure.database.connection import get_db_connection
        
        # Validação
        if input_data.conta_id_origem == input_data.conta_id_destino:
            return CreateTransferOutput(
                success=False,
                message="Conta de origem e destino não podem ser iguais"
            )
        
        if input_data.valor <= 0:
            return CreateTransferOutput(
                success=False,
                message="Valor da transferência deve ser positivo"
            )
        
        try:
            with get_db_connection() as conn:
                id_saida, id_entrada = create_transfer_pair(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id_origem=input_data.conta_id_origem,
                    conta_id_destino=input_data.conta_id_destino,
                    valor=input_data.valor,
                    data_transacao=input_data.data_transacao
                )
                
                conn.commit()
                
                return CreateTransferOutput(
                    success=True,
                    transaction_id_saida=id_saida,
                    transaction_id_entrada=id_entrada,
                    message=f"Transferência realizada com sucesso"
                )
                
        except Exception as e:
            return CreateTransferOutput(
                success=False,
                message=f"Erro na transferência: {str(e)}"
            )


__all__ = [
    'CreateTransferInput',
    'CreateTransferOutput',
    'CreateTransferUseCase',
]
