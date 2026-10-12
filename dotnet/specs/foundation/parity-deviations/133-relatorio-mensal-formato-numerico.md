# Desvio 133 — Relatório mensal: formato numérico em inglês

- **Estado:** implementado (feature 013, 2026-10-09); aprovado em 2026-10-09. Relatório mensal em pt-BR (`R$ 1.234,56`, `+12,5%`).
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
`monthly_report_service.py:403-436` usa `f"{valor:,.2f}"` e `{variacao:+.1f}`: o texto sai como `R$ 1,234.56` (milhar com vírgula, decimal com ponto), enquanto todas as outras mensagens usam `R$ 1.234,56`.

## Comportamento proposto (.NET)
`R$ 1.234,56` (cultura pt-BR) e vírgula decimal no percentual (`+12,5%`).

## Impacto nos clientes
Texto do relatório mensal.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar com o 132.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
