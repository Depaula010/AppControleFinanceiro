"""
Implementação do repositório de transações financeiras usando SQLAlchemy.
"""

from typing import Optional
from datetime import date
from decimal import Decimal
from sqlalchemy import func
from sqlalchemy.orm import Session
from app.infrastructure.database.models import TransactionModel, AccountModel
from app.domain.repositories import ITransactionRepository
from .sqlalchemy_base_repository import SQLAlchemyBaseRepository


class SQLAlchemyTransactionRepository(SQLAlchemyBaseRepository[TransactionModel]):
    """
    Repositório de transações financeiras com SQLAlchemy.

    Implementa ITransactionRepository com operações específicas de transações.
    """

    def __init__(self, session: Session):
        """
        Inicializa repositório de transações.

        Args:
            session: Sessão SQLAlchemy
        """
        super().__init__(session, TransactionModel)

    def get_by_user(
        self,
        usuario_id: int,
        skip: int = 0,
        limit: int = 100
    ) -> list[TransactionModel]:
        """Lista transações de um usuário com paginação."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.usuario_id == usuario_id
        ).order_by(
            TransactionModel.data_transacao.desc(),
            TransactionModel.id.desc()
        ).offset(skip).limit(limit).all()

    def get_by_account(
        self,
        conta_id: int,
        skip: int = 0,
        limit: int = 100
    ) -> list[TransactionModel]:
        """Lista transações de uma conta."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.conta_id == conta_id
        ).order_by(
            TransactionModel.data_transacao.desc(),
            TransactionModel.id.desc()
        ).offset(skip).limit(limit).all()

    def get_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """Lista transações em um período."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.data_transacao >= data_inicio,
            TransactionModel.data_transacao <= data_fim
        ).order_by(
            TransactionModel.data_transacao.desc(),
            TransactionModel.id.desc()
        ).all()

    def get_by_type(
        self,
        usuario_id: int,
        tipo_transacao: str
    ) -> list[TransactionModel]:
        """Lista transações de um tipo específico."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.tipo_transacao == tipo_transacao
        ).order_by(
            TransactionModel.data_transacao.desc()
        ).all()

    def get_income_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """Lista apenas receitas em um período."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.tipo_transacao == 'Renda',
            TransactionModel.data_transacao >= data_inicio,
            TransactionModel.data_transacao <= data_fim
        ).order_by(
            TransactionModel.data_transacao.desc()
        ).all()

    def get_expenses_by_period(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> list[TransactionModel]:
        """Lista apenas despesas em um período."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.tipo_transacao == 'Despesa',
            TransactionModel.data_transacao >= data_inicio,
            TransactionModel.data_transacao <= data_fim
        ).order_by(
            TransactionModel.data_transacao.desc()
        ).all()

    def get_by_invoice(self, fatura_id: int) -> list[TransactionModel]:
        """Lista transações de uma fatura."""
        return self.session.query(TransactionModel).filter(
            TransactionModel.fatura_id == fatura_id
        ).order_by(
            TransactionModel.data_transacao.desc()
        ).all()

    def calculate_balance(
        self,
        conta_id: int,
        up_to_date: Optional[date] = None
    ) -> Decimal:
        """
        Calcula saldo de uma conta até uma data.

        Args:
            conta_id: ID da conta
            up_to_date: Data limite (None = todas)

        Returns:
            Saldo calculado
        """
        # Buscar saldo inicial da conta
        account = self.session.query(AccountModel).filter(
            AccountModel.id == conta_id
        ).first()

        if account is None:
            return Decimal('0.00')

        saldo_inicial = account.saldo_inicial

        # Query base
        query = self.session.query(
            func.sum(TransactionModel.valor)
        ).filter(
            TransactionModel.conta_id == conta_id,
            TransactionModel.consolidada == True
        )

        # Aplicar filtro de data se fornecido
        if up_to_date:
            query = query.filter(TransactionModel.data_transacao <= up_to_date)

        # Calcular receitas
        receitas = query.filter(
            TransactionModel.tipo_transacao == 'Renda'
        ).scalar() or Decimal('0.00')

        # Calcular despesas
        despesas = query.filter(
            TransactionModel.tipo_transacao == 'Despesa'
        ).scalar() or Decimal('0.00')

        # Saldo = saldo_inicial + receitas - despesas
        return saldo_inicial + receitas - despesas

    def calculate_total_income(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> Decimal:
        """Calcula total de receitas em um período."""
        result = self.session.query(
            func.sum(TransactionModel.valor)
        ).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.tipo_transacao == 'Renda',
            TransactionModel.data_transacao >= data_inicio,
            TransactionModel.data_transacao <= data_fim,
            TransactionModel.consolidada == True
        ).scalar()

        return result or Decimal('0.00')

    def calculate_total_expenses(
        self,
        usuario_id: int,
        data_inicio: date,
        data_fim: date
    ) -> Decimal:
        """Calcula total de despesas em um período."""
        result = self.session.query(
            func.sum(TransactionModel.valor)
        ).filter(
            TransactionModel.usuario_id == usuario_id,
            TransactionModel.tipo_transacao == 'Despesa',
            TransactionModel.data_transacao >= data_inicio,
            TransactionModel.data_transacao <= data_fim,
            TransactionModel.consolidada == True
        ).scalar()

        return result or Decimal('0.00')

    def consolidate(self, id: int) -> bool:
        """Marca transação como consolidada."""
        result = self.update(id, consolidada=True)
        return result is not None

    def unconsolidate(self, id: int) -> bool:
        """Desmarca transação como consolidada."""
        result = self.update(id, consolidada=False)
        return result is not None
