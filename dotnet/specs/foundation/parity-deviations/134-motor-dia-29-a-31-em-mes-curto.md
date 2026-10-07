# Desvio 134 — Motor: agendamento de dia 29 a 31 não dispara em mês curto

- **Estado:** proposto
- **Features afetadas:** 013, 005
- **Severidade:** correção

## Comportamento atual (Flask)
`schedule_processor.py:71` filtra `a.dia_execucao = :dia_hoje`. Um `FIXO` ou `PARCELADO` com `dia_execucao = 31` nunca roda em abril, junho, setembro e novembro (30 dias) nem em fevereiro; o de dia 30 e 29, em fevereiro. A spec 005 (RN-005-05) afirma que "dia inexistente cai no último dia do mês pelo motor", o que o código não faz (só `process_invoice_schedules` corta o dia, `invoice_service.py:703-708`).

## Comportamento proposto (.NET)
No último dia do mês, executar também os agendamentos com `dia_execucao` maior que o último dia.

## Impacto nos clientes
Contas de fim de mês passam a ser lançadas em todo mês.

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar.

## Decisão do Rafael
(pendente)
