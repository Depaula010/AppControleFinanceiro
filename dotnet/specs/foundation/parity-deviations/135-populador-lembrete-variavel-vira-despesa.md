# Desvio 135 — Populador de fatura lança `LEMBRETE_VARIAVEL` como despesa

- **Estado:** proposto
- **Features afetadas:** 013, 004, 011
- **Severidade:** correção (decisão de produto)

## Comportamento atual (Flask)
`invoice_service.py:625-645,685-738,764-765`: `process_invoice_schedules` seleciona também `LEMBRETE_VARIAVEL` e grava uma transação com o `valor_previsto` na fatura, contando-a como "lembrete". O motor (`schedule_processor.py:91-104`) trata o mesmo tipo só como lembrete ("me diga o valor exato deste mês") e não lança nada. Resultado: conta variável de cartão entra na fatura com o valor previsto sem o usuário informar o valor real, e o `agendamento_id` gravado faz o check-in considerá-la paga.

## Comportamento proposto (.NET)
Não lançar `LEMBRETE_VARIAVEL` no populador; o usuário informa o valor real pelo bot.

## Impacto nos clientes
Contas variáveis de cartão deixam de aparecer na fatura com valor previsto.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Decisão de produto do Rafael: confirmar qual era a intenção.

## Decisão do Rafael
(pendente)
