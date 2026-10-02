# Desvio 022 — WeatherAPI por HTTP sem TLS

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** segurança

## Comportamento atual (Flask)
`http://api.weatherapi.com/v1/...` com a chave na query.

## Comportamento proposto (.NET)
HTTPS (suportado pelo provedor).

## Impacto nos clientes
Nenhum.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
