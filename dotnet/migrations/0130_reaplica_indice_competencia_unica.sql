-- Auditoria 2026-11-16 (achado M7): a 0090 termina sem erro quando ha duplicata por (agendamento_id, competencia) e o runner
-- a marca como aplicada sem o indice ter sido criado. Esta migration tenta de novo (uma vez, no proximo --migrate).
-- Aditiva e idempotente: se o indice ja existe, nao faz nada. Se ainda houver duplicata, avisa e NAO derruba o deploy;
-- confira com: SELECT 1 FROM pg_indexes WHERE indexname = 'ux_transacoes_agendamento_competencia';
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM transacoes
        WHERE agendamento_id IS NOT NULL AND competencia IS NOT NULL
        GROUP BY agendamento_id, competencia
        HAVING COUNT(*) > 1
    ) THEN
        RAISE WARNING 'ux_transacoes_agendamento_competencia NAO criado: existem lancamentos duplicados por (agendamento_id, competencia).';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ux_transacoes_agendamento_competencia
            ON transacoes (agendamento_id, competencia)
            WHERE agendamento_id IS NOT NULL AND competencia IS NOT NULL;
    END IF;
END
$$;
