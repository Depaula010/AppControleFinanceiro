# Desvio 013 — Motor de agendamentos sem idempotência e lembrete variável

- **Estado:** aprovado
- **Features afetadas:** 009, 013
- **Severidade:** correção

## Comportamento atual (Flask)
`schedule_processor.py:107-146` não tem lock nem idempotência: duas chamadas no mesmo dia duplicam lançamentos. `LEMBRETE_VARIAVEL` só dispara se `notificar_antes_dias = 0` (a query filtra `dia_execucao = hoje` e depois subtrai `notificar_antes_dias`). Cria um engine novo a cada chamada. O job não está nos labels do Ofelia: só roda por `POST /admin/run-motor-agendamentos`.

## Comportamento proposto (.NET)
Lock Redis `job_lock:motor-agendamentos` e idempotência por (`agendamento_id`, data). `LEMBRETE_VARIAVEL` dispara em `dia_execucao − notificar_antes_dias`. A rota continua existindo e também é agendada pelo Quartz (diário); confirmar se o bot ainda a chama.

## Impacto nos clientes
Lembretes variáveis com antecedência passam a funcionar (antes nunca disparavam).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. Confirmar quem dispara a rota hoje.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** confirmar quem dispara `/admin/run-motor-agendamentos` hoje; o gatilho externo é desligado na virada se o Quartz assumir.
