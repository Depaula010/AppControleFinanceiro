# Desvio 056 — OpenRoute: não portar o código morto e quebrado do travel_time_service

- **Estado:** implementado (feature 010, 2026-10-02)
- **Features afetadas:** 010
- **Severidade:** correção

## Comportamento atual (Flask)
`get_next_event_after` e `suggest_alternative_time` chamam `CalendarManagementService.get_events_by_date_range`, que **não existe** (`travel_time_service.py:245, 394`); `check_conflict` não tem chamador.

## Comportamento proposto (.NET)
Não portar (RN-010-64).

## Impacto nos clientes
Nenhum: nada os usa.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
