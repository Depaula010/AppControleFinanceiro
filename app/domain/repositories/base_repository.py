"""
Interface base para todos os repositórios.

Define o contrato CRUD genérico que todos os repositórios devem implementar.
"""

from typing import Protocol, TypeVar, Generic, Optional, List
from abc import abstractmethod


# Type variable para o modelo de domínio
T = TypeVar('T')
ID = TypeVar('ID')


class IBaseRepository(Protocol, Generic[T, ID]):
    """
    Interface base para repositórios (padrão Repository Pattern).

    Define operações CRUD genéricas que todos os repositórios devem implementar.

    Type Parameters:
        T: Tipo do modelo de domínio
        ID: Tipo do identificador (geralmente int)

    Métodos obrigatórios:
        - get_by_id: Buscar por ID
        - get_all: Listar todos os registros
        - create: Criar novo registro
        - update: Atualizar registro existente
        - delete: Deletar registro
        - exists: Verificar se registro existe
    """

    @abstractmethod
    def get_by_id(self, id: ID) -> Optional[T]:
        """
        Busca uma entidade por ID.

        Args:
            id: Identificador único da entidade

        Returns:
            Entidade encontrada ou None se não existir
        """
        ...

    @abstractmethod
    def get_all(self, skip: int = 0, limit: int = 100) -> List[T]:
        """
        Lista todas as entidades com paginação.

        Args:
            skip: Quantidade de registros a pular (offset)
            limit: Quantidade máxima de registros a retornar

        Returns:
            Lista de entidades
        """
        ...

    @abstractmethod
    def create(self, entity: T) -> T:
        """
        Cria uma nova entidade.

        Args:
            entity: Entidade a ser criada (sem ID)

        Returns:
            Entidade criada com ID gerado
        """
        ...

    @abstractmethod
    def update(self, id: ID, entity: T) -> Optional[T]:
        """
        Atualiza uma entidade existente.

        Args:
            id: ID da entidade a atualizar
            entity: Dados atualizados da entidade

        Returns:
            Entidade atualizada ou None se não existir
        """
        ...

    @abstractmethod
    def delete(self, id: ID) -> bool:
        """
        Deleta uma entidade por ID.

        Args:
            id: ID da entidade a deletar

        Returns:
            True se deletou com sucesso, False se não encontrou
        """
        ...

    @abstractmethod
    def exists(self, id: ID) -> bool:
        """
        Verifica se uma entidade existe.

        Args:
            id: ID da entidade

        Returns:
            True se existe, False caso contrário
        """
        ...

    @abstractmethod
    def count(self) -> int:
        """
        Conta o total de entidades.

        Returns:
            Quantidade total de registros
        """
        ...
