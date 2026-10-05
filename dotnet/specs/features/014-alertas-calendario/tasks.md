# Tasks 014 — Alertas de calendário

Regra: cada tarefa termina com testes verdes e um commit; testes primeiro, código depois. Pré-requisito: spec aprovada por delegação (só paridade e desvios 003/004/005/008/024; 120 a 122 propostos e **não** implementados). Sem edição de `Program.cs`, `PostgresFixture`, `DependencyInjection.cs` nem `Directory.*.props`. Comandos: `dotnet build dotnet/MeuSecretario.slnx` e `dotnet test dotnet/MeuSecretario.slnx`. Sem migration.

- [x] **T0 — Spec, plan, tasks e desvios 120 a 122** (estado `proposto`) e linhas no fim de `parity-deviations/README.md`.

## Domain

- [x] **T1 — Regras.** `CalendarAlertConfig`, `CalendarAlertRules` (minutos 1..60 inteiros JSON, truthiness de `ativo`, `ParseActive`, mensagem de sucesso) (AC-014-24 a 28).
- [x] **T2 — Janela e mensagem.** `AlertWindow`, `AlertMessage`, `UpcomingEvent` (AC-014-40, 45).

## Application

- [x] **T3 — Porta e casos de uso das rotas.** `ICalendarAlertConfigRepository`, `GetCalendarAlertConfig`, `UpdateCalendarAlertConfig`, `ActivateCalendarAlerts`, `DeactivateCalendarAlerts` com repositório e Google falsos (AC-014-10 a 12, 20 a 33).
- [x] **T4 — `CalendarAlertService`.** Busca de eventos, filtro, claim fail-open, envio, `ProcessAlertsForUser` com `ICalendarClient`, `InMemoryCache`, bot e relógio falsos (AC-014-40 a 46).

## Infrastructure

- [x] **T5 — `CalendarAlertConfigRepository`.** Testes de integração primeiro (SQL real): `get_or_create` idempotente e concorrente, `UPDATE` parcial com `updated_at`, usuários ativos (AC-014-47, 50).

## Api

- [x] **T6 — Respostas, mensagens e log.** `CalendarAlertsResponses`, `CalendarAlertsMessages`, `CalendarAlertsLog` (14010–14011), 500 fixo (AC-014-12, 29, 33).
- [x] **T7 — Endpoints.** `CalendarAlertsModule` com os 4 caminhos; 403, 404, 405, 401; integração por rota com corpo exato (AC-014-01 a 33, 50).
- [x] **T8 — Claim com Redis real.** `CalendarAlertServiceRedisTests` com `RedisFixture` (AC-014-43).

## Fechamento

- [x] **T9 — Contrato e desvios.** Revisar cada resposta contra o Python lido; spec §10 preenchida; build sem warnings e testes verdes; relatório com os arquivos compartilhados tocados.
