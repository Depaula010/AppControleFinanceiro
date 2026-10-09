# Desvio 230 — Gráfico de Gastos: período sem validação do tipo e da faixa

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 012
- **Severidade:** correção

## Comportamento atual (Flask)
`GraficoGastosIntent` repassa `periodo_dias` e `num_meses` do Gemini sem validar (`analytics_intents.py:133-143`). Texto, lista ou `null` (`"30"`, `[30]`, `null`) fazem o `timedelta` do `chart_service` falhar dentro do `try` e a resposta é "Não há dados suficientes"; decimal (`7.5`) e booleano (`true`) funcionam e aparecem na legenda como `7.5 dias` e `True dias`; valores negativos desenham um gráfico do futuro (vazio); valores enormes (`99999999999`) estouram o calendário e também viram "sem dados". Confirmado pelo harness (`grafico_pizza_periodo_*`, `grafico_barras_meses_*`).

## Comportamento proposto (.NET)
Aceitar só inteiro de 1 a 3650 (pizza) e de 1 a 120 (barras e linha); qualquer outra coisa usa o padrão (30 dias, 6 meses). A legenda mostra sempre um inteiro.

## Impacto nos clientes
Quem pede "gráfico dos últimos 3 meses" e recebe do Gemini um valor estranho passa a receber o gráfico padrão em vez de uma mensagem de falta de dados.

## Alternativas
Manter como está (paridade estrita), que é o que a 012 implementa.

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
