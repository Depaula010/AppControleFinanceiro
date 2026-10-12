# Desvio 049 — Contas: limite_credito zero respondido como null

- **Estado:** rejeitado
- **Features afetadas:** 002
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
O teste de falsidade (`if conta.limite_credito else None`, `api.py:477, 636`) devolve `null` para 0; o banco guarda `0.00`.

## Comportamento proposto (.NET)
Devolver `0.0`.

## Impacto nos clientes
Nenhum cliente conhecido distingue (o front usa `if (account.limite)`).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Rejeitar: sem ganho; manter a paridade.

## Decisão do Rafael
Rejeitado em 2026-10-02 ("aprovo as recomendações"): o .NET mantém o comportamento do Flask.
