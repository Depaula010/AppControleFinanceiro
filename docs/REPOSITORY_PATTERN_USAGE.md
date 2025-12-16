# Guia de Uso do Repository Pattern

**Data**: 2025-12-16
**Fase**: D.4 - Repository Pattern Implementado

---

## 📋 Visão Geral

O Repository Pattern foi implementado para abstrair o acesso a dados, separando a lógica de negócio da camada de persistência.

### Benefícios

- ✅ **Testabilidade**: Fácil mockar repositórios em testes
- ✅ **Manutenibilidade**: Lógica de acesso a dados centralizada
- ✅ **Flexibilidade**: Trocar implementação (SQL → NoSQL) sem afetar serviços
- ✅ **Type Safety**: Interfaces tipadas com Protocols
- ✅ **Clean Architecture**: Separação clara de responsabilidades

---

## 🏗️ Estrutura

```
app/
├── domain/
│   └── repositories/          # Interfaces (Protocols)
│       ├── base_repository.py
│       ├── user_repository.py
│       ├── account_repository.py
│       └── transaction_repository.py
│
└── infrastructure/
    └── database/
        └── repositories/      # Implementações (SQLAlchemy)
            ├── sqlalchemy_base_repository.py
            ├── sqlalchemy_user_repository.py
            ├── sqlalchemy_account_repository.py
            └── sqlalchemy_transaction_repository.py
```

**Separação de Camadas**:
- `domain/repositories`: Interfaces (contratos)
- `infrastructure/database/repositories`: Implementações concretas

---

## 💻 Exemplos de Uso

### 1. Uso Básico com SQLAlchemy Session

```python
from sqlalchemy import create_engine
from sqlalchemy.orm import Session
from app.infrastructure.database.repositories import SQLAlchemyUserRepository
import os

# Criar engine e sessão
engine = create_engine(os.getenv('DATABASE_URL'))
session = Session(engine)

try:
    # Instanciar repositório
    user_repo = SQLAlchemyUserRepository(session)

    # Buscar usuário por WhatsApp
    user = user_repo.get_by_whatsapp("+5511999999999")

    if user:
        print(f"Usuário encontrado: {user.nome}")
        print(f"Email: {user.email or 'Não definido'}")

        # Atualizar último acesso
        user_repo.update_last_access(user.id)

        # Commit manual
        session.commit()
    else:
        print("Usuário não encontrado")

finally:
    session.close()
```

---

### 2. Criar Novo Usuário

```python
from app.infrastructure.database.models import UserModel
from app.infrastructure.database.repositories import SQLAlchemyUserRepository
from sqlalchemy.orm import Session

session = Session(engine)

try:
    user_repo = SQLAlchemyUserRepository(session)

    # Verificar se WhatsApp já existe
    if user_repo.exists_by_whatsapp("+5511988887777"):
        print("WhatsApp já cadastrado!")
    else:
        # Criar novo usuário
        new_user = UserModel(
            nome="João Silva",
            numero_whatsapp="+5511988887777",
            fuso_horario="America/Sao_Paulo",
            ativo=True
        )

        created_user = user_repo.create(new_user)
        session.commit()

        print(f"Usuário criado com ID: {created_user.id}")

finally:
    session.close()
```

---

### 3. Listar Contas de um Usuário

```python
from app.infrastructure.database.repositories import (
    SQLAlchemyUserRepository,
    SQLAlchemyAccountRepository
)

session = Session(engine)

try:
    user_repo = SQLAlchemyUserRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Buscar usuário
    user = user_repo.get_by_whatsapp("+5511999999999")

    if user:
        # Listar todas as contas
        all_accounts = account_repo.get_by_user(user.id)
        print(f"Total de contas: {len(all_accounts)}")

        # Listar apenas contas ativas
        active_accounts = account_repo.get_active_by_user(user.id)
        print(f"Contas ativas: {len(active_accounts)}")

        # Listar apenas cartões de crédito
        credit_cards = account_repo.get_credit_cards_by_user(user.id)
        print(f"Cartões de crédito: {len(credit_cards)}")

        for account in active_accounts:
            print(f"  - {account.nome_conta} ({account.tipo_conta})")

finally:
    session.close()
```

---

### 4. Consultar Transações e Calcular Saldo

