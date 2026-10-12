# Desvio 055 — OpenRoute: data de São Paulo e incremento atômico no limite diário

- **Estado:** implementado (feature 010, 2026-10-02)
- **Features afetadas:** 010, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`check_rate_limit`/`increment_rate_limit` não têm chamador (`travel_time_service.py:471-533`); a data é `datetime.now()` (fuso do container) e o incremento lê e grava sem atomicidade.

## Comportamento proposto (.NET)
O limitador é portado com data de São Paulo e incremento atômico (script Lua que lê `3` ou `"3"` e grava no mesmo formato). **Aplicar o limite** no intent de rota da 012 é decisão de produto e **não** está aprovada aqui: o limitador fica portado e sem uso, como no Flask.

## Impacto nos clientes
Nenhum por ora.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar a data e o incremento atômico; não ativar o limite.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Decisão de produto pendente:** o limite diário continua **sem uso** (como no Flask) até o Rafael decidir aplicá-lo.
