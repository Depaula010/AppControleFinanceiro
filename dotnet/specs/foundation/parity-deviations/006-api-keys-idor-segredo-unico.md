# Desvio 006 — IDOR em /api-keys e segredo único para JWT e admin

- **Estado:** aprovado
- **Features afetadas:** 001, 008, 009
- **Severidade:** segurança

## Comportamento atual (Flask)
`/api-keys/*` usa `X-API-KEY` global e o `usuario_id` do caminho: quem tem a chave global lê e altera qualquer usuário; `/api-keys/lgpd/exportar/<id>` devolve chaves **decifradas**. `PUT/DELETE /api-keys/chave/<id>` não filtram por usuário. JWT e `x-api-key` de admin usam a mesma `API_SECRET_KEY`: quem conhece a chave de admin forja tokens.

## Comportamento proposto (.NET)
Manter `x-api-key` para `/api-keys/*` (cliente servidor-a-servidor), mas `chave/<id>` valida o dono. Separar a chave de assinatura do JWT em `JWT_SIGNING_KEY` **somente se** o Rafael aceitar invalidar os tokens existentes na virada; caso contrário manter a mesma chave e registrar o risco.

## Impacto nos clientes
Trocar a chave de assinatura desloga todos os usuários uma vez.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar a checagem de dono. Adiar a separação de chaves para depois da virada.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** só a checagem de dono é aplicada agora; a separação da chave do JWT fica para depois da virada.
