# Desvio 054 — Clima: tradução em português e chance de chuva

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** correção

## Comportamento atual (Flask)
O mapa de tradução tem chaves em inglês (`weather_service.py:18-54`) mas a chamada usa `lang=pt` (`:119`): nunca casa, o emoji é sempre o termômetro; `chance_chuva` é sempre `None` (`:147`) e `get_forecast` não tem chamador, então o resumo matinal nunca mostra chuva.

## Comportamento proposto (.NET)
Casar pelo texto em português da API (e por `condition.code`) para o emoji. A chamada de `forecast.json` para a chance de chuva **fica de fora por ora** (consome cota da WeatherAPI): só entra se a cota permitir.

## Impacto nos clientes
O resumo matinal muda o emoji do tempo.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar só a tradução.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
