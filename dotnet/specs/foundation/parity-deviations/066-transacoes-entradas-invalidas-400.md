# Desvio 066 — Transações: entradas inválidas viram 400

- **Estado:** implementado (feature 004, 2026-10-04)
- **Features afetadas:** 004
- **Severidade:** correção

## Comportamento atual (Flask)
Nulos, NaN e descrição acima de 255 caracteres geram 500.

## Comportamento proposto (.NET)
400 com mensagem em português.

## Impacto nos clientes
Clientes veem 400 onde hoje veem 500.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 004.
