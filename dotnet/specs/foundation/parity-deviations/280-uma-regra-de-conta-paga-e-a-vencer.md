# Desvio 280 — Uma só regra de "conta paga" e de "a vencer"

- **Estado:** implementado
- **Features afetadas:** 006, 012, 013
- **Severidade:** correção (achado M6 da auditoria 2026-11-16)

## Comportamento atual (Flask / até o desvio 279)
Três regras diferentes para a mesma pergunta:
1. **Check-in** (013): `SchedulePaymentSql` (competência do vencimento, cadência contínua pelo último lançamento).
2. **"Contas Fixas" e "Vencimentos hoje/amanhã/semana"** (012, `FixedBillsReader`): `dia_execucao` do mês e "pago" = transação de mesma `descricao` no mês. Conta semanal/quinzenal/diária (que não tem dia do mês) aparecia no dia errado ou não aparecia; conta do dia 31 sumia em mês de 30 dias.
3. **Dashboard** (006, `DashboardAlerts`): próximo vencimento pela data de início e "pago" = qualquer transação do mês; nunca mostrava conta atrasada.

## Comportamento proposto (.NET)
- **Próximo vencimento** e **"já pago"** passam pelo mesmo SQL do check-in (`SchedulePaymentSql.DueDates`, `NextDue`, `Paid`):
  - mensal/anual: dia do mês limitado ao último dia (dia 31 vence no dia 30 em mês de 30 dias, e 28/29 em fevereiro); anual só no `mes_execucao`;
  - semanal/quinzenal/diária: último lançamento (competência) mais o intervalo, ou `data_inicio` se nunca lançado;
  - paga = transação do agendamento com a competência daquele vencimento (ou regra antiga para lançamento sem competência).
- **"Contas Fixas"** (`ListPendingAsync`): ocorrências do mês ainda não pagas; conta contínua com vencimento até o fim do mês entra, atrasada inclusive. O dia mostrado é o do vencimento (para contínuas e para dia 31 em mês curto).
- **"Vencimentos hoje/amanhã/semana"** (`ListDueBillsAsync(userId, first, last)`): uma consulta por janela de datas (antes eram dois trechos por dia do mês). Conta semanal aparece na semana em que vence e some depois de paga; conta do dia 31 aparece no dia 30 de mês curto. O parcelado só com parcela restante (desvio 270). Respeita `data_inicio`.
- **Dashboard** (`/api/dashboard/alerts`): `PaidDates` passa a conter os **vencimentos** já pagos pela regra do check-in e as contas contínuas usam o próximo vencimento do banco. Agora aparecem também as contas **atrasadas nos últimos 7 dias** e ainda não pagas, com `dias_restantes` negativo e `tipo` `danger` (o front já escrevia "Venceu há N dias"). Mais antigas que 7 dias seguem só no check-in. Parcelado quitado e tipos não listados deixam de aparecer.

## Impacto nos clientes
Nenhuma rota, campo ou mensagem muda de nome. O alerta atrasado é um item a mais na lista, com `dias_restantes` negativo (campo que já existia). Ordem: por data de vencimento (antes por dia do mês).

## Decisão do Rafael
"faça todos" na conversa de 16/11/2026.
