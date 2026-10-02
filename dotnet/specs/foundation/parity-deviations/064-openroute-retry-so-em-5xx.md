# Desvio 064 — OpenRoute: repetir só em timeout, rede e 5xx

- **Estado:** aprovado
- **Features afetadas:** 010
- **Severidade:** correção

## Comportamento atual (Flask)
`raise_for_status` faz 400/401/403/404/429 virarem nova tentativa (`travel_time_service.py:75-76, 107-113`), gastando 2 requisições da cota diária por falha.

## Comportamento proposto (.NET)
Repetir só em timeout, erro de rede e 5xx.

## Impacto nos clientes
Chave inválida ou endereço malformado falham com 1 requisição.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
