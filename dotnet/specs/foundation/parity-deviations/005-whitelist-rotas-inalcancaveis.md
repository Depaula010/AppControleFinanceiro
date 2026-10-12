# Desvio 005 — Rotas registradas porém bloqueadas pela whitelist

- **Estado:** implementado (feature 009-0, 2026-10-04) — escopo 009-0: whitelist derivada de `GetEndpoint()` e padrões suspeitos só para caminho sem endpoint; escopo 009a: as 9 rotas `/admin/*` com `x-api-key` (incl. `/admin/config-alertas-financeiros`, que casava com `/config`) alcançam o handler; 009b/c ainda pendentes
- **Features afetadas:** 008, 009, 011, 014
- **Severidade:** compatibilidade

## Comportamento atual (Flask)
O filtro de segurança devolve 404 e conta tentativa para rotas registradas fora de `VALID_ENDPOINTS`: `/api-keys/usuario/*`, `/api-keys/chave/*`, `/api-keys/uso/*`, `/api-keys/logs/*`, `/api-keys/lgpd/*`, `/api-keys/health`, `/calendar-alerts/ativar/*`, `/calendar-alerts/desativar/*`, `/admin/setup-reserva-emergencia`, `/admin/setup-budget-violations`, `/api/agendamento/<id>/reserva`, `/api/agendamentos/reserva`. `/admin/config-alertas-financeiros` e `/calendar-alerts/config/<id>` estão na whitelist mas casam com o padrão suspeito `/config` (testado antes). Para IP público (ou privado com `X-Forwarded-For`) nada disso funciona; só chamadas internas sem o header passam. Na whitelist sem rota: `/webhook-google-calendar`, `/api-keys/validate`, `/settings/`, `/addresses/`.

## Comportamento proposto (.NET)
A whitelist é derivada das rotas registradas (todas acessíveis). Padrões suspeitos passam a valer apenas para rotas **não** registradas. As rotas continuam protegidas por autenticação (desvios 003/006), não pelo 404.

## Impacto nos clientes
Rotas que antes davam 404 externo passam a existir de fora. Confirmar com o Rafael se alguma deve continuar apenas interna.

## Alternativas
Manter como está (paridade estrita).

## Recomendação
Aprovar derivação automática. Decidir se `/api-keys/*` fica exposto externamente.

## Decisão do Rafael
Aprovado em 2026-10-02 (aprovação em bloco de todos os desvios propostos). **Condição:** confirmar quais rotas de `/api-keys/*` e `/admin/*` devem ser alcançáveis de fora; as demais exigem autenticação.
