"""
ORM Model para MacroCategorias.

Este módulo mapeia a tabela 'MacroCategoria' que armazena as categorias
de nível médio, associadas a um grupo de categorias.
"""

from typing import Optional

from sqlalchemy import Integer, String, ForeignKey
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


class MacroCategoryModel(Base):
    """
    Modelo ORM para MacroCategorias.

    Representa o nível intermediário da hierarquia de categorização:
    GrupoCategoria -> MacroCategoria -> SubCategoria

    Macrocategorias podem ser globais (usuario_id = NULL) ou específicas
    de um usuário (usuario_id != NULL).

    Exemplos: "Alimentação", "Transporte", "Saúde", "Salário"

    Attributes:
        id: Identificador único da macrocategoria
        grupo_id: ID do grupo de categorias ao qual pertence
        usuario_id: ID do usuário (NULL para categorias globais)
        nome_macro: Nome da macrocategoria
        ordem_macro: Ordem de exibição (opcional)

    Relationships (comentadas até todos os modelos estarem criados):
        group: Grupo de categorias ao qual esta macrocategoria pertence
        user: Usuário dono da categoria (NULL para categorias globais)
        sub_categories: Lista de subcategorias desta macrocategoria

    Constraints:
        - grupo_id é obrigatório
        - Ao deletar o grupo, impede a deleção (RESTRICT)
        - Ao deletar o usuário, remove as categorias dele (CASCADE)
    """

    __tablename__ = "MacroCategoria"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único da macrocategoria"
    )

    grupo_id: Mapped[int] = mapped_column(
        ForeignKey("GrupoCategoria.id", ondelete="RESTRICT"),
        nullable=False,
        index=True,
        comment="ID do grupo de categorias"
    )

    usuario_id: Mapped[Optional[int]] = mapped_column(
        ForeignKey("Usuarios.id", ondelete="CASCADE"),
        nullable=True,
        index=True,
        comment="ID do usuário (NULL para categorias globais)"
    )

    nome_macro: Mapped[str] = mapped_column(
        String(255),
        nullable=False,
        index=True,
        comment="Nome da macrocategoria (ex: Alimentação, Transporte)"
    )

    ordem_macro: Mapped[Optional[int]] = mapped_column(
        Integer,
        nullable=True,
        comment="Ordem de exibição nas listas"
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # group: Mapped["CategoryGroupModel"] = relationship(
    #     "CategoryGroupModel",
    #     back_populates="macro_categories"
    # )

    # user: Mapped[Optional["UserModel"]] = relationship(
    #     "UserModel",
    #     back_populates="macro_categories"
    # )

    # sub_categories: Mapped[list["SubCategoryModel"]] = relationship(
    #     "SubCategoryModel",
    #     back_populates="macro_category",
    #     cascade="all, delete-orphan"
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
            f"<MacroCategoryModel(id={self.id}, "
            f"nome='{self.nome_macro}', "
            f"grupo_id={self.grupo_id}, "
            f"usuario_id={self.usuario_id})>"
        )
