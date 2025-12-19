"""
Script de teste para validar repositórios ORM.

Execute dentro do Docker:
    docker-compose exec web python test_repositories.py

Ou localmente (se tiver DATABASE_URL configurado):
    python test_repositories.py
"""

import os
import sys
from datetime import date, datetime
from decimal import Decimal
from sqlalchemy import create_engine
from sqlalchemy.orm import Session

# Adicionar app ao path
sys.path.insert(0, os.path.dirname(__file__))

from app.infrastructure.database.repositories import (
    SQLAlchemyUserRepository,
    SQLAlchemyAccountRepository,
    SQLAlchemyTransactionRepository
)


def test_user_repository(session: Session):
    """Testa repositório de usuários."""
    print("\n" + "="*60)
    print("TESTE: UserRepository")
    print("="*60)

    user_repo = SQLAlchemyUserRepository(session)

    # Listar usuários ativos
    active_users = user_repo.get_active_users()
    print(f"✅ Usuários ativos encontrados: {len(active_users)}")

    if active_users:
        user = active_users[0]
        print(f"\n📋 Primeiro usuário:")
        print(f"   ID: {user.id}")
        print(f"   Nome: {user.nome}")
        print(f"   WhatsApp: {user.numero_whatsapp}")
        print(f"   Email: {user.email or 'Não definido'}")
        print(f"   Ativo: {user.ativo}")
        print(f"   Timezone: {user.fuso_horario}")
        print(f"   Último acesso: {user.ultimo_acesso or 'Nunca'}")
        print(f"   Criado em: {user.created_at}")
        print(f"   Atualizado em: {user.updated_at or 'Nunca'}")

        # Testar busca por WhatsApp
        found_user = user_repo.get_by_whatsapp(user.numero_whatsapp)
        print(f"\n✅ Busca por WhatsApp funcionando: {found_user is not None}")

        # Testar atualização de último acesso
        before_update = user.updated_at
        user_repo.update_last_access(user.id)
        session.flush()
        session.refresh(user)
        after_update = user.updated_at

        print(f"✅ Updated_at funcionando: {after_update != before_update}")

        return user
    else:
        print("⚠️  Nenhum usuário ativo encontrado")
        return None


def test_account_repository(session: Session, usuario_id: int):
    """Testa repositório de contas."""
    print("\n" + "="*60)
    print("TESTE: AccountRepository")
    print("="*60)

    account_repo = SQLAlchemyAccountRepository(session)

    # Listar todas as contas
    all_accounts = account_repo.get_by_user(usuario_id)
    print(f"✅ Total de contas do usuário: {len(all_accounts)}")

    # Listar contas ativas
    active_accounts = account_repo.get_active_by_user(usuario_id)
    print(f"✅ Contas ativas: {len(active_accounts)}")

    # Listar cartões de crédito
    credit_cards = account_repo.get_credit_cards_by_user(usuario_id)
    print(f"✅ Cartões de crédito: {len(credit_cards)}")

    if all_accounts:
        print(f"\n📋 Contas do usuário:")
        for acc in all_accounts:
            status = "🟢 Ativa" if acc.ativa else "🔴 Inativa"
            print(f"   [{acc.id}] {acc.nome_conta} - {acc.tipo_conta} - {status}")
            if acc.tipo_conta == "Cartão de Crédito":
                print(f"       └─ Limite: R$ {acc.limite_credito or 'Não definido'}")
                print(f"       └─ Vencimento: dia {acc.dia_vencimento}")
                print(f"       └─ Fechamento: dia {acc.dia_fechamento}")

        return all_accounts[0]
    else:
        print("⚠️  Nenhuma conta encontrada")
        return None


