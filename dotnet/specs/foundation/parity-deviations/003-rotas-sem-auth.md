# Desvio 003 — Rotas de escrita sem autenticação

- **Estado:** aprovado; implementado no escopo da feature 009 (009a: `clear-bot-session`, `config-alertas-financeiros`, `get-notification-config`); 011 (`reprocessar` exige `x-api-key`, 2026-10-05); pendente na feature 014
- **Features afetadas:** 009, 011, 014
- **Severidade:** segurança

## Comportamento atual (Flask)
Sem nenhuma autenticação: `/calendar-alerts/*`, `POST /disconnect-calendar/<id>`, `POST /api/fatura/<id>/reprocessar`, `POST /admin/clear-bot-session` (apaga a sessão do bot), `POST /admin/config-alertas-financeiros`, `GET /admin/get-notification-config/<id>`. O usuário vem do caminho.

## Comportamento proposto (.NET)
`x-api-key` obrigatório em `clear-bot-session`, `config-alertas-financeiros`, `get-notification-config`, `reprocessar` e `disconnect-calendar`. `/calendar-alerts/*` passa a exigir JWT e usa o `user_id` do token (o `<id>` do caminho deve coincidir, senão 403).

## Impacto nos clientes
**Verificar quem chama:** o Angular usa `/calendar-alerts/*` ou `/disconnect-calendar`? O bot usa `get-notification-config`/`clear-bot-session`? Se sim, ajustar o cliente ou manter a rota aberta para esse cliente.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar após mapear os chamadores no Angular e no bot.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** antes de implementar, mapear quem chama cada rota (Angular, bot, scripts); se algum cliente legítimo depender da rota aberta, ele é ajustado ou a rota fica aberta só para ele.
