"""
ORM Model para Grupos de Categorias.

Este módulo mapeia a tabela 'GrupoCategoria' que armazena os grupos
de categorias de alto nível (ex: Receitas, Despesas).
"""

from typing import Optional

from sqlalchemy import Integer, String
from sqlalchemy.orm import Mapped, mapped_column, relationship

from .base import Base


class CategoryGroupModel(Base):
    """
    Modelo ORM para Grupos de Categorias.

    Representa o nível mais alto da hierarquia de categorização:
    GrupoCategoria -> MacroCategoria -> SubCategoria

    Exemplos de grupos: "Receitas", "Despesas", "Investimentos"

    Attributes:
        id: Identificador único do grupo
        nome_grupo: Nome do grupo de categorias (único)

    Relationships (comentadas até todos os modelos estarem criados):
        macro_categories: Lista de macrocategorias deste grupo

    Constraints:
        - nome_grupo deve ser único
    """

    __tablename__ = "GrupoCategoria"

    # Colunas principais
    id: Mapped[int] = mapped_column(
        Integer,
        primary_key=True,
        autoincrement=True,
        comment="ID único do grupo de categorias"
    )

    nome_grupo: Mapped[str] = mapped_column(
        String(100),
        nullable=False,
        unique=True,
        index=True,
        comment="Nome do grupo (ex: Receitas, Despesas)"
    )

    # Relationships (comentados até todos os modelos estarem criados)
    # macro_categories: Mapped[list["MacroCategoryModel"]] = relationship(
    #     "MacroCategoryModel",
    #     back_populates="group",
    #     cascade="all, delete-orphan"
    # )

    def __repr__(self) -> str:
        return f"<CategoryGroupModel(id={self.id}, nome='{self.nome_grupo}')>"
