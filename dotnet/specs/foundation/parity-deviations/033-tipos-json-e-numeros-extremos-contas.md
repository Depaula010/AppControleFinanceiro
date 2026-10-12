# Desvio 033 — Contas: tipo JSON errado e números extremos

- **Estado:** implementado (feature 002, 2026-10-02)
- **Features afetadas:** 002
- **Severidade:** correção

## Comportamento atual (Flask)
Nome/tipo que não são texto (`null`, número, lista) dão `AttributeError` e 500 (`api.py:362-363, 531, 548`); `saldo_inicial`/`limite_credito` de módulo ≥ 10^13 ou `"inf"` estouram o `numeric(15,2)` e dão 500; `"nan"` é gravado e a resposta sai com `NaN` (JSON inválido).

## Comportamento proposto (.NET)
Não-texto em nome/tipo é tratado como ausente (400 com a mensagem de obrigatório do POST ou do PUT); número não finito ou fora de `numeric(15,2)` ⇒ 400 `"Saldo inicial deve ser um número válido"` / `"Limite de crédito deve ser um número válido"`.

## Impacto nos clientes
400 no lugar de 500 para entradas absurdas. Nenhum cliente conhecido envia isso.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
