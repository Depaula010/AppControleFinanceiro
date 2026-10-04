# Desvio 069 — PUT recalcula fatura_id

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Mudar conta, data ou tipo mantém a fatura antiga.

## Comportamento proposto (.NET)
fatura_id é recalculado quando esses campos mudam.

## Impacto nos clientes
Valores de fatura passam a refletir a edição.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
