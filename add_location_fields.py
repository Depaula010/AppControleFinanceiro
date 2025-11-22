#!/usr/bin/env python3
"""
Migration: Adiciona campos de localização à tabela Usuarios
Data: 2025-11-22
Descrição: Adiciona campos cidade e estado para configuração de localização do usuário
"""

import os
from sqlalchemy import create_engine, text

def run_migration():
    """Executa a migration para adicionar campos de localização"""

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
            print("🔄 Iniciando migration: adicionar campos de localização...")

            # Verificar se as colunas já existem
            check_sql = text("""
                SELECT column_name
                FROM information_schema.columns
                WHERE table_name = 'usuarios'
                  AND column_name IN ('cidade', 'estado')
            """)

            existing_columns = conn.execute(check_sql).fetchall()
            existing_column_names = [row[0] for row in existing_columns]

            if 'cidade' in existing_column_names and 'estado' in existing_column_names:
                print("✅ Colunas 'cidade' e 'estado' já existem. Migration não necessária.")
                return True

            # Executar migration
            conn.begin()

            migration_sql = text("""
                -- Adicionar coluna cidade (padrão: São Paulo)
                ALTER TABLE Usuarios
                ADD COLUMN IF NOT EXISTS cidade VARCHAR(100) DEFAULT 'São Paulo';

                -- Adicionar coluna estado (padrão: SP)
                ALTER TABLE Usuarios
                ADD COLUMN IF NOT EXISTS estado VARCHAR(2) DEFAULT 'SP';

                -- Criar índice para busca por localização
                CREATE INDEX IF NOT EXISTS idx_usuarios_localizacao
                ON Usuarios(cidade, estado);
            """)

            conn.execute(migration_sql)
            conn.commit()

            print("✅ Migration executada com sucesso!")
            print("   - Coluna 'cidade' adicionada (padrão: 'São Paulo')")
            print("   - Coluna 'estado' adicionada (padrão: 'SP')")
            print("   - Índice de localização criado")

            return True

    except Exception as e:
        print(f"❌ ERRO ao executar migration: {e}")
        return False

if __name__ == "__main__":
    success = run_migration()
    exit(0 if success else 1)
