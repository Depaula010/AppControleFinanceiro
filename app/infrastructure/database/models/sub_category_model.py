"""
ORM Model para SubCategorias.

Este módulo mapeia a tabela 'SubCategoria' que armazena as categorias
de nível mais baixo, associadas a uma macrocategoria.
"""

from typing import Optional

from sqlalchemy import Integer, String, ForeignKey, UniqueConstraint
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


class SubCategoryModel(Base):
    """
    Modelo ORM para SubCategorias.

    Representa o nível mais baixo da hierarquia de categorização:
    GrupoCategoria -> MacroCategoria -> SubCategoria

    Subcategorias podem ser globais (usuario_id = NULL) ou específicas
    de um usuário (usuario_id != NULL).

    Exemplos:
    - MacroCategoria "Alimentação" -> SubCategoria "Supermercado", "Restaurante"
    - MacroCategoria "Transporte" -> SubCategoria "Combustível", "Uber"
    - MacroCategoria "Salário" -> SubCategoria "Salário Mensal", "Bônus"

    Attributes:
        id: Identificador único da subcategoria
        macro_id: ID da macrocategoria à qual pertence
        usuario_id: ID do usuário (NULL para categorias globais)
        nome_sub: Nome da subcategoria

    Relationships (comentadas até todos os modelos estarem criados):
        macro_category: Macrocategoria à qual esta subcategoria pertence
        user: Usuário dono da categoria (NULL para categorias globais)
        transactions: Lista de transações usando esta subcategoria

    Constraints:
        - Combinação (macro_id, nome_sub, usuario_id) deve ser única
        - Ao deletar a macrocategoria, remove as subcategorias (CASCADE)
        - Ao deletar o usuário, remove as categorias dele (CASCADE)
    """

    __tablename__ = "SubCategoria"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da subcategoria"
    )

    macro_id: Mapped[int] = mapped_column(
        ForeignKey("MacroCategoria.id", ondelete="CASCADE"),
        nullable=False,
        index=True,
        comment="ID da macrocategoria"
    )

    usuario_id: Mapped[Optional[int]] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=True,
        index=True,
        comment="ID do usuário (NULL para categorias globais)"
    )

    nome_sub: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        index=True,
        comment="Nome da subcategoria (ex: Supermercado, Combustível)"
    )

    # Constraints
    __table_args__ = (
        UniqueConstraint(
            "macro_id",
            "nome_sub",
            "usuario_id",
            name="uq_subcategoria_macro_nome_usuario"
        ),
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # macro_category: Mapped["MacroCategoryModel"] = relationship(
    #     "MacroCategoryModel",
    #     back_populates="sub_categories"
    # )

    # user: Mapped[Optional["UserModel"]] = relationship(
    #     "UserModel",
    #     back_populates="sub_categories"
    # )

    # transactions: Mapped[list["TransactionModel"]] = relationship(
    #     "TransactionModel",
    #     back_populates="sub_category"
    # )

    # Propriedades de conveniência
    @property
    def is_global(self) -> bool:
        """Retorna True se a categoria é global (não pertence a um usuário)."""
        return self.usuario_id is None

    @property
    def is_user_specific(self) -> bool:
        """Retorna True se a categoria pertence a um usuário específico."""
        return self.usuario_id is not None

    def __repr__(self) -> str:
        return (
            f"<SubCategoryModel(id={self.id}, "
            f"nome='{self.nome_sub}', "
            f"macro_id={self.macro_id}, "
            f"usuario_id={self.usuario_id})>"
        )
