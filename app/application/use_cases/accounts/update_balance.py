# app/application/use_cases/accounts/update_balance.py
"""
Use Case: Atualizar Saldo Inicial

Encapsula a atualização do saldo inicial de uma conta.
"""

from dataclasses import dataclass


@dataclass
class UpdateAccountBalanceInput:
    """Dados de entrada para atualização de saldo."""
    usuario_id: int
    conta_id: int
    novo_saldo_inicial: float


@dataclass
class UpdateAccountBalanceOutput:
    """Resultado da atualização."""
    success: bool
    saldo_anterior: float = 0.0
    saldo_novo: float = 0.0
    message: str = ""


class UpdateAccountBalanceUseCase:
    """
    Use case para atualização de saldo inicial de conta.
    
    Responsabilidades:
    - Validar permissão do usuário
    - Atualizar saldo inicial
    - Retornar valores antes/depois
    """
    
    def execute(self, input_data: UpdateAccountBalanceInput) -> UpdateAccountBalanceOutput:
        """
        Atualiza saldo inicial de uma conta.
        
        Args:
            input_data: Dados da atualização
            
        Returns:
            UpdateAccountBalanceOutput com resultado
        """
        from app.services.finance.account_service import (
            update_saldo_inicial,
            get_saldo_contas
        )
        from app.infrastructure.database.connection import get_db_connection
        
        try:
            with get_db_connection() as conn:
                # Buscar saldo atual
                saldos_antes = get_saldo_contas(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id=input_data.conta_id
                )
                
                saldo_anterior = saldos_antes[0]['saldo'] if saldos_antes else 0.0
                
                # Atualizar
                update_saldo_inicial(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id=input_data.conta_id,
                    novo_saldo_inicial=input_data.novo_saldo_inicial
                )
                
                # Buscar novo saldo
                saldos_depois = get_saldo_contas(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id=input_data.conta_id
                )
                
                saldo_novo = saldos_depois[0]['saldo'] if saldos_depois else input_data.novo_saldo_inicial
                
                conn.commit()
                
                return UpdateAccountBalanceOutput(
                    success=True,
                    saldo_anterior=saldo_anterior,
                    saldo_novo=saldo_novo,
                    message="Saldo inicial atualizado com sucesso"
                )
                
        except Exception as e:
            return UpdateAccountBalanceOutput(
                success=False,
                message=f"Erro ao atualizar saldo: {str(e)}"
            )


__all__ = [
    'UpdateAccountBalanceInput',
    'UpdateAccountBalanceOutput',
    'UpdateAccountBalanceUseCase',
]
