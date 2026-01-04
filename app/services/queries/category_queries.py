"""
Queries SQL centralizadas para Categorias (SubCategoria, MacroCategoria, GrupoCategoria).

Este módulo contém todas as queries SQL relacionadas a categorias de transações
para evitar duplicação de código e facilitar manutenção.

IMPORTANTE: Ao modificar uma query aqui, a mudança afeta TODOS os lugares que a utilizam.
"""

from sqlalchemy import text
from typing import Dict, Any, Optional


class CategoryQueries:
    """
    Queries SQL reutilizáveis para operações com Categorias.

    O sistema tem 3 níveis de categorias:
    - GrupoCategoria (ex: "Despesa", "Renda", "Meta Financeira")
    - MacroCategoria (ex: "Alimentação", "Transporte")
    - SubCategoria (ex: "Restaurante", "Uber")
    """

    @staticmethod
    def get_short_term_investment_subcategory() -> text:
        """
        Busca subcategoria de "Investimentos de Curto Prazo".

        Usado em:
        - Transferências (criar transação de investimento temporário)
        - Classificação automática de reservas

        Parâmetros necessários:
            :uid (int) - ID do usuário (para validar categorias personalizadas)

        Retorna: ID da subcategoria

        Hierarquia:
        - Grupo: "Meta Financeira"
        - Macro: (qualquer)
        - Sub: "Investimentos de Curto Prazo"
        """
        return text("""
            SELECT s.id
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE g.nome_grupo = 'Meta Financeira'
              AND s.nome_sub = 'Investimentos de Curto Prazo'
              AND (s.usuario_id IS NULL OR s.usuario_id = :uid)
            LIMIT 1
        """)

    @staticmethod
    def get_loan_payment_subcategory() -> text:
        """
        Busca subcategoria de "Quitação de Empréstimos (Principal)".

        Usado em:
        - Transferências para quitação de dívidas
        - Classificação de pagamentos de empréstimos

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: ID da subcategoria

        Hierarquia:
        - Grupo: "Meta Financeira"
        - Macro: (qualquer)
        - Sub: "Quitação de Empréstimos (Principal)"
        """
        return text("""
            SELECT s.id
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE g.nome_grupo = 'Meta Financeira'
              AND s.nome_sub = 'Quitação de Empréstimos (Principal)'
              AND (s.usuario_id IS NULL OR s.usuario_id = :uid)
            LIMIT 1
        """)

    @staticmethod
    def get_fallback_category_outros() -> text:
        """
        Busca categoria fallback "Outros" de uma MacroCategoria.

        Usado em:
        - Quando não consegue mapear subcategoria específica
        - Fallback para categorização automática

        Parâmetros necessários:
            :nome_macro (str) - Nome da MacroCategoria (ex: "Alimentação")

        Retorna: ID da subcategoria "Outros"

        IMPORTANTE: Só busca categorias padrão do sistema (usuario_id IS NULL)
        """
        return text("""
            SELECT s.id
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            WHERE m.nome_macro = :nome_macro
              AND s.nome_sub = 'Outros'
              AND s.usuario_id IS NULL
            LIMIT 1
        """)

    @staticmethod
    def get_all_subcategories() -> text:
        """
        Busca todas as subcategorias disponíveis para um usuário.

        Usado em:
        - Listagem de categorias disponíveis
        - Validação de subcategoria_id

        Parâmetros necessários:
            :uid (int) - ID do usuário

        Retorna: id, nome_sub, nome_macro, nome_grupo

        Inclui:
        - Categorias padrão do sistema (usuario_id IS NULL)
        - Categorias personalizadas do usuário
        """
        return text("""
            SELECT
                s.id,
                s.nome_sub,
                m.nome_macro,
                g.nome_grupo
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE s.usuario_id IS NULL OR s.usuario_id = :uid
            ORDER BY g.nome_grupo, m.nome_macro, s.nome_sub
        """)

    @staticmethod
    def get_subcategory_by_name() -> text:
        """
        Busca subcategoria pelo nome completo (grupo + macro + sub).

        Usado em:
        - Mapeamento de categoria a partir de texto
        - Validação de categoria existe

        Parâmetros necessários:
            :uid (int) - ID do usuário
            :grupo (str) - Nome do grupo (ex: "Despesa")
            :macro (str) - Nome da macro (ex: "Alimentação")
            :sub (str) - Nome da sub (ex: "Restaurante")

        Retorna: ID da subcategoria
        """
        return text("""
            SELECT s.id
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE g.nome_grupo = :grupo
              AND m.nome_macro = :macro
              AND s.nome_sub = :sub
              AND (s.usuario_id IS NULL OR s.usuario_id = :uid)
            LIMIT 1
        """)

    @staticmethod
    def get_category_info() -> text:
        """
        Busca informações completas de uma categoria pelo ID.

        Usado em:
        - Exibição de nome completo da categoria
        - Validação de tipo de transação

        Parâmetros necessários:
            :subcategoria_id (int) - ID da subcategoria

        Retorna: nome_sub, nome_macro, nome_grupo
        """
        return text("""
            SELECT
                s.nome_sub,
                m.nome_macro,
                g.nome_grupo
            FROM SubCategoria s
            JOIN MacroCategoria m ON s.macro_id = m.id
            JOIN GrupoCategoria g ON m.grupo_id = g.id
            WHERE s.id = :subcategoria_id
        """)

    @staticmethod
    def get_parametros_categoria_especial(usuario_id: int, tipo: str) -> Dict[str, Any]:
        """
        Retorna parâmetros para buscar categorias especiais do sistema.

        Args:
            usuario_id: ID do usuário
            tipo: Tipo de categoria ("investimento" ou "emprestimo")

        Returns:
            Dict com parâmetros para a query
        """
        return {
            "uid": usuario_id
        }
