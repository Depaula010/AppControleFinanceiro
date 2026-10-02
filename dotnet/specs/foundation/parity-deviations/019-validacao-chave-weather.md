# Desvio 019 — Validação de chave weather usa outro provedor

- **Estado:** aprovado
- **Features afetadas:** 007, 010
- **Severidade:** correção

## Comportamento atual (Flask)
`POST /api/user/api-keys/validate` valida `weather` contra OpenWeatherMap, mas o serviço de clima usa WeatherAPI.com (`api.py:2645-2658`, `weather_service.py`).

## Comportamento proposto (.NET)
Validar contra WeatherAPI.com.

## Impacto nos clientes
Chaves válidas da WeatherAPI deixam de ser rejeitadas.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
