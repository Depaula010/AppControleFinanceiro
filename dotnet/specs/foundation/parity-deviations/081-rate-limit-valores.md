# Desvio 081 — Valores do rate limit (desligado por padrão)

- **Estado:** implementado (feature 009-0, 2026-10-04) — limitador e valores implementados, desligado por padrão (`RATE_LIMIT_ENABLED=false`); ligar só depois de conferir os 429 em produção
- **Features afetadas:** 009
- **Severidade:** segurança

## Comportamento atual (Flask)
O Flask declara 100/h, sem comprovação de que barra.

## Comportamento proposto (.NET)
300/h geral e 60/h em /admin, controlados por RATE_LIMIT_ENABLED (padrão false).

## Impacto nos clientes
Condição: ligar só depois de conferir os logs de 429 em produção.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
