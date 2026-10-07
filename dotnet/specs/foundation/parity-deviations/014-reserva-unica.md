# Desvio 014 — Reserva de emergência calculada em três lugares

- **Estado:** aprovado; implementado nas features 005 e 011 (2026-10-05); pendente na 012
- **Features afetadas:** 005, 011, 012
- **Severidade:** correção

## Comportamento atual (Flask)
Fórmula em `emergency_reserve_service.py`, em SQL no `whatsapp_handler.py:774` e em `api.py:2335` (esta não filtra o tipo `FIXO`/`LEMBRETE_VARIAVEL`).

## Comportamento proposto (.NET)
Uma implementação de domínio (R9 de `business-rules.md`) com filtro de tipo.

## Impacto nos clientes
`reserva_emergencia.meta` em `/api/bills/summary` pode mudar se existirem agendamentos `PARCELADO` marcados para a reserva.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
