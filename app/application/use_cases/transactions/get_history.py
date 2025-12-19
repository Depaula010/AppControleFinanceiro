# app/application/use_cases/transactions/get_history.py
"""
Use Case: Obter Histórico de Transações

Encapsula a consulta de histórico de transações com filtros.
"""

from dataclasses import dataclass, field
from datetime import date
from typing import Optional, List


@dataclass
class TransactionHistoryFilter:
    """Filtros para consulta de histórico."""
    usuario_id: int
    conta_id: Optional[int] = None
    tipo_transacao: Optional[str] = None  # 'Renda', 'Despesa', 'Transferência'
    data_inicio: Optional[date] = None
    data_fim: Optional[date] = None
    categoria_id: Optional[int] = None
    limit: int = 50
    offset: int = 0


@dataclass
class TransactionItem:
    """Uma transação no histórico."""
    id: int
    descricao: str
    valor: float
    tipo_transacao: str
    data_transacao: date
    conta_nome: str
    categoria_nome: Optional[str] = None
    subcategoria_nome: Optional[str] = None


@dataclass
class GetTransactionHistoryOutput:
    """Resultado da consulta de histórico."""
    success: bool
    transactions: List[TransactionItem] = field(default_factory=list)
    total_count: int = 0
    message: str = ""


class GetTransactionHistoryUseCase:
    """
    Use case para consulta de histórico de transações.
    
    Responsabilidades:
    - Aplicar filtros de consulta
    - Paginar resultados
    - Enriquecer dados com nomes de categorias/contas
    """
    
    def execute(self, filter_data: TransactionHistoryFilter) -> GetTransactionHistoryOutput:
        """
        Consulta histórico de transações.
        
        Args:
            filter_data: Filtros da consulta
            
        Returns:
            GetTransactionHistoryOutput com transações encontradas
        """
        from app.infrastructure.database.connection import get_db_connection
        from sqlalchemy import text
        
        try:
            with get_db_connection() as conn:
                # Construir query dinâmica
                where_clauses = ["t.usuario_id = :usuario_id"]
                params = {"usuario_id": filter_data.usuario_id}
                
                if filter_data.conta_id:
                    where_clauses.append("t.conta_id = :conta_id")
                    params["conta_id"] = filter_data.conta_id
                
                if filter_data.tipo_transacao:
                    where_clauses.append("t.tipo_transacao = :tipo_transacao")
                    params["tipo_transacao"] = filter_data.tipo_transacao
                
                if filter_data.data_inicio:
                    where_clauses.append("t.data_transacao >= :data_inicio")
                    params["data_inicio"] = filter_data.data_inicio
                
                if filter_data.data_fim:
                    where_clauses.append("t.data_transacao <= :data_fim")
                    params["data_fim"] = filter_data.data_fim
                
                if filter_data.categoria_id:
                    where_clauses.append("s.categoria_id = :categoria_id")
                    params["categoria_id"] = filter_data.categoria_id
                
                where_sql = " AND ".join(where_clauses)
                
                # Query principal
                sql = text(f"""
                    SELECT 
                        t.id,
                        t.descricao,
                        t.valor,
                        t.tipo_transacao,
                        t.data_transacao,
                        c.nome_conta,
                        cat.nome as categoria_nome,
                        s.nome as subcategoria_nome
                    FROM Transacoes t
                    JOIN Contas c ON t.conta_id = c.id
                    LEFT JOIN Subcategorias s ON t.subcategoria_id = s.id
                    LEFT JOIN Categorias cat ON s.categoria_id = cat.id
                    WHERE {where_sql}
                    ORDER BY t.data_transacao DESC, t.id DESC
                    LIMIT :limit OFFSET :offset
                """)
                
                params["limit"] = filter_data.limit
                params["offset"] = filter_data.offset
                
                result = conn.execute(sql, params).fetchall()
                
                # Contar total
                count_sql = text(f"""
                    SELECT COUNT(*) 
                    FROM Transacoes t
                    LEFT JOIN Subcategorias s ON t.subcategoria_id = s.id
                    WHERE {where_sql}
                """)
                total = conn.execute(count_sql, {k: v for k, v in params.items() 
                                                  if k not in ('limit', 'offset')}).scalar()
                
                transactions = [
                    TransactionItem(
                        id=row[0],
                        descricao=row[1],
                        valor=float(row[2]),
                        tipo_transacao=row[3],
                        data_transacao=row[4],
                        conta_nome=row[5],
                        categoria_nome=row[6],
                        subcategoria_nome=row[7]
                    )
                    for row in result
                ]
                
                return GetTransactionHistoryOutput(
                    success=True,
                    transactions=transactions,
                    total_count=total or 0,
                    message=f"Encontradas {len(transactions)} transações"
                )
                
        except Exception as e:
            return GetTransactionHistoryOutput(
                success=False,
                message=f"Erro ao consultar histórico: {str(e)}"
            )


__all__ = [
    'TransactionHistoryFilter',
    'TransactionItem',
    'GetTransactionHistoryOutput',
    'GetTransactionHistoryUseCase',
]
