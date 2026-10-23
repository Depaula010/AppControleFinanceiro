-- Desvio 258: o banco passa a recusar dois lançamentos do mesmo vencimento do mesmo agendamento.
-- Até aqui a idempotência do executor dependia só do código (FOR UPDATE + EXISTS); este índice é a segunda trava, no próprio banco.
-- Só vale para linhas com agendamento_id e competencia (as antigas, com competencia NULL, e os lançamentos avulsos não entram).
-- Aditiva e idempotente. Se já existir duplicata por (agendamento_id, competencia), NÃO derruba o deploy: avisa e não cria o índice
-- (corrija os duplicados e rode o --migrate de novo; a migration só é marcada como aplicada quando este script termina).
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM transacoes
        WHERE agendamento_id IS NOT NULL AND competencia IS NOT NULL
        GROUP BY agendamento_id, competencia
        HAVING COUNT(*) > 1
    ) THEN
        RAISE WARNING 'ux_transacoes_agendamento_competencia NAO criado: existem lançamentos duplicados por (agendamento_id, competencia). Corrija e rode a migration de novo.';
    ELSE
        CREATE UNIQUE INDEX IF NOT EXISTS ux_transacoes_agendamento_competencia
            ON transacoes (agendamento_id, competencia)
            WHERE agendamento_id IS NOT NULL AND competencia IS NOT NULL;
    END IF;
END
$$;
