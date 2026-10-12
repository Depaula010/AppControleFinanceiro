# Desvio 084 — Configurações: tipos JSON inesperados dão 400

- **Estado:** implementado (feature 007, 2026-10-05)
- **Features afetadas:** 007
- **Severidade:** correção

## Comportamento atual (Flask)
Tipos errados geram 500.

## Comportamento proposto (.NET)
400 com a mensagem da própria rota.

## Impacto nos clientes
Clientes veem 400.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 007.
