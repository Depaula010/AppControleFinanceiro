# Desvio 039 — Contas inativas deixam de ser usadas por outras features

- **Estado:** aprovado
- **Features afetadas:** 004, 011, 012
- **Severidade:** correção

## Comportamento atual (Flask)
`account_queries.py:36-40, 59-64, 86-91, 110-115, 133-138` e `account_service.py:23, 53-78, 104-111, 318` buscam conta por nome/tipo **sem** `ativa = true`: o bot e as transações podem lançar numa conta "removida".

## Comportamento proposto (.NET)
As features que resolvem conta (004, 011, 012) filtram `ativa = true`; a decisão de detalhe fica nas specs delas.

## Impacto nos clientes
Compras no bot deixam de cair em conta removida (caem na próxima conta ou no fallback).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar e levar para as specs 004, 011 e 012.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
