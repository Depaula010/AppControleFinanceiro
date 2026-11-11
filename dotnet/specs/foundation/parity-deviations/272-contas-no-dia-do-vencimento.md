# Desvio 272 — Conta avisada e cobrada no dia do vencimento; motor só lança o que é de cartão

- **Estado:** implementado (PR desta mudança)
- **Features afetadas:** 013
- **Severidade:** correção

## Comportamento atual
- O motor de agendamentos (00:05) lança sozinho, como paga, toda conta `FIXO` e `PARCELADO` que vence no dia, de qualquer conta.
- O check-in noturno e o "vence hoje" do resumo da manhã escondem o que já foi lançado. Resultado: a conta fixa fora do cartão só era avisada na véspera ("vence amanhã") e nunca era cobrada no check-in.
- Caso real (10/11/2026): Escola, Natação, Robô aspirador (5/18) e Clube foram lançadas às 00:05 pelo motor e não apareceram no check-in da noite (só o Salário, que é lembrete variável e o motor não lança). Confirmado no banco de produção: as 4 transações têm `created_at` 00:05 e `agendamento_id`.

## Comportamento proposto (.NET)
- O motor (e a recuperação dos últimos 3 dias, desvio 262) só lança `FIXO`/`PARCELADO` cuja conta é **cartão de crédito** (vai na fatura). Conta de outra conta fica para o usuário confirmar pelo check-in ("ok" ou o número), que já lança com `competencia` = vencimento. Se ele não confirmar, a conta segue como atrasada nos check-ins seguintes.
- O resumo da manhã avisa só o que **vence hoje** (contas e faturas); o bloco "Vence amanhã" some, inclusive "Amanhã:" no bloco informativo do cartão.
- A consulta sob pedido "o que vence amanhã?" não muda.

## Impacto nos clientes
Nenhuma rota muda. Mensagem da manhã sem "VENCE AMANHÃ"; check-in noturno passa a cobrar contas fixas e parceladas de conta corrente no dia do vencimento.

## Limitação aceita
Conta fixa ou parcelada fora do cartão só vira transação quando o usuário confirma no check-in noturno. Quem estiver sem check-in ativo (ou não responder) não terá essas contas lançadas sozinhas, e relatório mensal e previsão as mostram como pendentes até a confirmação.

## Alternativas
Manter o motor lançando e o check-in perguntando mesmo assim (conta contaria como paga antes da confirmação; mais código).

## Recomendação
Aprovar.

## Decisão do Rafael
Pedido na conversa de 10/11/2026 ("as notificações financeiras no check-in noturno e matinal devem acontecer no dia em que as contas vencem"), com as duas opções acima escolhidas.
