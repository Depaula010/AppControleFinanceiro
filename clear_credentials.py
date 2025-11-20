"""
Script para limpar credenciais OAuth do Google Calendar
Uso: python clear_credentials.py <usuario_id>
"""

import sys
from sqlalchemy import text
from app import db_engine

def clear_user_credentials(usuario_id):
    """Remove credenciais OAuth do usuário do banco de dados"""

    if not db_engine:
        print("❌ Erro: Banco de dados não configurado")
        return False

    try:
        # Verificar se existem credenciais
        check_sql = text("""
            SELECT usuario_id, scopes, token_expiry
            FROM GoogleCalendarTokens
            WHERE usuario_id = :uid
        """)

        with db_engine.connect() as conn:
            result = conn.execute(check_sql, {"uid": usuario_id}).fetchone()

            if not result:
                print(f"⚠️ Nenhuma credencial encontrada para usuário {usuario_id}")
                return False

            print(f"\n📋 Credenciais encontradas para usuário {usuario_id}:")
            print(f"   Scopes: {result.scopes}")
            print(f"   Expiry: {result.token_expiry}")

            # Confirmar exclusão
            confirm = input(f"\n⚠️ Deseja realmente deletar as credenciais do usuário {usuario_id}? (s/N): ")

            if confirm.lower() != 's':
                print("❌ Operação cancelada")
                return False

            # Deletar credenciais
            delete_sql = text("DELETE FROM GoogleCalendarTokens WHERE usuario_id = :uid")

            conn.begin()
            conn.execute(delete_sql, {"uid": usuario_id})
            conn.commit()

            print(f"\n✅ Credenciais do usuário {usuario_id} deletadas com sucesso!")
            print("\n📝 Próximos passos:")
            print("   1. Reinicie sua aplicação")
            print("   2. Acesse /admin/calendar-status para gerar nova URL de autorização")
            print("   3. Autorize novamente com as novas permissões de escrita")

            return True

    except Exception as e:
        print(f"❌ Erro ao deletar credenciais: {e}")
        import traceback
        traceback.print_exc()
        return False

def list_all_credentials():
    """Lista todas as credenciais salvas no banco"""

    if not db_engine:
        print("❌ Erro: Banco de dados não configurado")
        return

    try:
        sql = text("""
            SELECT usuario_id, scopes, token_expiry, updated_at
            FROM GoogleCalendarTokens
            ORDER BY usuario_id
        """)

        with db_engine.connect() as conn:
            results = conn.execute(sql).fetchall()

            if not results:
                print("ℹ️ Nenhuma credencial encontrada no banco de dados")
                return

            print(f"\n📋 Total de credenciais: {len(results)}\n")

            for row in results:
                print(f"👤 Usuário ID: {row.usuario_id}")
                print(f"   Scopes: {row.scopes}")
                print(f"   Expiry: {row.token_expiry}")
                print(f"   Updated: {row.updated_at}")
                print()

    except Exception as e:
        print(f"❌ Erro ao listar credenciais: {e}")
        import traceback
        traceback.print_exc()

if __name__ == "__main__":
    print("=" * 60)
    print("🔧 Script de Limpeza de Credenciais OAuth - Google Calendar")
    print("=" * 60)

    if len(sys.argv) < 2:
        print("\n📋 Uso:")
        print("   python clear_credentials.py <usuario_id>  - Deletar credenciais de um usuário")
        print("   python clear_credentials.py list          - Listar todas as credenciais")
        print("\nExemplos:")
        print("   python clear_credentials.py 1")
        print("   python clear_credentials.py list")
        sys.exit(1)

    command = sys.argv[1]

    if command.lower() == "list":
        list_all_credentials()
    else:
        try:
            usuario_id = int(command)
            clear_user_credentials(usuario_id)
        except ValueError:
            print(f"❌ Erro: '{command}' não é um ID de usuário válido")
            sys.exit(1)
