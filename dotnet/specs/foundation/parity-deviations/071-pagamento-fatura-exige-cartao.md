# Desvio 071 — Pagamento de fatura exige conta de cartão

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Aceita qualquer conta destino.

## Comportamento proposto (.NET)
400 se a conta destino não for cartão de crédito.

## Impacto nos clientes
Pagamento para conta comum deixa de ser aceito.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
