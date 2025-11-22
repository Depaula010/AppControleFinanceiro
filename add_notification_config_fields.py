#!/usr/bin/env python3
"""
Migration: Adiciona campos de resumo matinal à tabela NotificationConfigs
Data: 2025-11-22
Descrição: Adiciona campos resumo_matinal_ativo e resumo_matinal_hora
"""

import os
from sqlalchemy import create_engine, text

def run_migration():
    """Executa a migration para adicionar campos de resumo matinal"""

    # Carregar DATABASE_URL
    DATABASE_URL = os.environ.get('DATABASE_URL')
    if not DATABASE_URL:
        print("❌ ERRO: Variável DATABASE_URL não configurada")
        return False

    # Ajustar URL se necessário (postgres:// -> postgresql://)
    if DATABASE_URL.startswith("postgres://"):
        DATABASE_URL = DATABASE_URL.replace("postgres://", "postgresql://", 1)

    try:
        engine = create_engine(DATABASE_URL)

        with engine.connect() as conn:
            print("🔄 Iniciando migration: adicionar campos de resumo matinal...")

            # Verificar se as colunas já existem
            check_sql = text("""
                SELECT column_name
                FROM information_schema.columns
                WHERE table_name = 'notificationconfigs'
                  AND column_name IN ('resumo_matinal_ativo', 'resumo_matinal_hora')
            """)

            existing_columns = conn.execute(check_sql).fetchall()
            existing_column_names = [row[0] for row in existing_columns]

            if 'resumo_matinal_ativo' in existing_column_names and 'resumo_matinal_hora' in existing_column_names:
                print("✅ Colunas de resumo matinal já existem. Migration não necessária.")
                return True

            # Executar migration
            conn.begin()

            migration_sql = text("""
                -- Adicionar coluna resumo_matinal_ativo (padrão: TRUE)
                ALTER TABLE NotificationConfigs
                ADD COLUMN IF NOT EXISTS resumo_matinal_ativo BOOLEAN NOT NULL DEFAULT TRUE;

                -- Adicionar coluna resumo_matinal_hora (padrão: 07:00:00)
                ALTER TABLE NotificationConfigs
                ADD COLUMN IF NOT EXISTS resumo_matinal_hora TIME NOT NULL DEFAULT '07:00:00';
            """)

            conn.execute(migration_sql)
            conn.commit()

            print("✅ Migration executada com sucesso!")
            print("   - Coluna 'resumo_matinal_ativo' adicionada (padrão: TRUE)")
            print("   - Coluna 'resumo_matinal_hora' adicionada (padrão: '07:00:00')")

            return True

    except Exception as e:
        print(f"❌ ERRO ao executar migration: {e}")
        return False

if __name__ == "__main__":
    success = run_migration()
    exit(0 if success else 1)
