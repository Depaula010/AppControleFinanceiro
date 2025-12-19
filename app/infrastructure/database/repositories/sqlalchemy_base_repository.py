"""
Implementação base de repositório usando SQLAlchemy.

Fornece implementação genérica de operações CRUD que pode ser
estendida por repositórios específicos.
"""

from typing import TypeVar, Generic, Optional, List, Type
from sqlalchemy.orm import Session
from app.infrastructure.database.models.base import Base


T = TypeVar('T', bound=Base)


class SQLAlchemyBaseRepository(Generic[T]):
    """
    Repositório base com implementação SQLAlchemy.

    Implementa operações CRUD genéricas que funcionam com qualquer modelo ORM.

    Type Parameters:
        T: Tipo do modelo ORM (deve estender Base)

    Usage:
        class UserRepository(SQLAlchemyBaseRepository[UserModel]):
            def __init__(self, session: Session):
                super().__init__(session, UserModel)
    """

    def __init__(self, session: Session, model_class: Type[T]):
        """
        Inicializa repositório base.

        Args:
            session: Sessão SQLAlchemy
            model_class: Classe do modelo ORM
        """
        self.session = session
        self.model_class = model_class

    def get_by_id(self, id: int) -> Optional[T]:
        """
        Busca entidade por ID.

        Args:
            id: Identificador único

        Returns:
            Entidade ou None
        """
        return self.session.query(self.model_class).filter(
            self.model_class.id == id
        ).first()

    def get_all(self, skip: int = 0, limit: int = 100) -> List[T]:
        """
        Lista todas as entidades com paginação.

        Args:
            skip: Offset
            limit: Limite de registros

        Returns:
            Lista de entidades
        """
        return self.session.query(self.model_class)\
            .offset(skip)\
            .limit(limit)\
            .all()

    def create(self, entity: T) -> T:
        """
        Cria nova entidade.

        Args:
            entity: Entidade a criar (modelo ORM)

        Returns:
            Entidade criada com ID
        """
        self.session.add(entity)
        self.session.flush()  # Flush para gerar ID
        self.session.refresh(entity)  # Refresh para pegar valores do servidor
        return entity

    def update(self, id: int, **kwargs) -> Optional[T]:
        """
        Atualiza entidade existente.

        Args:
            id: ID da entidade
            **kwargs: Campos a atualizar

        Returns:
            Entidade atualizada ou None
        """
        entity = self.get_by_id(id)
        if entity is None:
            return None

        for key, value in kwargs.items():
            if hasattr(entity, key):
                setattr(entity, key, value)

        self.session.flush()
        self.session.refresh(entity)
        return entity

    def delete(self, id: int) -> bool:
        """
        Deleta entidade por ID.

        Args:
            id: ID da entidade

        Returns:
            True se deletou, False se não encontrou
        """
        entity = self.get_by_id(id)
        if entity is None:
            return False

        self.session.delete(entity)
        self.session.flush()
        return True

    def exists(self, id: int) -> bool:
        """
        Verifica se entidade existe.

        Args:
            id: ID da entidade

        Returns:
            True se existe
        """
        return self.session.query(
            self.session.query(self.model_class).filter(
                self.model_class.id == id
            ).exists()
        ).scalar()

    def count(self) -> int:
        """
        Conta total de entidades.

        Returns:
            Quantidade de registros
        """
        return self.session.query(self.model_class).count()

    def commit(self):
        """
        Commit explícito da transação.

        Normalmente o commit é feito pelo gerenciador de sessão,
        mas este método permite commit manual quando necessário.
        """
        self.session.commit()

    def rollback(self):
        """
        Rollback explícito da transação.

        Desfaz todas as mudanças não commitadas.
        """
        self.session.rollback()
