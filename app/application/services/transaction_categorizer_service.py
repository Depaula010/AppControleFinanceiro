"""
Serviço para categorização automática de transações usando IA.

Elimina código duplicado presente em 7+ lugares.
"""

from typing import Optional, List, Dict, Any
from sqlalchemy.engine import Connection

from app.services import finance_service, gemini_service


class TransactionCategorizerService:
    """
    Serviço para categorizar transações automaticamente.

    Elimina código duplicado como:
        cats_list = finance_service.get_user_categories(conn, usuario_id, tipo)
        id_outros = finance_service.get_fallback_category_id(conn, tipo)
        id_cat = gemini_service.categorize_transaction(cats_list, desc, tipo, id_outros, uid)

    Usage:
        from app.application.services.transaction_categorizer_service import TransactionCategorizerService

        with db_transaction() as conn:
            categoria_id = TransactionCategorizerService.categorize_with_ai(
                conn=conn,
                usuario_id=123,
                descricao="Compra no supermercado",
                tipo_transacao="Despesa"
            )
    """

    @staticmethod
    def categorize_with_ai(
        conn: Connection,
        usuario_id: int,
        descricao: str,
        tipo_transacao: str
    ) -> int:
        """
        Categoriza transação usando IA (Gemini).

        Busca categorias do usuário, fallback, e chama IA para categorizar.

        Args:
            conn: Conexão com banco de dados
            usuario_id: ID do usuário
            descricao: Descrição da transação
            tipo_transacao: Tipo ('Renda', 'Despesa', 'Transferência')

        Returns:
            int: ID da categoria escolhida pela IA

        Example:
            >>> categoria_id = TransactionCategorizerService.categorize_with_ai(
            ...     conn, usuario_id=1, descricao="Mercado", tipo_transacao="Despesa"
            ... )
            >>> categoria_id
            15  # ID da categoria "Supermercado"
        """
        # Buscar categorias do usuário
        categories_json_list = finance_service.get_user_categories(
            conn, usuario_id, tipo_transacao
        )

        # Buscar categoria fallback ("Outros")
        id_fallback = finance_service.get_fallback_category_id(conn, tipo_transacao)

        # Categorizar com IA
        categoria_id = gemini_service.categorize_transaction(
            categories_json_list,
            descricao,
            tipo_transacao,
            id_fallback,
            usuario_id
        )

        return categoria_id

    @staticmethod
    def categorize_with_ai_batch(
        conn: Connection,
        usuario_id: int,
        transacoes: List[Dict[str, Any]]
    ) -> List[int]:
        """
        Categoriza múltiplas transações de uma vez.

        Args:
            conn: Conexão com banco de dados
            usuario_id: ID do usuário
            transacoes: Lista de dicts com 'descricao' e 'tipo_transacao'

        Returns:
            List[int]: Lista de IDs de categorias (mesma ordem de entrada)

        Example:
            >>> transacoes = [
            ...     {"descricao": "Mercado", "tipo_transacao": "Despesa"},
            ...     {"descricao": "Salário", "tipo_transacao": "Renda"},
            ... ]
            >>> categorias = TransactionCategorizerService.categorize_with_ai_batch(
            ...     conn, usuario_id=1, transacoes=transacoes
            ... )
            >>> categorias
            [15, 3]  # IDs das categorias
        """
        categorias_ids = []

        for transacao in transacoes:
            descricao = transacao.get('descricao', '')
            tipo_transacao = transacao.get('tipo_transacao', 'Despesa')

            categoria_id = TransactionCategorizerService.categorize_with_ai(
                conn, usuario_id, descricao, tipo_transacao
            )

            categorias_ids.append(categoria_id)

        return categorias_ids

    @staticmethod
    def get_category_name(conn: Connection, categoria_id: int) -> Optional[str]:
        """
        Busca nome da categoria por ID.

        Args:
            conn: Conexão com banco de dados
            categoria_id: ID da categoria

        Returns:
            str | None: Nome da categoria ou None se não encontrada

        Example:
            >>> TransactionCategorizerService.get_category_name(conn, 15)
            'Supermercado'
        """
        return finance_service.get_category_name_by_id(conn, categoria_id)

    @staticmethod
    def suggest_category(
        conn: Connection,
        usuario_id: int,
        descricao: str,
        tipo_transacao: str,
        top_n: int = 3
    ) -> List[Dict[str, Any]]:
        """
        Sugere top N categorias mais prováveis para uma transação.

        Útil para interfaces que querem mostrar múltiplas opções ao usuário.

        Args:
            conn: Conexão com banco de dados
            usuario_id: ID do usuário
            descricao: Descrição da transação
            tipo_transacao: Tipo da transação
            top_n: Número de sugestões (padrão 3)

        Returns:
            List[Dict]: Lista de categorias sugeridas com scores

        Example:
            >>> sugestoes = TransactionCategorizerService.suggest_category(
            ...     conn, usuario_id=1, descricao="Mercado", tipo_transacao="Despesa"
            ... )
            >>> sugestoes
            [
                {"id": 15, "nome": "Supermercado", "score": 0.95},
                {"id": 16, "nome": "Alimentação", "score": 0.75},
                {"id": 17, "nome": "Casa", "score": 0.60}
            ]
        """
        # Por enquanto, retorna apenas a melhor categoria
        # TODO: Expandir gemini_service para retornar top N com scores
        categoria_id = TransactionCategorizerService.categorize_with_ai(
            conn, usuario_id, descricao, tipo_transacao
        )

        categoria_nome = TransactionCategorizerService.get_category_name(
            conn, categoria_id
        )

        return [{
            "id": categoria_id,
            "nome": categoria_nome,
            "score": 1.0  # Placeholder
        }]
