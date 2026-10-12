-- ============================================================================
-- Ajuste de dados da virada: faturas de cartão com vencimento no mês seguinte ao do fechamento (desvio 065)
--
-- O Flask criava faturas por dois caminhos com fórmulas diferentes: o populador mensal (vencimento no mesmo mês do
-- fechamento) e o lançamento de compra (vencimento um mês depois, quando V >= F). Resultado em produção (2026-10-02):
-- faturas "deslocadas" (ex.: fecha 13/10, vence 20/11) e pares do mesmo ciclo. O .NET usa uma regra única (vencimento no
-- mesmo mês do fechamento quando o dia de vencimento >= dia de fechamento) e localiza a fatura por (conta, vencimento);
-- sem este ajuste, as próximas compras caem em faturas do ciclo errado.
--
-- O que faz, para cada fatura de cartão cujo vencimento NÃO está no mês do fechamento:
--   * se já existe a fatura do mesmo ciclo com vencimento no mês do fechamento: move as transações para ela (a Aberta
--     prevalece no status) e apaga a fatura deslocada;
--   * senão: corrige o vencimento da fatura para o mês do fechamento (mesmo dia de vencimento).
--
-- Só SQL comum (sem bloco DO $$): roda no HeidiSQL (execute tudo com F9) e no psql. Numa transação; a conferência
-- no fim mostra o resultado ANTES do COMMIT: se estiver certo, o COMMIT já está no script; se quiser revisar antes,
-- troque o COMMIT final por ROLLBACK, rode, confira e depois rode de novo com COMMIT.
-- Idempotente (depois de aplicado, não encontra mais faturas deslocadas). Rode UMA vez no dia da virada, depois do backup.
-- ============================================================================
BEGIN;

-- 1) Faturas deslocadas e o vencimento correto de cada uma.
CREATE TEMP TABLE _ajuste_faturas AS
SELECT s.id AS shifted_id,
       s.status AS shifted_status,
       s.conta_id,
       make_date(EXTRACT(YEAR FROM s.data_fechamento)::int,
                 EXTRACT(MONTH FROM s.data_fechamento)::int,
                 LEAST(EXTRACT(DAY FROM s.data_vencimento)::int,
                       EXTRACT(DAY FROM (date_trunc('month', s.data_fechamento) + interval '1 month - 1 day'))::int)) AS target_due,
       NULL::int AS target_id
FROM faturas s
JOIN contas c ON c.id = s.conta_id
WHERE c.tipo_conta = 'Cartão de Crédito'
  AND date_trunc('month', s.data_vencimento) <> date_trunc('month', s.data_fechamento);

-- 2) Para cada uma, a fatura (não deslocada) do mesmo ciclo que já tem o vencimento correto, se existir.
UPDATE _ajuste_faturas a
SET target_id = t.id
FROM faturas t
WHERE t.conta_id = a.conta_id
  AND t.data_vencimento = a.target_due
  AND t.id <> a.shifted_id
  AND t.id NOT IN (SELECT shifted_id FROM _ajuste_faturas);

-- 3) Ciclos duplicados: as transações vão para a fatura correta, a Aberta prevalece e a deslocada é apagada.
UPDATE transacoes t
SET fatura_id = a.target_id
FROM _ajuste_faturas a
WHERE t.fatura_id = a.shifted_id AND a.target_id IS NOT NULL;

UPDATE faturas f
SET status = 'Aberta'
FROM _ajuste_faturas a
WHERE f.id = a.target_id AND a.shifted_status = 'Aberta' AND f.status <> 'Aberta';

DELETE FROM faturas f
USING _ajuste_faturas a
WHERE f.id = a.shifted_id AND a.target_id IS NOT NULL;

-- 4) Demais deslocadas: corrigir o vencimento. Em duas etapas (via ano 3026) para não esbarrar na chave única
--    (conta, vencimento) enquanto os vencimentos trocam de lugar.
UPDATE faturas f
SET data_vencimento = a.target_due + interval '1000 years'
FROM _ajuste_faturas a
WHERE f.id = a.shifted_id AND a.target_id IS NULL;

UPDATE faturas f
SET data_vencimento = a.target_due
FROM _ajuste_faturas a
WHERE f.id = a.shifted_id AND a.target_id IS NULL;

-- 5) Conferência: uma fatura por ciclo, todas com vencimento no mês do fechamento.
SELECT f.id, f.conta_id, f.data_fechamento, f.data_vencimento, f.status,
       (SELECT count(*) FROM transacoes t WHERE t.fatura_id = f.id) AS transacoes
FROM faturas f
JOIN contas c ON c.id = f.conta_id
WHERE c.tipo_conta = 'Cartão de Crédito'
ORDER BY f.conta_id, f.data_fechamento;

COMMIT;
