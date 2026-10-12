# Desvio 093 — Dashboard: alertas MENSAL e ANUAL ignoram data_inicio

- **Estado:** implementado (feature 006, 2026-10-09)
- **Features afetadas:** 006
- **Severidade:** correção

## Comportamento atual (Flask)
`api.py:1252-1269` (MENSAL) e `:1228-1235` (ANUAL) não olham `data_inicio`. Um agendamento que ainda não começou (ex.: parcelas do bot com `data_inicio` no mês seguinte) já aparece como conta a vencer neste mês.

## Comportamento proposto (.NET)
Só considerar vencimentos com data maior ou igual a `data_inicio`; antes disso o agendamento não gera alerta.

## Impacto nos clientes
Alertas falsos deixam de aparecer; nenhum campo muda.

## Alternativas
Manter como está (paridade estrita). **É o que a feature 006 implementa até haver aprovação.**

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
