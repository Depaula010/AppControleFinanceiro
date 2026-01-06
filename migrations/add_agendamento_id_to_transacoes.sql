-- Migration: Adicionar coluna agendamento_id na tabela Transacoes
-- Data: 2026-01-05
-- Descrição: Adiciona rastreabilidade entre transações e agendamentos para popular faturas automaticamente

-- Verificar se a coluna já existe antes de adicionar
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_name = 'Transacoes'
        AND column_name = 'agendamento_id'
    ) THEN
        -- Adicionar coluna agendamento_id
        ALTER TABLE "Transacoes"
        ADD COLUMN agendamento_id INT NULL;

        -- Adicionar foreign key para Agendamentos
        ALTER TABLE "Transacoes"
        ADD CONSTRAINT fk_transacoes_agendamento
        FOREIGN KEY (agendamento_id)
        REFERENCES "Agendamentos"(id)
        ON DELETE SET NULL;

        -- Criar índice para performance
        CREATE INDEX idx_transacoes_agendamento_id
        ON "Transacoes"(agendamento_id);

        RAISE NOTICE 'Coluna agendamento_id adicionada com sucesso à tabela Transacoes';
    ELSE
        RAISE NOTICE 'Coluna agendamento_id já existe na tabela Transacoes';
    END IF;
END $$;

-- Comentário explicativo
COMMENT ON COLUMN "Transacoes".agendamento_id IS
'ID do agendamento que originou esta transação (para FIXO, PARCELADO, LEMBRETE_VARIAVEL). NULL para transações manuais.';
