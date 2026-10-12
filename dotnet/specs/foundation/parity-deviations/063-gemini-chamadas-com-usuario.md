# Desvio 063 — Gemini: três chamadas passam a usar o usuário e a chave dele

- **Estado:** aprovado
- **Features afetadas:** 010, 006
- **Severidade:** correção

## Comportamento atual (Flask)
`generate_ai_insights`, `generate_forecast_insights` e `suggest_best_slot_with_ai` usam o modelo global sem `usuario_id` (`analytics_service.py:353`, `forecast_service.py:435`, `free_time_finder_service.py:535`): não respeitam "chave própria × sistema", não contam uso nem limite do plano e, pelo `genai.configure` global, podem usar a chave do último usuário.

## Comportamento proposto (.NET)
Passar o `UserId` ao `ILlmClient` (a chave e os limites do 008 passam a valer).

## Impacto nos clientes
O uso dessas três funções passa a contar no plano do usuário (hoje nenhum usuário tem plano com limite).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
