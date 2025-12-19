# app/application/use_cases/reports/monthly_report.py
"""
Use Case: Relatório Mensal

Encapsula a geração de relatório financeiro mensal.
"""

from dataclasses import dataclass, field
from datetime import date
from typing import List
from calendar import monthrange


@dataclass
class MonthlyReportInput:
    """Dados de entrada para relatório mensal."""
    usuario_id: int
    ano: int
    mes: int  # 1-12


@dataclass
class CategorySummary:
    """Resumo por categoria."""
    categoria_nome: str
    total: float
    percentual: float


@dataclass
class MonthlyReportOutput:
    """Resultado do relatório mensal."""
    success: bool
    periodo: str = ""
    total_receitas: float = 0.0
    total_despesas: float = 0.0
    saldo_periodo: float = 0.0
    categorias_despesas: List[CategorySummary] = field(default_factory=list)
    categorias_receitas: List[CategorySummary] = field(default_factory=list)
    message: str = ""


class GenerateMonthlyReportUseCase:
    """
    Use case para geração de relatório mensal.
    
    Responsabilidades:
    - Calcular totais de receitas e despesas
    - Agrupar por categoria
    - Calcular percentuais
    """
    
    def execute(self, input_data: MonthlyReportInput) -> MonthlyReportOutput:
        """
        Gera relatório mensal.
        
        Args:
            input_data: Parâmetros do relatório
            
        Returns:
            MonthlyReportOutput com dados do relatório
        """
        from app.infrastructure.database.connection import get_db_connection
        from sqlalchemy import text
        
        try:
            # Calcular período
            _, ultimo_dia = monthrange(input_data.ano, input_data.mes)
            data_inicio = date(input_data.ano, input_data.mes, 1)
            data_fim = date(input_data.ano, input_data.mes, ultimo_dia)
            
            with get_db_connection() as conn:
                # Query para totais por tipo
                sql_totais = text("""
                    SELECT 
                        tipo_transacao,
                        SUM(valor) as total
                    FROM Transacoes
                    WHERE usuario_id = :usuario_id
                      AND data_transacao >= :data_inicio
                      AND data_transacao <= :data_fim
                      AND tipo_transacao IN ('Renda', 'Despesa')
                    GROUP BY tipo_transacao
                """)
                
                result_totais = conn.execute(sql_totais, {
                    "usuario_id": input_data.usuario_id,
                    "data_inicio": data_inicio,
                    "data_fim": data_fim
                }).fetchall()
                
                total_receitas = 0.0
                total_despesas = 0.0
                
                for row in result_totais:
                    if row[0] == 'Renda':
                        total_receitas = float(row[1])
                    elif row[0] == 'Despesa':
                        total_despesas = abs(float(row[1]))
                
                # Query para despesas por categoria
                sql_categorias = text("""
                    SELECT 
                        COALESCE(cat.nome, 'Sem categoria') as categoria,
                        SUM(ABS(t.valor)) as total
                    FROM Transacoes t
                    LEFT JOIN Subcategorias s ON t.subcategoria_id = s.id
                    LEFT JOIN Categorias cat ON s.categoria_id = cat.id
                    WHERE t.usuario_id = :usuario_id
                      AND t.data_transacao >= :data_inicio
                      AND t.data_transacao <= :data_fim
                      AND t.tipo_transacao = :tipo
                    GROUP BY cat.nome
                    ORDER BY total DESC
                """)
                
                # Despesas por categoria
                result_desp = conn.execute(sql_categorias, {
                    "usuario_id": input_data.usuario_id,
                    "data_inicio": data_inicio,
                    "data_fim": data_fim,
                    "tipo": "Despesa"
                }).fetchall()
                
                categorias_despesas = [
                    CategorySummary(
                        categoria_nome=row[0],
                        total=float(row[1]),
                        percentual=round(float(row[1]) / total_despesas * 100, 1) if total_despesas > 0 else 0
                    )
                    for row in result_desp
                ]
                
                # Receitas por categoria
                result_rec = conn.execute(sql_categorias, {
                    "usuario_id": input_data.usuario_id,
                    "data_inicio": data_inicio,
                    "data_fim": data_fim,
                    "tipo": "Renda"
                }).fetchall()
                
                categorias_receitas = [
                    CategorySummary(
                        categoria_nome=row[0],
                        total=float(row[1]),
                        percentual=round(float(row[1]) / total_receitas * 100, 1) if total_receitas > 0 else 0
                    )
                    for row in result_rec
                ]
                
                meses = ['', 'Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho',
                         'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro']
                
                return MonthlyReportOutput(
                    success=True,
                    periodo=f"{meses[input_data.mes]} {input_data.ano}",
                    total_receitas=total_receitas,
                    total_despesas=total_despesas,
                    saldo_periodo=total_receitas - total_despesas,
                    categorias_despesas=categorias_despesas,
                    categorias_receitas=categorias_receitas,
                    message="Relatório gerado com sucesso"
                )
                
        except Exception as e:
            return MonthlyReportOutput(
                success=False,
                message=f"Erro ao gerar relatório: {str(e)}"
            )


__all__ = [
    'MonthlyReportInput',
    'CategorySummary',
    'MonthlyReportOutput',
    'GenerateMonthlyReportUseCase',
]
