# Desvio 007 — Dois cálculos de saldo

- **Estado:** aprovado
- **Features afetadas:** 002, 006
- **Severidade:** correção

## Comportamento atual (Flask)
`saldo_total` do dashboard considera só transações consolidadas e contas ativas (`get_saldo_contas`, `account_service.py:145`); `saldo` de `GET /api/accounts` soma todas as transações (`api.py:293`).

## Comportamento proposto (.NET)
Um único cálculo: `saldo_inicial + SUM(valor)` das transações consolidadas, em todos os lugares.

## Impacto nos clientes
O saldo mostrado em Contas pode mudar se houver transações não consolidadas.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