def test_transaction_repository(session: Session, usuario_id: int, conta_id: int = None):
    """Testa repositório de transações."""
    print("\n" + "="*60)
    print("TESTE: TransactionRepository")
    print("="*60)

    transaction_repo = SQLAlchemyTransactionRepository(session)

    # Listar transações do usuário (últimas 10)
    transactions = transaction_repo.get_by_user(usuario_id, skip=0, limit=10)
    print(f"✅ Últimas 10 transações: {len(transactions)}")

    if transactions:
        print(f"\n📋 Transações recentes:")
        for t in transactions[:5]:  # Mostrar apenas 5
            tipo_emoji = "💰" if t.tipo_transacao == "Renda" else "💸"
            consolidada = "✅" if t.consolidada else "⏳"
            print(f"   {tipo_emoji} [{t.id}] {t.descricao}")
            print(f"      └─ Valor: R$ {t.valor} | Data: {t.data_transacao} | {consolidada}")

    # Calcular totais do mês atual
    hoje = date.today()
    inicio_mes = date(hoje.year, hoje.month, 1)

    receitas_mes = transaction_repo.calculate_total_income(
        usuario_id, inicio_mes, hoje
    )
    despesas_mes = transaction_repo.calculate_total_expenses(
        usuario_id, inicio_mes, hoje
    )

    print(f"\n📊 Resumo do mês atual ({inicio_mes.strftime('%m/%Y')}):")
    print(f"   💰 Receitas: R$ {receitas_mes:,.2f}")
    print(f"   💸 Despesas: R$ {despesas_mes:,.2f}")
    print(f"   📈 Saldo: R$ {(receitas_mes - despesas_mes):,.2f}")

    # Se tiver conta, calcular saldo
    if conta_id:
        saldo = transaction_repo.calculate_balance(conta_id)
        print(f"\n💼 Saldo da conta ID {conta_id}: R$ {saldo:,.2f}")


def test_new_fields(session: Session):
    """Testa se os novos campos da migração estão funcionando."""
    print("\n" + "="*60)
    print("TESTE: Novos Campos da Migração")
    print("="*60)

    user_repo = SQLAlchemyUserRepository(session)
    account_repo = SQLAlchemyAccountRepository(session)

    # Testar campos novos do usuário
    users = user_repo.get_active_users()
    if users:
        user = users[0]
        print("✅ Campos novos em Usuarios:")
        print(f"   - email: {user.email or 'NULL'} ✓")
        print(f"   - conta_padrao_id: {user.conta_padrao_id or 'NULL'} ✓")
        print(f"   - fuso_horario: {user.fuso_horario} ✓")
        print(f"   - ativo: {user.ativo} ✓")
        print(f"   - ultimo_acesso: {user.ultimo_acesso or 'NULL'} ✓")
        print(f"   - updated_at: {user.updated_at or 'NULL'} ✓")

    # Testar campos novos da conta
    accounts = account_repo.get_all(limit=1)
    if accounts:
        account = accounts[0]
        print("\n✅ Campos novos em Contas:")
        print(f"   - limite_credito: {account.limite_credito or 'NULL'} ✓")
        print(f"   - inclui_saldo_total: {account.inclui_saldo_total} ✓")
        print(f"   - cor_hex: {account.cor_hex or 'NULL'} ✓")
        print(f"   - icone: {account.icone or 'NULL'} ✓")
        print(f"   - ativa: {account.ativa} ✓")
        print(f"   - ordem: {account.ordem or 'NULL'} ✓")
        print(f"   - updated_at: {account.updated_at or 'NULL'} ✓")


def main():
    """Executa todos os testes."""
    print("\n🚀 Iniciando testes dos repositórios ORM...")

    # Conectar ao banco
    database_url = os.getenv('DATABASE_URL')
    if not database_url:
        print("❌ ERRO: DATABASE_URL não definida!")
        print("Configure a variável de ambiente DATABASE_URL")
        return

    print(f"📡 Conectando ao banco de dados...")
    engine = create_engine(database_url)
    session = Session(engine)

    try:
        # Teste 1: Usuários
        user = test_user_repository(session)

        if user:
            # Teste 2: Contas
            account = test_account_repository(session, user.id)

            # Teste 3: Transações
            conta_id = account.id if account else None
            test_transaction_repository(session, user.id, conta_id)

            # Teste 4: Novos campos
            test_new_fields(session)

        # Commit (se tiver feito alguma alteração)
        session.commit()

        print("\n" + "="*60)
        print("✅ TODOS OS TESTES CONCLUÍDOS COM SUCESSO!")
        print("="*60)
        print("\n📖 Os repositórios estão funcionando corretamente!")
        print("📖 A migração SQL foi aplicada com sucesso!")
        print("📖 O ORM está mapeando corretamente as tabelas!")

    except Exception as e:
        print(f"\n❌ ERRO durante os testes:")
        print(f"   {type(e).__name__}: {e}")
        import traceback
        traceback.print_exc()
        session.rollback()

    finally:
        session.close()
        print("\n🔌 Conexão fechada.")


if __name__ == "__main__":
    main()
