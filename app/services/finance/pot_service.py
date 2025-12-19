# app/services/finance/pot_service.py
"""
Serviço de gerenciamento de potes de gastos.

Este módulo contém funções para consultar e gerenciar potes de gastos do usuário.
"""

from typing import List, Any
from ._database import text, Connection


def get_pote_status(conn: Connection, usuario_id: int) -> List[Any]:
    """
    Consulta o status de todos os potes de gasto do mês.

    Retorna para cada pote:
    - nome_pote: Nome do pote
    - valor_limite: Limite mensal do pote
    - valor_gasto_negativo: Valor total gasto no mês (negativo)

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário

    Returns:
        Lista de tuplas com (nome_pote, valor_limite, valor_gasto_negativo)
    """
    sql = text("""
        SELECT
            p.nome_pote, p.valor_limite,
            COALESCE(SUM(t.valor), 0) AS valor_gasto_negativo
        FROM PotesDeGastos p
        LEFT JOIN PoteSubCategorias psc ON p.id = psc.pote_id
        LEFT JOIN Transacoes t ON psc.subcategoria_id = t.subcategoria_id
            AND t.tipo_transacao = 'Despesa'
            AND t.usuario_id = :uid
            AND t.data_transacao >= date_trunc('month', CURRENT_DATE)
            AND t.data_transacao < date_trunc('month', CURRENT_DATE) + interval '1 month'
        WHERE
            p.usuario_id = :uid
            AND p.ativo = TRUE
        GROUP BY p.id, p.nome_pote, p.valor_limite
        ORDER BY p.nome_pote;
    """)
    return conn.execute(sql, {"uid": usuario_id}).fetchall()


__all__ = ['get_pote_status']
