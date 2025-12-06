"""
Migration: Adicionar colunas de contas padrão na tabela Usuarios

Data: 2025-12-06
Descrição: Adiciona conta_padrao_renda_id e conta_padrao_despesa_id para permitir
           que usuários configurem suas contas padrão personalizadas.
"""

from sqlalchemy import text
from app import db_engine


def run_migration():
    """Executa a migration para adicionar colunas de contas padrão"""

    with db_engine.connect() as conn:
        trans = conn.begin()

        try:
            print("[MIGRATION] Verificando se colunas já existem...")

            # Verificar se as colunas já existem
            check_sql = text("""
                SELECT column_name
                FROM information_schema.columns
                WHERE table_name = 'usuarios'
                AND column_name IN ('conta_padrao_renda_id', 'conta_padrao_despesa_id')
            """)
            existing_columns = conn.execute(check_sql).fetchall()

            if len(existing_columns) == 2:
                print("[MIGRATION] ✅ Colunas já existem. Migration não necessária.")
                trans.rollback()
                return

            print("[MIGRATION] Adicionando colunas de contas padrão...")

            # Adicionar colunas
            alter_sql = text("""
                ALTER TABLE Usuarios
                ADD COLUMN IF NOT EXISTS conta_padrao_renda_id INT REFERENCES Contas(id) ON DELETE SET NULL,
                ADD COLUMN IF NOT EXISTS conta_padrao_despesa_id INT REFERENCES Contas(id) ON DELETE SET NULL
            """)
            conn.execute(alter_sql)

            print("[MIGRATION] Criando índices para performance...")

            # Criar índices
            index_renda = text("""
                CREATE INDEX IF NOT EXISTS idx_usuarios_conta_renda
                ON Usuarios(conta_padrao_renda_id)
            """)
            conn.execute(index_renda)

            index_despesa = text("""
                CREATE INDEX IF NOT EXISTS idx_usuarios_conta_despesa
                ON Usuarios(conta_padrao_despesa_id)
            """)
            conn.execute(index_despesa)

            trans.commit()
            print("[MIGRATION] ✅ Migration concluída com sucesso!")
            print("[MIGRATION] Colunas adicionadas:")
            print("[MIGRATION]   - conta_padrao_renda_id")
            print("[MIGRATION]   - conta_padrao_despesa_id")
            print("[MIGRATION] Índices criados:")
            print("[MIGRATION]   - idx_usuarios_conta_renda")
            print("[MIGRATION]   - idx_usuarios_conta_despesa")

        except Exception as e:
            trans.rollback()
            print(f"[MIGRATION] ❌ Erro durante migration: {e}")
            raise


if __name__ == "__main__":
    print("="*60)
    print("MIGRATION: Adicionar Contas Padrão")
    print("="*60)
    run_migration()
    print("="*60)
