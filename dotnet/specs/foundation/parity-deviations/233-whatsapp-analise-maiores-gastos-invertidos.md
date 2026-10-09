# Desvio 233 — Análise Inteligente: "Maiores gastos" lista os menores e os dias da semana saem em inglês

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
A consulta dos maiores gastos do mês usa `ORDER BY valor DESC LIMIT 5` sobre valores **negativos**: vêm primeiro as despesas de menor valor absoluto. O padrão semanal usa `TO_CHAR(data_transacao, 'Day')` (sempre inglês: `Monday`, `Wednesday`) no prompt de um relatório em português. Confirmado pelo harness (`analise_completa`) e pelo teste de integração `SpendingAnalysis_...`.

## Comportamento proposto (.NET)
Ordenar por `ABS(valor) DESC` (com desempate por id) e traduzir os dias da semana para português no código.

## Impacto nos clientes
A seção "Maiores Gastos" do relatório passa a trazer os maiores gastos de fato.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar junto com o 158.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