```python
from datetime import date
from app.infrastructure.database.repositories import (
    SQLAlchemyTransactionRepository,
    SQLAlchemyAccountRepository
)

session = Session(engine)

try:
    transaction_repo = SQLAlchemyTransactionRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Buscar conta
    account = account_repo.get_by_id(1)

    if account:
        # Calcular saldo atual
        saldo_atual = transaction_repo.calculate_balance(account.id)
        print(f"Saldo atual da {account.nome_conta}: R$ {saldo_atual}")

        # Transações do mês atual
        hoje = date.today()
        inicio_mes = date(hoje.year, hoje.month, 1)

        transacoes_mes = transaction_repo.get_by_period(
            usuario_id=account.usuario_id,
            data_inicio=inicio_mes,
            data_fim=hoje
        )

        print(f"\nTransações do mês: {len(transacoes_mes)}")

        # Totais do mês
        receitas = transaction_repo.calculate_total_income(
            usuario_id=account.usuario_id,
            data_inicio=inicio_mes,
            data_fim=hoje
        )

        despesas = transaction_repo.calculate_total_expenses(
            usuario_id=account.usuario_id,
            data_inicio=inicio_mes,
            data_fim=hoje
        )

        print(f"Receitas do mês: R$ {receitas}")
        print(f"Despesas do mês: R$ {despesas}")
        print(f"Saldo do mês: R$ {receitas - despesas}")

finally:
    session.close()
```

---

### 5. Context Manager para Sessões (Recomendado)

```python
from contextlib import contextmanager
from sqlalchemy import create_engine
from sqlalchemy.orm import sessionmaker

# Criar engine e session factory
engine = create_engine(os.getenv('DATABASE_URL'))
SessionLocal = sessionmaker(bind=engine)

@contextmanager
def get_db_session():
    """Context manager para gerenciar sessões."""
    session = SessionLocal()
    try:
        yield session
        session.commit()  # Commit automático se não houver exceção
    except Exception:
        session.rollback()  # Rollback em caso de erro
        raise
    finally:
        session.close()


# Usar com context manager
with get_db_session() as session:
    user_repo = SQLAlchemyUserRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Fazer operações
    user = user_repo.get_by_whatsapp("+5511999999999")
    accounts = account_repo.get_by_user(user.id)

    # Commit automático ao sair do bloco 'with'
```

---

### 6. Uso em Serviços (Service Layer)

```python
from app.domain.repositories import IUserRepository, IAccountRepository

class FinanceService:
    """
    Serviço de finanças que usa repositórios.

    Recebe repositórios via injeção de dependências.
    """

    def __init__(
        self,
        user_repo: IUserRepository,
        account_repo: IAccountRepository
    ):
        self.user_repo = user_repo
        self.account_repo = account_repo

    def get_user_financial_summary(self, whatsapp: str) -> dict:
        """
        Retorna resumo financeiro de um usuário.

        Args:
            whatsapp: Número do WhatsApp

        Returns:
            Dicionário com resumo
        """
        # Buscar usuário
        user = self.user_repo.get_by_whatsapp(whatsapp)
        if not user:
            raise ValueError("Usuário não encontrado")

        # Buscar contas ativas
        accounts = self.account_repo.get_active_by_user(user.id)

        # Atualizar último acesso
        self.user_repo.update_last_access(user.id)

        return {
            "usuario": {
                "id": user.id,
                "nome": user.nome,
                "email": user.email
            },
            "contas": [
                {
                    "id": acc.id,
                    "nome": acc.nome_conta,
                    "tipo": acc.tipo_conta
                }
                for acc in accounts
            ],
            "total_contas": len(accounts)
        }


# Usar serviço
with get_db_session() as session:
    # Instanciar repositórios
    user_repo = SQLAlchemyUserRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Instanciar serviço
    finance_service = FinanceService(user_repo, account_repo)

    # Usar serviço
    summary = finance_service.get_user_financial_summary("+5511999999999")
    print(summary)
```

---

## 🧪 Testes Unitários com Mocks

