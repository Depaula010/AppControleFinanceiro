# app/services/finance/category_service.py
"""
Serviço de gerenciamento de categorias.

Este módulo contém funções para buscar e gerenciar categorias e sub-categorias
de transações (Renda e Despesa).
"""

from typing import List, Dict, Any, Optional
from ._database import text, Connection


def get_user_categories(
    conn: Connection,
    usuario_id: int,
    tipo_transacao: str
) -> List[Dict[str, Any]]:
    """
    Busca categorias globais + do usuário por tipo (Renda/Despesa).

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        tipo_transacao: 'Renda' ou 'Despesa'

    Returns:
        Lista de dicionários com:
        - id: ID da subcategoria
        - nome_sub: Nome da subcategoria
        - nome_macro: Nome da macro-categoria
    """
    grupo_filtro_sql = "g.nome_grupo = 'Renda'"
    if tipo_transacao == 'Despesa':
        grupo_filtro_sql = "g.nome_grupo IN ('Despesa Essencial', 'Despesa Discricionária', 'Meta Financeira', 'Geral')"

    sql = text(f"""
        SELECT s.id, s.nome_sub, m.nome_macro
        FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        JOIN GrupoCategoria g ON m.grupo_id = g.id
        WHERE (s.usuario_id IS NULL OR s.usuario_id = :uid) AND ({grupo_filtro_sql})
    """)
    result = conn.execute(sql, {"uid": usuario_id}).fetchall()

    # Converte para o formato JSON que o Gemini espera
    return [{"id": row[0], "nome_sub": row[1], "nome_macro": row[2]} for row in result]


def get_fallback_category_id(conn: Connection, tipo_transacao: str) -> Optional[int]:
    """
    Pega o ID da subcategoria 'Outros' (Renda ou Despesa).

    Categoria fallback usada quando não é possível categorizar automaticamente.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        tipo_transacao: 'Renda' ou 'Despesa'

    Returns:
        ID da subcategoria 'Outros' ou None se não encontrada
    """
    nome_macro_outros = 'Receitas Gerais' if tipo_transacao == 'Renda' else 'Despesas Gerais'

    sql = text("""
        SELECT s.id FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        WHERE m.nome_macro = :nome_macro AND s.nome_sub = 'Outros' AND s.usuario_id IS NULL LIMIT 1
    """)
    return conn.execute(sql, {"nome_macro": nome_macro_outros}).scalar_one_or_none()


def get_category_name_by_id(conn: Connection, subcategoria_id: int) -> str:
    """
    Busca o nome formatado 'Macro -> Sub' pelo ID da subcategoria.

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        subcategoria_id: ID da subcategoria

    Returns:
        Nome formatado "Macro -> Sub" ou "Categoria Desconhecida"
    """
    sql = text("""
        SELECT s.nome_sub, m.nome_macro
        FROM SubCategoria s
        JOIN MacroCategoria m ON s.macro_id = m.id
        WHERE s.id = :scid
    """)
    info = conn.execute(sql, {"scid": subcategoria_id}).fetchone()
    return f"{info[1]} -> {info[0]}" if info else "Categoria Desconhecida"


def get_category_spending(
    conn: Connection,
    usuario_id: int,
    nome_categoria_consulta: str
) -> float:
    """
    Consulta o gasto total em uma categoria/macro-categoria no mês atual.

    Busca tanto em subcategorias quanto em macro-categorias usando ILIKE (case-insensitive).

    Args:
        conn: Conexão do SQLAlchemy (requerida)
        usuario_id: ID do usuário
        nome_categoria_consulta: Nome da categoria (parcial ou completo)

    Returns:
        Valor total gasto (positivo) na categoria no mês atual
    """
    sql = text("""
        WITH CategoriaAlvo AS (
            SELECT id FROM SubCategoria
            WHERE (usuario_id = :uid OR usuario_id IS NULL)
              AND nome_sub ILIKE :nome_cat

            UNION

            SELECT s.id FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            WHERE (m.usuario_id = :uid OR m.usuario_id IS NULL)
              AND m.nome_macro ILIKE :nome_cat
        )
        SELECT COALESCE(SUM(t.valor), 0) AS valor_gasto_total
        FROM Transacoes t
        WHERE t.usuario_id = :uid
          AND t.tipo_transacao = 'Despesa'
          AND EXTRACT(MONTH FROM t.data_transacao) = EXTRACT(MONTH FROM CURRENT_DATE)
          AND EXTRACT(YEAR FROM t.data_transacao) = EXTRACT(YEAR FROM CURRENT_DATE)
          AND t.subcategoria_id IN (SELECT id FROM CategoriaAlvo);
    """)
    gasto_total_negativo = conn.execute(
        sql,
        {"uid": usuario_id, "nome_cat": f"%{nome_categoria_consulta}%"}
    ).scalar()

    # Retornar valor positivo (despesas são armazenadas como negativo)
    return (float(gasto_total_negativo or 0)) * -1


__all__ = [
    'get_user_categories',
    'get_fallback_category_id',
    'get_category_name_by_id',
    'get_category_spending',
]
