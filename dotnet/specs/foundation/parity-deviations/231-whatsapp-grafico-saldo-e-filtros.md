# Desvio 231 — Gráficos: saldo da linha ignora o histórico anterior ao período e filtros inconsistentes

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`generate_line_chart` soma o `saldo_inicial` de **todas** as contas (inclusive inativas) ao acumulado `SUM(valor) OVER (ORDER BY data_transacao)` calculado **só sobre as transações do período**; transações anteriores ao início da janela nunca entram, então o saldo mostrado não é o saldo real (o do `Consulta Saldo` é `saldo_inicial` + todas as transações consolidadas das contas ativas). Além disso, pizza e barras não filtram `consolidada` (a análise e a previsão filtram) e as barras somam `Renda` mas não `Receita`. Confirmado pela leitura do SQL e pelo harness (`grafico_linha*`).

## Comportamento proposto (.NET)
Linha: saldo inicial das contas **ativas** mais as transações consolidadas **anteriores** ao início da janela, mais o acumulado do período. Pizza e barras: só transações consolidadas.

## Impacto nos clientes
O gráfico de saldo passa a bater com o saldo consultado; gráficos deixam de incluir lançamentos ainda não consolidados.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
