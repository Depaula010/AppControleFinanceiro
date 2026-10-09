# Desvio 170 — Feedback da transação soma só a subcategoria da transação no pote

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`transaction_feedback_service.calcular_status_pote` junta o pote a `PoteSubCategorias` e filtra `psc.subcategoria_id = :subcategoria_id`. O gasto do período sai então só da subcategoria da transação, não de todas as subcategorias do pote. O aviso de orçamento (`budget_validation_service`, 011) soma todas as subcategorias do pote: os dois textos podem divergir para o mesmo pote.

## Comportamento proposto (.NET)
Somar o gasto de todas as subcategorias do pote no período (mesma regra do aviso de orçamento).

## Impacto nos clientes
O bloco "Pote" da mensagem de feedback passa a mostrar o gasto total do pote.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 012 implementa até a decisão.**

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
