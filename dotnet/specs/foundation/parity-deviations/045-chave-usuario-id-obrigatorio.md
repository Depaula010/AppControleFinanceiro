# Desvio 045 — API keys: usuario_id obrigatório em PUT/DELETE /chave/<id>

- **Estado:** implementado (feature 008, 2026-10-02)
- **Features afetadas:** 008
- **Severidade:** segurança

## Comportamento atual (Flask)
`PUT/DELETE /api-keys/chave/<id>` não filtram por usuário (IDOR) e o caminho nem tem usuário; o desvio 006 aprovado exige "validar o dono".

## Comportamento proposto (.NET)
`usuario_id` obrigatório (corpo no PUT, query no DELETE); 400 se faltar; a chave só é alterada ou desativada se pertencer a esse usuário (senão 404).

## Impacto nos clientes
Quebra qualquer script que chame sem `usuario_id` (nenhum conhecido).

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
