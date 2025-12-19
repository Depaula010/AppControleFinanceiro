# app/infrastructure/database/models/base.py
"""
Base class para todos os modelos SQLAlchemy ORM.
Usando SQLAlchemy 2.0+ com DeclarativeBase e Mapped types.
"""

from datetime import datetime
from sqlalchemy import DateTime, func
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    """
    Classe base para todos os modelos ORM.

    Todos os modelos herdam desta classe para ter acesso aos recursos do ORM.

    Usage:
        class MyModel(Base):
            __tablename__ = "my_table"
            id: Mapped[int] = mapped_column(primary_key=True)
    """
    pass


class TimestampMixin:
    """
    Mixin que adiciona timestamps automáticos (created_at, updated_at).

    Usage:
        class MyModel(Base, TimestampMixin):
            __tablename__ = "my_table"
            # created_at e updated_at serão adicionados automaticamente
    """

    created_at: Mapped[datetime] = mapped_column(
        DateTime(timezone=True),
        server_default=func.now(),
        nullable=False,
        comment="Data/hora de criação do registro"
    )

    updated_at: Mapped[datetime | None] = mapped_column(
        DateTime(timezone=True),
        onupdate=func.now(),
        nullable=True,
        comment="Data/hora da última atualização"
    )
