# Desvio 092 — Dashboard: top 10 de categorias devolve as de menor gasto

- **Estado:** implementado (feature 006, 2026-10-09)
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`analytics_service.py:69-70`: `ORDER BY total DESC LIMIT 10` sobre a soma de valores **negativos** (despesa é gravada negativa). A categoria que menos gastou vem primeiro e o `LIMIT` leva as 10 de menor gasto.

## Comportamento proposto (.NET)
`ORDER BY total ASC` (maior gasto primeiro). Nenhum campo muda.

## Impacto nos clientes
O bloco `gastos_categoria` passa a trazer as 10 maiores categorias. O Angular de hoje não exibe o bloco.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
