# Desvio 052 — Rotas /api-keys/* desligadas por padrão em produção

- **Estado:** aprovado
- **Features afetadas:** 008, 009
- **Severidade:** segurança

## Comportamento atual (Flask)
Hoje só `/api-keys/preferencias/*` é alcançável de fora (as outras 15 rotas são bloqueadas pelo filtro de segurança) e ninguém conhecido as usa. O desvio 005 derivaria a whitelist das rotas registradas, expondo as 15 rotas, que leem e apagam dados de qualquer usuário atrás de um único segredo (o mesmo do JWT).

## Comportamento proposto (.NET)
As rotas `/api-keys/*` só são registradas se `API_KEYS_ROUTES_ENABLED=true` (padrão **false** em produção; os testes ligam). Com a variável desligada elas respondem 404, como hoje para IP público. Refina o desvio 005 só para este prefixo.

## Impacto nos clientes
Efeito prático igual ao de hoje; liga-se por variável quando houver um cliente legítimo.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar (opção B da decisão G-6).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
