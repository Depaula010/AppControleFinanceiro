# Desvio 011 — Incremento de parcelas em três lugares

- **Estado:** aprovado
- **Features afetadas:** 004, 005, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`schedule_processor.py:142` (compara com `==`), `invoice_service.py:741` e `nightly_checkin_service.py:741` (`>=`) incrementam `parcelas_executadas`; o processador não grava `agendamento_id`, então a idempotência da fatura não o vê e a parcela pode duplicar.

## Comportamento proposto (.NET)
Uma rotina de domínio única, `>=`, sempre gravando `agendamento_id`, idempotente por (`agendamento_id`, mês).

## Impacto nos clientes
Nenhum campo muda; elimina parcela em dobro.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
