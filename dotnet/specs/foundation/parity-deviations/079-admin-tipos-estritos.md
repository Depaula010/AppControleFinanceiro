# Desvio 079 — Tipos estritos no corpo das rotas admin

- **Estado:** aprovado (onda 2, 2026-10-04)
- **Features afetadas:** 009
- **Severidade:** correção

## Comportamento atual (Flask)
Tipos errados geram 500 ou coerção silenciosa.

## Comportamento proposto (.NET)
ip/reason texto, usuario_id inteiro, ativo booleano; senão 400.

## Impacto nos clientes
Clientes passam a receber 400.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-04 ("aprovo as recomendações", decisoes-onda-2.md). Detalhes e fonte na spec da feature 009.
