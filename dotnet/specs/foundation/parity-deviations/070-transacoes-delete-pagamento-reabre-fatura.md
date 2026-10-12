# Desvio 070 — Apagar Pagamento Fatura reabre a fatura

- **Estado:** implementado (feature 004, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
A fatura continua Paga depois de apagar o pagamento.

## Comportamento proposto (.NET)
A fatura volta a Fechada ou Aberta.

## Impacto nos clientes
Saldo da fatura volta a ficar devedor.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
