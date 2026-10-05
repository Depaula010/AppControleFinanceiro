# Desvio 090 — Dashboard: janela dos gastos mensais começa no meio de um mês

- **Estado:** proposto
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`analytics_service.py:32`: `data_inicio = dia 1 do mês atual - 30 * meses dias`. Para `meses=3` em outubro começa em 3 de julho, então o primeiro mês de `gastos_mensais` sai parcial e menor que o real.

## Comportamento proposto (.NET)
Início = dia 1 do mês de `hoje - (meses - 1)` meses: `meses` meses civis completos incluindo o atual (`meses=1` é só o mês atual).

## Impacto nos clientes
O gráfico mensal do Angular mostra o mês inicial com o valor completo. Para `meses=N` passam a sair N barras (hoje podem sair N+1).

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
