# app/application/use_cases/accounts/get_balance.py
"""
Use Case: Obter Saldo de Contas

Encapsula a consulta de saldos de contas do usuário.
"""

from dataclasses import dataclass, field
from typing import Optional, List


@dataclass
class GetAccountBalanceInput:
    """Dados de entrada para consulta de saldos."""
    usuario_id: int
    conta_id: Optional[int] = None  # None = todas as contas


@dataclass
class AccountBalance:
    """Saldo de uma conta."""
    conta_id: int
    nome_conta: str
    tipo_conta: str
    saldo: float


@dataclass
class GetAccountBalanceOutput:
    """Resultado da consulta de saldos."""
    success: bool
    balances: List[AccountBalance] = field(default_factory=list)
    total: float = 0.0
    message: str = ""


class GetAccountBalanceUseCase:
    """
    Use case para consulta de saldos de contas.
    
    Responsabilidades:
    - Buscar saldos de uma ou todas as contas
    - Calcular total agregado
    - Formatar resposta consistente
    """
    
    def execute(self, input_data: GetAccountBalanceInput) -> GetAccountBalanceOutput:
        """
        Consulta saldos de contas.
        
        Args:
            input_data: Filtros da consulta
            
        Returns:
            GetAccountBalanceOutput com saldos encontrados
        """
        from app.services.finance.account_service import get_saldo_contas
        from app.infrastructure.database.connection import get_db_connection
        
        try:
            with get_db_connection() as conn:
                saldos = get_saldo_contas(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    conta_id=input_data.conta_id
                )
                
                balances = [
                    AccountBalance(
                        conta_id=s.get('conta_id', 0),
                        nome_conta=s['nome_conta'],
                        tipo_conta=s['tipo_conta'],
                        saldo=s['saldo']
                    )
                    for s in saldos
                ]
                
                total = sum(b.saldo for b in balances)
                
                return GetAccountBalanceOutput(
                    success=True,
                    balances=balances,
                    total=total,
                    message=f"{len(balances)} conta(s) encontrada(s)"
                )
                
        except Exception as e:
            return GetAccountBalanceOutput(
                success=False,
                message=f"Erro ao consultar saldos: {str(e)}"
            )


__all__ = [
    'GetAccountBalanceInput',
    'AccountBalance',
    'GetAccountBalanceOutput',
    'GetAccountBalanceUseCase',
]
