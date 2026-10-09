# Desvio 091 — Dashboard: gastos mensais em ordem decrescente de mês

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`analytics_service.py:45`: `ORDER BY mes DESC`. O Angular desenha as barras na ordem recebida (`dashboard.component.ts:199-200`), então o mês mais novo fica à esquerda do gráfico.

## Comportamento proposto (.NET)
Ordem crescente de mês (mais antigo à esquerda).

## Impacto nos clientes
Só a ordem do array `gastos_mensais` muda. Nenhum outro cliente usa o campo.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar (alternativa: o front inverte o array).

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
