# Desvio 252 — Check-in confirma lembretes variáveis perguntando o valor e lista o que não foi confirmado

- **Estado:** implementado (PRs #34, #35 e #38)
- **Features afetadas:** 012, 013
- **Severidade:** correção

## Comportamento atual (Flask)
O check-in oferece `LEMBRETE_VARIAVEL` (salário, seguro, água, gasolina), mas o executor os recusava (`NotEligible`) e a resposta descartava os itens em silêncio: "ok" confirmava 1 de 5 contas sem avisar.

## Comportamento proposto (.NET)
(1) Itens pedidos que não viram lançamento voltam na resposta, com o motivo, e o log 12401 registra o id do agendamento (PR #34). (2) O executor aceita `LEMBRETE_VARIAVEL` só quando o usuário confirma (`AllowVariableReminder`); o motor automático nunca os lança (PR #35). (3) Depois do "ok" o bot pergunta o valor de cada lembrete variável (número, "ok" para o previsto, "pular"), em chave própria `nightly_checkin_values:{numero}:{id}` e na mesma janela de 1 h (PR #38).

## Impacto nos clientes
Mensagem do check-in e das respostas muda (novas perguntas e a lista de não confirmados). A sessão do Flask no Redis não muda de formato.

## Alternativas
Lançar o valor previsto às cegas (PR #35 antes do #38; rejeitado por poder registrar valor diferente do pago). Manter o descarte silencioso (rejeitado).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido pelo Rafael nas conversas de 15 a 20/10/2026 ("Perguntar o valor" escolhido na pergunta de 20/10). Implementado nos PRs #34, #35 e #38.
