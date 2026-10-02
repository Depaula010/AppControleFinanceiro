# Desvio 002 — Rotas /admin/setup-* sem autenticação e com DDL via GET

- **Estado:** aprovado
- **Features afetadas:** 009
- **Severidade:** segurança

## Comportamento atual (Flask)
`GET /admin/setup-database` (DROP + CREATE do schema inteiro), `/populate-global-categories`, `/setup-user-data` (número de WhatsApp e dias do cartão fixos no código; devolve `user_api_key_para_automate`), `/setup-*` e `/cleanup-deprecated-notification-fields` não exigem `x-api-key` (`database_setup.py`, `feature_migrations.py`). `/admin/debug-calendar` imprime os 20 primeiros caracteres do access token do Google; `/admin/oauth-config-check` revela o prefixo do client_id.

## Comportamento proposto (.NET)
As rotas de setup deixam de executar DDL: o schema é gerido por migrations. Continuam registradas, exigem `x-api-key` e respondem 200 com texto informando "gerenciado por migrations" (para não quebrar scripts). `debug-calendar` e `oauth-config-check` exigem `x-api-key` e não devolvem fragmentos de segredo.

## Impacto nos clientes
Postman e scripts de setup deixam de recriar schema. Nenhum cliente de produção usa essas rotas.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar. Risco de manter: qualquer visitante recria o schema (a whitelist é hoje a única barreira).

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos).
