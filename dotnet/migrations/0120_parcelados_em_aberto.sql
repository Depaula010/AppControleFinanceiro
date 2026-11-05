-- Desvio 271: compras parceladas que entraram como despesa simples com "(n/N)" na descrição (ex.: extrato do Inter,
-- "ZP ROGGI 62899 (1/6)") e ainda têm parcela por vir viram conta parcelada (agendamento PARCELADO), como o gancho do
-- lançamento passa a fazer com as novas. Assim aparecem em Contas Mensais e no bot até a última parcela.
--
-- Para cada compra (usuário, conta, descrição sem o sufixo, total de parcelas), pega a última parcela lançada (n) e conta
-- quantas parcelas já passaram:
--   * cartão: as parcelas cujas faturas já fecharam (fechamento antes de hoje). A da fatura aberta o populador da fatura
--     lança na madrugada seguinte, na fatura certa;
--   * outras contas: as parcelas com data até hoje.
-- Compra que acabou antes (cancelada ou antecipada) fica de fora: se a mesma origem (ex.: o extrato importado) trouxe
-- lançamentos da conta depois da data em que a próxima parcela deveria ter vindo, e ela não veio, a compra terminou.
-- Só cria se sobrar parcela e se não houver parcelado ativo igual. A última parcela lançada é ligada ao agendamento
-- (agendamento_id), para o populador não lançá-la de novo. Comum SQL, sem bloco DO; rodar de novo não duplica.
WITH parcelas AS (
    SELECT t.id, t.usuario_id, t.conta_id, t.subcategoria_id, t.valor, t.data_transacao, t.origem,
           trim(regexp_replace(t.descricao, '\s*\(\d{1,3}/\d{1,3}\)\s*$', '')) AS base,
           ((regexp_match(t.descricao, '\((\d{1,3})/(\d{1,3})\)\s*$'))[1])::int AS n,
           ((regexp_match(t.descricao, '\((\d{1,3})/(\d{1,3})\)\s*$'))[2])::int AS total
    FROM transacoes t
    WHERE t.agendamento_id IS NULL
      AND t.valor < 0
      AND t.tipo_transacao = 'Despesa'
      AND t.descricao ~ '\(\d{1,3}/\d{1,3}\)\s*$'
),
ultima AS (
    SELECT DISTINCT ON (p.usuario_id, p.conta_id, lower(p.base), p.total) p.*
    FROM parcelas p
    WHERE p.total BETWEEN 2 AND 99 AND p.n BETWEEN 1 AND p.total AND p.base <> ''
    ORDER BY p.usuario_id, p.conta_id, lower(p.base), p.total, p.n DESC, p.data_transacao DESC, p.id DESC
),
contagem AS (
    SELECT u.*,
           u.n + (
               SELECT count(*)::int
               FROM generate_series(u.n + 1, u.total) AS k
               CROSS JOIN LATERAL (SELECT (u.data_transacao + make_interval(months => k - u.n))::date AS dia) parcela
               CROSS JOIN LATERAL (
                   SELECT make_date(EXTRACT(YEAR FROM parcela.dia)::int, EXTRACT(MONTH FROM parcela.dia)::int,
                                    LEAST(c.dia_fechamento,
                                          EXTRACT(DAY FROM date_trunc('month', parcela.dia) + INTERVAL '1 month - 1 day')::int)) AS no_mes,
                          (date_trunc('month', parcela.dia) + INTERVAL '1 month')::date AS mes_seguinte
               ) f
               CROSS JOIN LATERAL (
                   SELECT CASE
                              WHEN parcela.dia <= f.no_mes THEN f.no_mes
                              ELSE make_date(EXTRACT(YEAR FROM f.mes_seguinte)::int, EXTRACT(MONTH FROM f.mes_seguinte)::int,
                                             LEAST(c.dia_fechamento,
                                                   EXTRACT(DAY FROM f.mes_seguinte + INTERVAL '1 month - 1 day')::int))
                          END AS fechamento
               ) ciclo
               WHERE CASE
                         WHEN c.tipo_conta = 'Cartão de Crédito' AND c.dia_fechamento > 0 AND c.dia_vencimento > 0
                             THEN ciclo.fechamento < CURRENT_DATE
                         ELSE parcela.dia <= CURRENT_DATE
                     END
           ) AS executadas
    FROM ultima u
    JOIN contas c ON c.id = u.conta_id
),
candidatas AS (
    SELECT x.*
    FROM contagem x
    WHERE x.executadas < x.total
      AND NOT (x.origem IS NOT NULL AND EXISTS (
          SELECT 1 FROM transacoes o
          WHERE o.usuario_id = x.usuario_id
            AND o.conta_id = x.conta_id
            AND o.origem = x.origem
            AND o.data_transacao > (x.data_transacao + INTERVAL '1 month')::date))
      AND NOT EXISTS (
          SELECT 1 FROM agendamentos a
          WHERE a.usuario_id = x.usuario_id
            AND a.conta_id = x.conta_id
            AND a.ativo = TRUE
            AND a.tipo_agendamento = 'PARCELADO'
            AND a.total_parcelas = x.total
            AND lower(trim(a.descricao)) = lower(x.base))
),
criados AS (
    INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento,
                              periodicidade, data_inicio, dia_execucao, total_parcelas, parcelas_executadas, ativo)
    SELECT x.usuario_id, x.conta_id, x.subcategoria_id, x.base, -x.valor, 'PARCELADO', 'MENSAL',
           (x.data_transacao + make_interval(months => x.executadas + 1 - x.n))::date,
           EXTRACT(DAY FROM x.data_transacao)::int, x.total, x.executadas, TRUE
    FROM candidatas x
    RETURNING id, usuario_id, conta_id, descricao, total_parcelas
)
UPDATE transacoes t
SET agendamento_id = criados.id
FROM criados
JOIN candidatas x ON x.usuario_id = criados.usuario_id
                 AND x.conta_id = criados.conta_id
                 AND x.base = criados.descricao
                 AND x.total = criados.total_parcelas
WHERE t.id = x.id;
