-- ============================================================================
-- Ajuste de dados da virada: faturas de cartão com vencimento no mês seguinte ao do fechamento (desvio 065)
--
-- O Flask criava faturas por dois caminhos com fórmulas diferentes: o populador mensal (vencimento no mesmo mês do
-- fechamento) e o lançamento de compra (vencimento um mês depois, quando V >= F). Resultado em produção (2026-10-02):
-- faturas "deslocadas" (ex.: fecha 13/10, vence 20/11) e pares do mesmo ciclo. O .NET usa uma regra única (vencimento no
-- mesmo mês do fechamento quando o dia de vencimento >= dia de fechamento) e localiza a fatura por (conta, vencimento);
-- sem este ajuste, as próximas compras caem em faturas do ciclo errado.
--
-- O que faz, para cada fatura de cartão cujo vencimento NÃO está no mês do fechamento (em ordem de fechamento):
--   * se já existe a fatura do mesmo ciclo com vencimento no mês do fechamento: move as transações para ela (a Aberta
--     prevalece no status) e apaga a fatura deslocada;
--   * senão: apenas corrige o vencimento da fatura para o mês do fechamento (mesmo dia de vencimento).
-- Idempotente. Rode UMA vez no dia da virada, depois do backup, e confira o SELECT final.
-- ============================================================================
BEGIN;

DO $$
DECLARE
    s RECORD;
    t RECORD;
    target date;
    due_day int;
    month_end int;
BEGIN
    FOR s IN
        SELECT f.id, f.conta_id, f.data_fechamento, f.data_vencimento, f.status
        FROM faturas f
        JOIN contas c ON c.id = f.conta_id
        WHERE c.tipo_conta = 'Cartão de Crédito'
          AND date_trunc('month', f.data_vencimento) <> date_trunc('month', f.data_fechamento)
        ORDER BY f.conta_id, f.data_fechamento, f.data_vencimento, f.id
    LOOP
        due_day := EXTRACT(DAY FROM s.data_vencimento)::int;
        month_end := EXTRACT(DAY FROM (date_trunc('month', s.data_fechamento) + interval '1 month - 1 day'))::int;
        target := make_date(EXTRACT(YEAR FROM s.data_fechamento)::int,
                            EXTRACT(MONTH FROM s.data_fechamento)::int,
                            LEAST(due_day, month_end));

        SELECT id, status INTO t FROM faturas
        WHERE conta_id = s.conta_id AND data_vencimento = target AND id <> s.id;

        IF FOUND THEN
            UPDATE transacoes SET fatura_id = t.id WHERE fatura_id = s.id;
            IF s.status = 'Aberta' AND t.status <> 'Aberta' THEN
                UPDATE faturas SET status = 'Aberta' WHERE id = t.id;
            END IF;
            DELETE FROM faturas WHERE id = s.id;
            RAISE NOTICE 'fatura % fundida em % (ciclo %)', s.id, t.id, s.data_fechamento;
        ELSE
            UPDATE faturas SET data_vencimento = target WHERE id = s.id;
            RAISE NOTICE 'fatura % : vencimento % -> %', s.id, s.data_vencimento, target;
        END IF;
    END LOOP;
END $$;

-- Conferência: não deve restar fatura de cartão com vencimento fora do mês do fechamento.
SELECT f.id, f.conta_id, f.data_fechamento, f.data_vencimento, f.status,
       (SELECT count(*) FROM transacoes t WHERE t.fatura_id = f.id) AS transacoes
FROM faturas f
JOIN contas c ON c.id = f.conta_id
WHERE c.tipo_conta = 'Cartão de Crédito'
ORDER BY f.conta_id, f.data_fechamento;

COMMIT;
