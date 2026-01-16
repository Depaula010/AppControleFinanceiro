# app/application/use_cases/budget/validate_budget.py
"""
Use Case: Validar Limite de Pote de Gastos

Encapsula a lógica de verificar se uma transação do tipo Despesa
ultrapassaria o limite de algum pote de gastos associado à subcategoria.
"""

from dataclasses import dataclass, field
from datetime import date
from typing import List, Optional


@dataclass
class ValidateBudgetInput:
    """Dados de entrada para validar orçamento."""
    usuario_id: int
    subcategoria_id: int
    valor_transacao: float  # Valor absoluto (positivo)
    data_transacao: date


@dataclass
class BudgetValidationDetail:
    """Detalhes da validação de um pote específico."""
    pote_id: int
    nome_pote: str
    valor_limite: float
    valor_gasto_atual: float
    valor_apos_transacao: float
    percentual_usado: float
    ultrapassaria_limite: bool


@dataclass
class ValidateBudgetOutput:
    """Resultado da validação de orçamento."""
    success: bool
    pode_prosseguir: bool
    requer_confirmacao: bool = False
    validacoes: List[BudgetValidationDetail] = field(default_factory=list)
    mensagem: str = ""


class ValidateBudgetUseCase:
    """
    Use case para validar limite de potes de gastos.

    Responsabilidades:
    - Verificar se a subcategoria está associada a algum pote
    - Calcular gasto atual no período do pote
    - Determinar se a transação ultrapassaria o limite
    - Retornar resultado com detalhes da validação
    """

    def execute(self, input_data: ValidateBudgetInput) -> ValidateBudgetOutput:
        """
        Executa a validação de orçamento.

        Args:
            input_data: Dados da transação a validar

        Returns:
            ValidateBudgetOutput com resultado da validação
        """
        from app.services.finance.budget_validation_service import (
            validate_budget,
            BudgetValidationResult,
        )
        from app.infrastructure.database.connection import get_db_connection

        try:
            with get_db_connection() as conn:
                result = validate_budget(
                    conn=conn,
                    usuario_id=input_data.usuario_id,
                    subcategoria_id=input_data.subcategoria_id,
                    valor_transacao=input_data.valor_transacao,
                    data_transacao=input_data.data_transacao
                )

                # Converter resultados do service para DTOs do use case
                validacoes = [
                    BudgetValidationDetail(
                        pote_id=v.pote_id,
                        nome_pote=v.nome_pote,
                        valor_limite=v.valor_limite,
                        valor_gasto_atual=v.valor_gasto_atual,
                        valor_apos_transacao=v.valor_apos_transacao,
                        percentual_usado=v.percentual_usado,
                        ultrapassaria_limite=v.ultrapassaria_limite
                    )
                    for v in result.validacoes
                ]

                return ValidateBudgetOutput(
                    success=True,
                    pode_prosseguir=result.pode_prosseguir,
                    requer_confirmacao=result.requer_confirmacao,
                    validacoes=validacoes,
                    mensagem=result.mensagem
                )

        except Exception as e:
            return ValidateBudgetOutput(
                success=False,
                pode_prosseguir=True,  # Em caso de erro, permite prosseguir
                requer_confirmacao=False,
                mensagem=f"Erro ao validar orçamento: {str(e)}"
            )


__all__ = [
    'ValidateBudgetInput',
    'BudgetValidationDetail',
    'ValidateBudgetOutput',
    'ValidateBudgetUseCase',
]
