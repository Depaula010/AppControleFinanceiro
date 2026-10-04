# Desvio 072 — Contas mensais: entradas inválidas viram 400 ou são ignoradas

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 005
- **Severidade:** correção

## Comportamento atual (Flask)
Nulos, tipos errados, NaN, datas inválidas e incluir_na_reserva como texto geram 500 ou gravam o texto None.

## Comportamento proposto (.NET)
400 ou campo ignorado, conforme a tabela da spec 005.

## Impacto nos clientes
Clientes veem 400 onde hoje veem 500.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 005.
