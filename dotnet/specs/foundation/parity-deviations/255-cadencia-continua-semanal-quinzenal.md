# Desvio 255 — Agendamentos SEMANAL, QUINZENAL e DIARIA têm cadência contínua

- **Estado:** implementado (PR #42)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual (Flask)
`SEMANAL`, `QUINZENAL` e `DIARIA` são aceitas no cadastro, mas o motor e o check-in tratam todo agendamento como mensal (um `dia_execucao` por mês). A Gasolina (R$ 100 por semana) e a Terapia (quinzenal) eram lançadas uma vez por mês.

## Comportamento proposto (.NET)
O próximo vencimento é a competência do último lançamento + 7, 14 ou 1 dia (ou `data_inicio` se nunca foi lançado). Check-in, motor, lembrete e regra de "paga" usam esse vencimento; um vencimento por vez. Decidido com o Rafael: "a partir do último lançamento" (e não grade fixa).

## Impacto nos clientes
A Terapia passa a ser lançada pelo motor a cada 14 dias (26 por ano, não 24); a Gasolina aparece no check-in a cada 7 dias. Dashboard, alertas financeiros e listagem de contas fixas continuam mensais.

## Alternativas
Grade fixa por dia da semana ou data âncora (rejeitada: os lançamentos reais não seguem uma grade). Manter mensal (rejeitado: o Rafael confirmou que Gasolina é semanal e Terapia quinzenal).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado pelo Rafael na conversa de 20/10/2026 ("100 por semana", "duas sessões por mês"; modelo "a partir do último lançamento" escolhido). Implementado no PR #42.
