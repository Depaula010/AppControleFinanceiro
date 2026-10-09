# Desvio 138 — Resumo matinal usa a chave do usuário

- **Estado:** aprovado (2026-10-09)
- **Features afetadas:** 013, 008, 010
- **Severidade:** correção

## Comportamento atual (Flask)
`daily_briefing.py:128` e `notification_triggers.py:134` chamam `generate_daily_briefing(briefing_data)` sem `usuario_id`: sempre a chave do sistema, sem contar uso nem respeitar "chave própria × sistema" (mesmo padrão do desvio 063). O clima do resumo também usa a chave do sistema (`daily_briefing_service.py:246,257`).

## Comportamento proposto (.NET)
Passar o `UserId` ao `ILlmClient` e ao `IWeatherClient`.

## Impacto nos clientes
O uso passa a contar no plano do usuário (hoje nenhum usuário tem plano com limite).

## Alternativas
Manter como está (paridade estrita; implementado assim).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-09 ("corrija da forma que achar melhor"; recomendação de decisoes-ondas-3-e-4.md).
