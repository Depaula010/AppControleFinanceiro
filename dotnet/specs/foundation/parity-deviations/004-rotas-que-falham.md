# Desvio 004 — Rotas que hoje respondem 500

- **Estado:** aprovado
- **Features afetadas:** 009, 014
- **Severidade:** correção

## Comportamento atual (Flask)
`/calendar-alerts/*` chama `CalendarAlertConfigService.get_or_create_config`, método inexistente ⇒ 500 com a mensagem do `AttributeError`. `GET /admin/gemini-cache-stats` chama `get_cache_stats()`, mas o método se chama `get_metrics` (`gemini_cache_service.py:313`) ⇒ 500.

## Comportamento proposto (.NET)
Implementar o comportamento pretendido: `get_or_create` da config (insere com padrão `alertas_tarefas_ativo=false`, `minutos_antes=1`) e estatísticas de cache.

## Impacto nos clientes
Nenhum cliente funcional depende do 500.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
