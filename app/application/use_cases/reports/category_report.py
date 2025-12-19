# app/application/use_cases/reports/category_report.py
"""
Use Case: Relatório por Categoria

Encapsula a geração de relatório de gastos por categoria.
"""

from dataclasses import dataclass, field
from datetime import date
from typing import Optional, List


@dataclass
class CategoryReportInput:
    """Dados de entrada para relatório por categoria."""
    usuario_id: int
    categoria_id: Optional[int] = None  # None = todas as categorias
    data_inicio: Optional[date] = None
    data_fim: Optional[date] = None


@dataclass
class SubcategorySummary:
    """Resumo por subcategoria."""
    subcategoria_nome: str
    total: float
    quantidade: int


@dataclass
class CategoryDetail:
    """Detalhes de uma categoria."""
    categoria_id: int
    categoria_nome: str
    total: float
    quantidade: int
    subcategorias: List[SubcategorySummary] = field(default_factory=list)


@dataclass
class GenerateCategoryReportOutput:
    """Resultado do relatório por categoria."""
    success: bool
    categorias: List[CategoryDetail] = field(default_factory=list)
    total_geral: float = 0.0
    message: str = ""


class GenerateCategoryReportUseCase:
    """
    Use case para relatório de gastos por categoria.
    
    Responsabilidades:
    - Agrupar gastos por categoria e subcategoria
    - Calcular totais e quantidades
    - Filtrar por período opcional
    """
    
    def execute(self, input_data: CategoryReportInput) -> GenerateCategoryReportOutput:
        """
        Gera relatório por categoria.
        
        Args:
            input_data: Parâmetros do relatório
            
        Returns:
            GenerateCategoryReportOutput com dados do relatório
        """
        from app.infrastructure.database.connection import get_db_connection
        from sqlalchemy import text
        
        try:
            with get_db_connection() as conn:
                # Construir where dinâmico
                where_clauses = ["t.usuario_id = :usuario_id", "t.tipo_transacao = 'Despesa'"]
                params = {"usuario_id": input_data.usuario_id}
                
                if input_data.categoria_id:
                    where_clauses.append("cat.id = :categoria_id")
                    params["categoria_id"] = input_data.categoria_id
                
                if input_data.data_inicio:
                    where_clauses.append("t.data_transacao >= :data_inicio")
                    params["data_inicio"] = input_data.data_inicio
                
                if input_data.data_fim:
                    where_clauses.append("t.data_transacao <= :data_fim")
                    params["data_fim"] = input_data.data_fim
                
                where_sql = " AND ".join(where_clauses)
                
                # Query para totais por categoria e subcategoria
                sql = text(f"""
                    SELECT 
                        cat.id as cat_id,
                        COALESCE(cat.nome, 'Sem categoria') as categoria,
                        s.nome as subcategoria,
                        SUM(ABS(t.valor)) as total,
                        COUNT(*) as quantidade
                    FROM Transacoes t
                    LEFT JOIN Subcategorias s ON t.subcategoria_id = s.id
                    LEFT JOIN Categorias cat ON s.categoria_id = cat.id
                    WHERE {where_sql}
                    GROUP BY cat.id, cat.nome, s.nome
                    ORDER BY cat.nome, total DESC
                """)
                
                result = conn.execute(sql, params).fetchall()
                
                # Agrupar por categoria
                categorias_dict = {}
                
                for row in result:
                    cat_id = row[0] or 0
                    cat_nome = row[1]
                    subcat_nome = row[2] or 'Não especificado'
                    total = float(row[3])
                    qtd = row[4]
                    
                    if cat_id not in categorias_dict:
                        categorias_dict[cat_id] = CategoryDetail(
                            categoria_id=cat_id,
                            categoria_nome=cat_nome,
                            total=0.0,
                            quantidade=0,
                            subcategorias=[]
                        )
                    
                    categorias_dict[cat_id].total += total
                    categorias_dict[cat_id].quantidade += qtd
                    categorias_dict[cat_id].subcategorias.append(
                        SubcategorySummary(
                            subcategoria_nome=subcat_nome,
                            total=total,
                            quantidade=qtd
                        )
                    )
                
                categorias = list(categorias_dict.values())
                categorias.sort(key=lambda x: x.total, reverse=True)
                
                total_geral = sum(c.total for c in categorias)
                
                return GenerateCategoryReportOutput(
                    success=True,
                    categorias=categorias,
                    total_geral=total_geral,
                    message=f"{len(categorias)} categoria(s) no relatório"
                )
                
        except Exception as e:
            return GenerateCategoryReportOutput(
                success=False,
                message=f"Erro ao gerar relatório: {str(e)}"
            )


__all__ = [
    'CategoryReportInput',
    'SubcategorySummary',
    'CategoryDetail',
    'GenerateCategoryReportOutput',
    'GenerateCategoryReportUseCase',
]
