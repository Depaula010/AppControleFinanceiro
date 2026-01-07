-- Migration: Adicionar campo mes_execucao à tabela Agendamentos
-- Data: 2026-01-06
-- Descrição: Campo necessário para filtrar agendamentos anuais por mês de execução
-- Usado em: Checkin noturno, queries de contas pendentes

-- Adicionar campo mes_execucao se não existir
ALTER TABLE "Agendamentos"
ADD COLUMN IF NOT EXISTS mes_execucao INTEGER;

-- Adicionar comentário explicativo
COMMENT ON COLUMN "Agendamentos".mes_execucao
IS 'Mês de execução (1-12) para agendamentos anuais. NULL para agendamentos não anuais.';

-- Adicionar constraint para validar valores
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'ck_agendamentos_mes_execucao'
    ) THEN
        ALTER TABLE "Agendamentos"
        ADD CONSTRAINT ck_agendamentos_mes_execucao
        CHECK (mes_execucao IS NULL OR (mes_execucao >= 1 AND mes_execucao <= 12));
    END IF;
END $$;

-- Índice para melhorar performance de queries com filtro de mes_execucao
CREATE INDEX IF NOT EXISTS idx_agendamentos_mes_execucao
ON "Agendamentos"(mes_execucao)
WHERE mes_execucao IS NOT NULL;

-- Log da migration
DO $$
BEGIN
    RAISE NOTICE 'Migration add_mes_execucao_to_agendamentos aplicada com sucesso!';
    RAISE NOTICE 'Campo mes_execucao adicionado à tabela Agendamentos';
END $$;