```python
import pytest
from unittest.mock import Mock
from app.domain.repositories import IUserRepository

def test_finance_service_get_summary():
    """Testa serviço com repositório mockado."""

    # Mockar repositório de usuários
    mock_user_repo = Mock(spec=IUserRepository)
    mock_user = Mock(id=1, nome="João", email="joao@example.com")
    mock_user_repo.get_by_whatsapp.return_value = mock_user

    # Mockar repositório de contas
    mock_account_repo = Mock()
    mock_accounts = [
        Mock(id=1, nome_conta="Nubank", tipo_conta="Conta Corrente"),
        Mock(id=2, nome_conta="Inter", tipo_conta="Conta Poupança")
    ]
    mock_account_repo.get_active_by_user.return_value = mock_accounts

    # Instanciar serviço com mocks
    service = FinanceService(mock_user_repo, mock_account_repo)

    # Executar método
    summary = service.get_user_financial_summary("+5511999999999")

    # Verificações
    assert summary["usuario"]["nome"] == "João"
    assert summary["total_contas"] == 2
    mock_user_repo.update_last_access.assert_called_once_with(1)
```

---

## 📊 Métodos Disponíveis

### UserRepository
- `get_by_id(id)` - Buscar por ID
- `get_by_whatsapp(numero)` - Buscar por WhatsApp
- `get_by_api_key(api_key)` - Buscar por API key
- `get_by_email(email)` - Buscar por email
- `get_active_users()` - Listar usuários ativos
- `create(user)` - Criar usuário
- `update(id, **kwargs)` - Atualizar usuário
- `delete(id)` - Deletar usuário
- `activate(id)` - Ativar usuário
- `deactivate(id)` - Desativar usuário
- `update_last_access(id)` - Atualizar último acesso
- `exists(id)` - Verificar se existe
- `exists_by_whatsapp(numero)` - Verificar por WhatsApp
- `exists_by_email(email)` - Verificar por email

### AccountRepository
- `get_by_id(id)` - Buscar por ID
- `get_by_user(usuario_id)` - Listar contas do usuário
- `get_active_by_user(usuario_id)` - Listar contas ativas
- `get_credit_cards_by_user(usuario_id)` - Listar cartões
- `get_by_user_and_name(usuario_id, nome)` - Buscar por nome
- `create(account)` - Criar conta
- `update(id, **kwargs)` - Atualizar conta
- `delete(id)` - Deletar conta
- `activate(id)` - Ativar conta
- `deactivate(id)` - Desativar conta
- `exists(id)` - Verificar se existe
- `exists_by_user_and_name(usuario_id, nome)` - Verificar por nome

### TransactionRepository
- `get_by_id(id)` - Buscar por ID
- `get_by_user(usuario_id)` - Listar transações do usuário
- `get_by_account(conta_id)` - Listar transações da conta
- `get_by_period(usuario_id, inicio, fim)` - Listar por período
- `get_by_type(usuario_id, tipo)` - Listar por tipo
- `get_income_by_period(usuario_id, inicio, fim)` - Listar receitas
- `get_expenses_by_period(usuario_id, inicio, fim)` - Listar despesas
- `get_by_invoice(fatura_id)` - Listar transações da fatura
- `calculate_balance(conta_id, up_to_date)` - Calcular saldo
- `calculate_total_income(usuario_id, inicio, fim)` - Total de receitas
- `calculate_total_expenses(usuario_id, inicio, fim)` - Total de despesas
- `create(transaction)` - Criar transação
- `update(id, **kwargs)` - Atualizar transação
- `delete(id)` - Deletar transação
- `consolidate(id)` - Marcar como consolidada
- `unconsolidate(id)` - Desmarcar como consolidada
- `exists(id)` - Verificar se existe

---

## ⚠️ Boas Práticas

1. **Sempre usar Context Manager** para sessões
2. **Commit manual** quando necessário controle preciso
3. **Rollback em exceções** para manter consistência
4. **Injeção de dependências** nos serviços (não criar repositórios dentro)
5. **Type hints** para melhor autocomplete e type checking
6. **Mockar repositórios** em testes unitários
7. **Usar transações** para operações que afetam múltiplas tabelas

---

## 🎯 Próximos Passos

Após executar a migração SQL ([migration_add_missing_fields.sql](../migrations/migration_add_missing_fields.sql)):

1. Testar repositórios com banco real
2. Implementar Dependency Injection (Fase D.5)
3. Criar mais repositórios (Invoice, Schedule, etc.)
4. Integrar com código Flask existente via feature flags

---

**Documentação completa em**: [PHASE_D_PROGRESS.md](PHASE_D_PROGRESS.md)
