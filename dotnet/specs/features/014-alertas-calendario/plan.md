# Plan 014 — Alertas de calendário

- **Spec:** `spec.md` (aprovada por delegação; só paridade e desvios 003/004/005/008/024; 120 a 122 propostos e **não** implementados)

Reaproveita: `Envelopes.B`, `RequireUser()`/`ICurrentUser`, `FlaskNotFound`, `SafeError`, `IDbConnectionFactory`, `IClock`, `ICache`, `IBotClient`, `ICalendarClient`, `IGoogleAccountService`, `CacheKeys.CalendarAlert`, `RawBody`/`RawValue`/`PyTruth` (Domain, da 002/007), `SettingsBodyReader` (007; leitura tolerante do corpo, desvio 024), `PostgresFixture`/`AuthFixtures` e os fakes de teste da 010 (`FakeBot`, `NeutralCache`, `InMemoryCache`). **Nenhum arquivo compartilhado é editado**: sem `Program.cs`, sem `DependencyInjection.cs`, sem pacote novo, **sem migration**. O módulo se registra sozinho (`IFeatureModule`). Sem EF: Dapper.

## 1. Domain (`MeuSecretario.Domain/CalendarAlerts`)

| Tipo | Papel |
|---|---|
| `CalendarAlertConfig` | `record(bool Active, int MinutesBefore)` (a linha lida/gravada) |
| `CalendarAlertRules` | `ValidateMinutes(RawValue)` ⇒ ausente/nulo, `Valid(n)` ou `Invalid` (inteiro JSON 1..60); `IsActiveRequested(RawValue)` (truthiness do Python); `ParseActive(RawValue)` ⇒ ausente, `true`/`false` ou inválido; `SuccessMessage(bool? active, int? minutes)` |
| `AlertWindow` | `For(DateTimeOffset now, int minutesBefore)` ⇒ `(Start, End)` = alvo ± 30 s |
| `AlertMessage` | `Build(title, startText, location, minutesBefore)` e `FormatTime(startText)` (`HH:mm` do texto; cru se não for ISO) |
| `UpcomingEvent` | `record(Id, Summary, Start, Location, Description, CalendarName)` |

## 2. Application (`MeuSecretario.Application/CalendarAlerts`)

- `ICalendarAlertConfigRepository`: `GetOrCreateAsync(userId)`, `UpdateAsync(userId, bool? active, int? minutes)` (lança em erro do banco; o caso de uso traduz), `ListUsersWithActiveAlertsAsync()` ⇒ `ActiveAlertUser(UserId, WhatsappNumber, MinutesBefore)`.
- Casos de uso (um por rota, `ExecuteAsync`, sem HTTP): `GetCalendarAlertConfig`, `UpdateCalendarAlertConfig(userId, RawBody?)`, `ActivateCalendarAlerts(userId, RawBody?)`, `DeactivateCalendarAlerts(userId)`; resultados como união (`Ok(message, config)`, `Rejected(message)` com `Rejection` = `NoParameters | NotConnected | InvalidMinutes | UpdateFailed`). A consulta ao Google usa `IGoogleAccountService.IsConnectedAsync`.
- `CalendarAlertService(ICalendarClient, ICache, IBotClient, IClock, ILogger)`: `GetUpcomingEventsAsync`, `SendEventAlertAsync`, `ProcessAlertsForUserAsync` (RN-014-11 a 15). Logs só com `userId`, contagens e `SafeError`-like (tipo da exceção); **nunca** número de WhatsApp, título nem local. O Application não tem `ILogger`: o serviço e os casos de uso recebem um `ICalendarAlertDiagnostics` mínimo (implementado na Infrastructure sobre `ILogger`, EventIds 14001 a 14006), no padrão do `IGoogleDiagnostics` da 010.

## 3. Infrastructure (`MeuSecretario.Infrastructure/CalendarAlerts`)

`CalendarAlertConfigRepository(IDbConnectionFactory)` com Dapper: `INSERT ... ON CONFLICT (usuario_id) DO NOTHING` + `SELECT`; `UPDATE` com fragmentos fixos e `updated_at = CURRENT_TIMESTAMP`; consulta de usuários ativos (RN-014-10). Registro em `AddCalendarAlertsInfrastructure` (`TryAddSingleton`), chamado pelo módulo.

## 4. Api (`MeuSecretario.Api/CalendarAlerts`)

- `CalendarAlertsModule : IFeatureModule`: `AddServices` (repositório, 4 casos de uso, `CalendarAlertService` por fábrica, para a validação de dependências do host não exigir o bot antes da hora); `MapEndpoints`: `MapGet`/`MapPost` nos 4 caminhos com `{usuarioId:regex(^\d+$)}` e `.RequireUser()`; o id é conferido com `ICurrentUser` (404 se estourar `int`, 403 se diferente).
- `CalendarAlertsMessages` (textos literais), `CalendarAlertsResponses` (`Envelopes.B`, `config` em ordem), `CalendarAlertsLog` (EventIds **14010–14011**; só operação, `userId` e `SafeError.Describe`).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com o texto fixo da rota (desvio 008).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `CalendarAlertRulesTests`, `AlertWindowTests`, `AlertMessageTests` | AC-014-24 a 28, 40, 45 |
| Unitário (Application) | `CalendarAlertUseCasesTests` (repositório e Google falsos), `CalendarAlertServiceTests` (`ICalendarClient`, `InMemoryCache`, bot e relógio falsos) | AC-014-20 a 33, 40 a 46 |
| Integração | `CalendarAlertConfigRepositoryTests` | SQL real: `get_or_create` idempotente e concorrente, `UPDATE` parcial, `updated_at`, CHECK, usuários ativos (AC-014-47, 50) |
| Integração | `CalendarAlertsEndpointsTests` | cada linha da §3 com corpo exato; `RequireUser` real; Google conectado/desconectado por fake de `IGoogleAccountService`; isolamento A × B (AC-014-01 a 33, 50) |
| Integração | `CalendarAlertServiceRedisTests` | `RedisFixture` real: claim `SET NX`, TTL ≈ 7200 s, dois ciclos sem reenvio (AC-014-43) |
| Contrato (Fase 3) | goldens do Flask de homologação | impossível para as 4 rotas: o Flask responde 500 (desvio 004); vale o teste de integração por rota |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","014-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Riscos e decisões

- **Redis fail-open do claim:** `ICache.SetIfNotExistsAsync` devolve falso tanto para "já existe" quanto para "Redis fora"; o serviço consulta `IsAvailableAsync` antes, como o `is_connected()` do Python (RN-014-13).
- **`ListEventsQuery`:** `TimeMin`/`TimeMax` em `DateTimeOffset` com o deslocamento de São Paulo; o teste confere os instantes enviados ao fake de `ICalendarClient`.
- **Hora da mensagem:** vem do texto `start`, não do relógio nem de conversão de fuso (RN-014-14).
- **`is_user_connected` pode renovar token e avisar por WhatsApp** (010, RN-010-39): comportamento herdado, igual ao Flask.
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
