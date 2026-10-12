# Desvio 132 — Relatório mensal: sinal das despesas

- **Estado:** implementado (feature 013, 2026-10-09); aprovado em 2026-10-09. `ABS` nas somas de despesa (totais, top 5 do maior para o menor, comparação, potes, contas pagas); vale também para `test-monthly-report` (009c).
- **Features afetadas:** 013, 009c (`test-monthly-report`)
- **Severidade:** correção

## Comportamento atual (Flask)
`monthly_report_service.py:60-101,112-145,172-205,216-249,266-275` soma `valor` de `Despesa`, que é gravado **negativo**: `total_despesas` sai negativo, `saldo_periodo = rendas − despesas` na verdade **soma** os dois, o top 5 vem na ordem inversa (`ORDER BY SUM DESC` põe o menor gasto primeiro) com valores negativos, `variacao_percentual` fica sempre 0 (`mes_anterior > 0` nunca é verdade) e todo pote aparece como `✅` (percentual negativo). Os relatórios nunca saíram em produção (desvio 020: `localhost:5000`); só `test-monthly-report` os gera.

## Comportamento proposto (.NET)
`ABS` nas somas de despesa (totais, top, comparação, potes, status de contas), `ORDER BY` pelo maior gasto, saldo `rendas − despesas`.

## Impacto nos clientes
Sem o 132, ligar o desvio 020 faria os usuários receberem relatórios com números errados. Os campos JSON de `dados` em `test-monthly-report` mudam de sinal.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar **antes** de ligar `JOBS_ENABLED` em produção.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
