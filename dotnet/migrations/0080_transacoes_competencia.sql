-- Desvio 253: a que vencimento uma transação de agendamento se refere (check-in noturno e motor).
-- Sem isso a consulta do check-in só sabia "paga no mesmo mês" ou "até 20 dias depois", e um pagamento atrasado de um mês
-- escondia o vencimento do mês seguinte. Só aditiva e idempotente; linhas antigas ficam NULL (as consultas mantêm a regra antiga para elas).
ALTER TABLE transacoes ADD COLUMN IF NOT EXISTS competencia date NULL;

CREATE INDEX IF NOT EXISTS ix_transacoes_agendamento_competencia
    ON transacoes (agendamento_id, competencia)
    WHERE agendamento_id IS NOT NULL;
