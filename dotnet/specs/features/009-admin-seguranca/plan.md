# Plan 009 — Admin e segurança

- **Spec:** `spec.md` (em revisão)
- **Migrations:** nenhuma (faixa 0080–0089 reservada, não usada).
- **Regra de arquivos compartilhados:** esta é a **única** feature autorizada a editar `Program.cs`. Não edita `DependencyInjection.cs`, `AppSettings.cs`, `ApiKeyUnauthorized` (008-0) nem `RequireApiKeyFilter`. Configuração nova lê `IConfiguration` por `IOptions` (não toca `AppSettings`).

## 0. Decisões de desenho

- O filtro é um **middleware** posicionado depois do `UseRouting` implícito do `WebApplication` (o `HttpContext.GetEndpoint()` já está resolvido) e antes do `StrictRoutingMiddleware`. A "whitelist derivada do `EndpointDataSource`" é, na prática, `GetEndpoint() is not null`: o `EndpointDataSource` é a fonte de onde o roteador tirou o endpoint, sem manter lista paralela para dessincronizar (spec §5.3).
- O estado de segurança vive no Redis atrás de **uma porta** (`ISecurityStore`), implementada sobre `ICache` (`SetJsonAsync<string>` com o JSON interno já serializado produz a codificação dupla do Python; AC-010-12). O filtro e as rotas admin usam a mesma porta, então a regra de bloqueio existe em um lugar.
- Datas do módulo de segurança: `IClock.Now` (São Paulo) formatado por `PythonDateFormat.IsoLocal` (naive, microssegundos como o `isoformat`).
- Cada sub-entrega registra o que é seu no `AddServices` do próprio módulo; ninguém depende da ordem de descoberta dos módulos (`FeatureModules.Discover` ordena por nome completo).

## 1. Domain (`MeuSecretario.Domain/Security`)

Funções puras, sem I/O, com teste unitário.

| Tipo | Assinatura | Papel |
|---|---|---|
| `SuspiciousRules` | `static IReadOnlyList<string> UrlPatterns` (30, ordem do Flask); `static IReadOnlyList<string> UserAgentPatterns` (10); `static SuspicionResult? Evaluate(string path, string? userAgent, bool isRegisteredRoute)` | RN-009-04: UA sempre; padrões de URL só se `!isRegisteredRoute`; sem padrão ⇒ `Unknown endpoint: <path>` (só se `!isRegisteredRoute`). Regex pré-compilada, `IgnoreCase \| CultureInvariant`, timeout de 100 ms por casamento |
| `SuspicionResult` | `record(string Reason)` | razão exata para log e stats |
| `IpClassifier` | `static bool IsTrusted(IPAddress ip)` (loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, `fc00::/7`, `fe80::/10`, `::1`); `static string Normalize(IPAddress ip)` (`MapToIPv4()` se IPv4-mapeado; `ToString()`); `static bool TryParseInput(string? text, out string normalized)` (P-009-2) | RN-009-01 |
| `AttemptWindow` | `static IReadOnlyList<Attempt> Prune(IReadOnlyList<Attempt> list, DateTime now)` (descarta > 10 min); `const int BlockThreshold = 5`; `const int WindowMinutes = 10`; `const int BlockMinutes = 60` | RN-009-05 |
| `SecurityKeys` | `Blocked(ip)`, `Attempts(ip)`, `Blacklist(ip)`; `const` dos prefixos e TTLs (3600, 600, 31 536 000) | RN-009-09 |
| `PyTimedelta` | `static string Format(TimeSpan)` ⇒ `H:MM:SS` (`str(timedelta)` sem microssegundos) | §4.2 |
| `SecurityLogText` | `static string Sanitize(string?)` (controle ⇒ `?`) | §9 |
| `CacheClearPlan` | `static CacheClearPlan From(object? usuarioId, string? pattern)` ⇒ `User(id, pattern?) \| Pattern(p) \| All` | RN-009-16 |
| `AdminTexts` | `const` dos 500 por rota, 401, textos de setup, mensagens de 400/404/500 | spec §4 |

## 2. Application

### 2.1 `Application/Security`

```csharp
public interface ISecurityStore
{
    Task<bool> IsAvailableAsync(CancellationToken ct);
    Task<bool> IsBlacklistedAsync(string ip, CancellationToken ct);
    Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken ct);       // apaga a chave se corrompida
    Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken ct); // Tracked(n) | Blocked(count) | RedisUnavailable
    Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken ct);          // false = Redis indisponível
    Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken ct);                              // false = Redis indisponível
    Task<bool> RemoveTempBlockAsync(string ip, CancellationToken ct);                                  // apaga blocked e attempts
    Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken ct);                             // RedisAvailable=false ⇒ listas vazias
}
```
`RedisSecurityStore` (Infrastructure/Security) usa `ICache` (`GetJsonAsync<string>`, `SetJsonAsync<string>`, `DeleteAsync`, `KeysByPatternAsync`, `IsAvailableAsync`). Serializa o valor interno com `JsonSerializer` compacto (aceita `", "`/`": "` na leitura). Não propaga exceção (fail-open): o `ICache` já não lança; JSON inválido ⇒ trata como ausente/corrompido conforme a regra.

### 2.2 `Application/Admin`

Casos de uso (um por classe, `ExecuteAsync`, resultado tipado, sem HTTP):
- 009a: `UnblockIp`, `GetSecurityStats`, `BlacklistIp`, `RemoveBlacklistedIp`, `ClearLlmCache`, `GetLlmCacheStats`, `ClearBotSession`, `GetNotificationConfig`, `SetFinancialAlerts`.
- 009b: `GetOAuthConfigCheck`, `RunCalendarDebug` (devolve linhas de texto já sem segredo).
- 009c: `RunScheduleMotor`, `TriggerBillsNotifications`, `TriggerDailyBriefing`, `TriggerMonthlyReports`, `SendTestNotification`, `SendTestMonthlyReport`, `SendTestDailyBriefing`.
A validação na ordem da spec fica no caso de uso; o texto em português só em `AdminMessages` (Api), como `AuthMessages`.

Portas novas (`Application/Abstractions`), todas com **implementação padrão `Unavailable*`** registrada por `TryAddScoped` na 009c (a 013/012 substitui por `Replace` ou registra antes):

```csharp
public interface IScheduleMotor { Task<MotorRunResult> RunAsync(CancellationToken ct); }            // 013: lock job_lock:motor-agendamentos + idempotência
public enum MotorRunResult { Executed, AlreadyRunning }
public interface IBillsNotificationJob { Task<BillsJobResult> RunAsync(CancellationToken ct); }      // (UsuariosProcessados, NotificacoesEnviadas, Erros, HoraProcessamento)
public interface IDailyBriefingJob
{
    Task<DailyBriefingRunResult> RunForCurrentMinuteAsync(CancellationToken ct);                      // trigger-daily-briefing
    Task<DailyBriefingTestResult> SendTestAsync(int userId, CancellationToken ct);                    // test-daily-briefing
}
public interface IMonthlyReportJob
{
    Task<MonthlyReportRunResult> RunAsync(MonthlyMoment moment, CancellationToken ct);                // trigger-monthly-reports-*
    Task<ManualReportResult> SendManualAsync(int userId, MonthlyMoment moment, CancellationToken ct); // test-monthly-report
}
public enum MonthlyMoment { InicioMes, FimMes }
```
`UnavailableX` lança `FeatureNotAvailableException`; o endpoint o converte em 503 `Funcionalidade ainda não disponível` (AC-009-39). Sem isso, as rotas da 009c ficariam inalcançáveis (404 + tentativa contada) até a 013.
`SendTestNotification` (rota 31) usa só portas existentes: `IGoogleAccountService`, `ICalendarClient`, `IBotClient`, `IClock` e o repositório abaixo; formatação de moeda pt-BR por `CurrencyFormatter` (a 012 expõe o mesmo; se ainda não existir, `Domain/Common/PtBrMoney` mínimo).

Repositórios (portas em `Application/Admin`):
- `IBotSessionRepository.ClearAsync(CancellationToken)` ⇒ linhas apagadas.
- `IAdminNotificationConfigRepository`: `GetOrCreateAsync(userId)` ⇒ `NotificationConfigRow? (null = usuário inexistente)`; `SetFinancialAlertsAsync(userId, bool)` ⇒ `bool?` (valor gravado; `null` = usuário inexistente).
- `IAdminTestDataRepository` (009c): `FindUserAsync(userId)` ⇒ `(Nome, NumeroWhatsapp)?`; `ListTomorrowBillsAsync(userId, dayOfMonth, limit)`.

## 3. Infrastructure (Dapper; sem `AppDbContext`)

`Infrastructure/Security`: `RedisSecurityStore` (§2.1), `SecurityOptions` (§4). `Infrastructure/Admin`: `BotSessionRepository`, `AdminNotificationConfigRepository`, `AdminTestDataRepository`. `IDbConnectionFactory`, SQL parametrizado, nomes minúsculos sem aspas. `PostgresException` com `SqlState 23503` ⇒ usuário inexistente (P-009-4).

```sql
-- BotSessionRepository
DELETE FROM baileys_auth WHERE session_id = 'baileys_session';          -- ExecuteAsync devolve as linhas afetadas

-- AdminNotificationConfigRepository.GetOrCreate (uma ida ao banco, sem corrida; 23503 ⇒ usuário inexistente)
INSERT INTO notificationconfigs (usuario_id) VALUES (@userId) ON CONFLICT (usuario_id) DO NOTHING;
SELECT resumo_matinal_ativo, resumo_matinal_hora::text AS resumo_matinal_hora, alertas_financeiros_ativos
FROM notificationconfigs WHERE usuario_id = @userId;

-- AdminNotificationConfigRepository.SetFinancialAlerts (depois do GetOrCreate)
UPDATE notificationconfigs SET alertas_financeiros_ativos = @ativo, updated_at = now()
WHERE usuario_id = @userId RETURNING alertas_financeiros_ativos;

-- AdminTestDataRepository (009c)
SELECT nome, numero_whatsapp FROM usuarios WHERE id = @userId;
SELECT descricao, valor_previsto FROM agendamentos
WHERE usuario_id = @userId AND ativo = TRUE AND dia_execucao = @day LIMIT 5;
```
`resumo_matinal_hora::text` ("07:00:00") é formatado para `HH:mm` em código (`[..5]`), como o `strftime('%H:%M')`. O `INSERT ... ON CONFLICT DO NOTHING` evita a corrida do get-or-create do Flask (leitura, depois `INSERT` simples que falharia por `UNIQUE` em chamadas simultâneas).

`ILlmCacheAdmin` (010c) já existe: `InvalidateUserAsync(int, string?)`, `InvalidatePatternAsync`, `GetMetrics()`.

## 4. Configuração nova (`IOptions<SecurityOptions>`, lida de `IConfiguration`, nomes do Flask)

| Chave | Padrão | Uso |
|---|---|---|
| `BOT_PROTECTION_ENABLED` | `true` | liga o `SecurityFilterMiddleware` |
| `SECURITY_HEADERS_ENABLED` | `true` | liga `SecurityHeadersMiddleware` |
| `SECURITY_LOG_FILE` | `<LogDirectory>/security.log` | arquivo do sub-logger |
| `SECURITY_LOG_LEVEL` | `WARNING` | nível mínimo do sub-logger |
| `RATE_LIMIT_ENABLED` | `false` (P-009-5) | liga o limitador |
| `RATE_LIMIT_PER_HOUR` | `300` | balde geral por IP |
| `RATE_LIMIT_ADMIN_PER_HOUR` | `60` | balde de `/admin/*` |

As três variáveis mortas do Flask (`AUTO_BLOCK_ENABLED`, `BLOCK_DURATION_MINUTES`, `MAX_SUSPICIOUS_ATTEMPTS`) **não** são lidas. Variáveis novas entram nos três lugares (heredoc do `.env` do workflow, `environment` do compose e, se secretas, secrets do GitHub); nenhuma destas é secreta. Documentar em `infra-deploy.md` na T-final.

## 5. Api

### 5.1 `Api/Security/` (009-0)

- `SecurityModule : IFeatureModule`: `AddServices` registra `ISecurityStore` (singleton), `SecurityOptions`, os middlewares, o `ForwardedHeadersOptions` e o rate limiter; `MapEndpoints` vazio.
- `SecurityFilterMiddleware(RequestDelegate next, ISecurityStore store, IClock clock, IOptions<SecurityOptions> options, ILogger<SecurityFilterMiddleware> logger)`: implementa spec §5.2 (bypass, blacklist, bloqueio, suspeito). Usa `ClientIp.Resolve(HttpContext)` que devolve `(string Ip, bool Internal)`; `Internal` = origem confiável **e** sem `X-Forwarded-For`/`X-Original-For`. Respostas por `Envelopes.D.Error(...)` (envelope D). Falha do store ⇒ segue (fail-open) e loga.
- `UnmatchedRouteMiddleware`: `if (context.GetEndpoint() is null) return FlaskNotFound.WriteAsync(context);` senão `next`.
- `SecurityHeadersMiddleware`: `OnStarting` acrescenta os headers do spec §7 (conjunto final fixado na T6).
- `RateLimiting`: `services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.OnRejected = JSON D + Retry-After; o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext,string>(ctx => ...) })`; isenções: `GET /` e `GET /api/health`, e `ClientIp.Internal`; partição `admin` quando o caminho começa por `/admin/`. Desligado por `RATE_LIMIT_ENABLED=false` (o `GlobalLimiter` devolve `NoLimiter`).
- `SecurityLog` (`[LoggerMessage]`, EventIds 9001+, categoria fixa `MeuSecretario.Security`): um método por mensagem do spec; todos os parâmetros passam por `SecurityLogText.Sanitize`.

### 5.2 `Api/Admin/` (009a, 009b, 009c)

- `AdminModule : IFeatureModule`: `AddServices` registra casos de uso, repositórios e (009c) as portas `Unavailable*` por `TryAdd`; `MapEndpoints` mapeia as 33 rotas num `MapGroup("/admin")` com `.RequireApiKey(AdminUnauthorized.EnvelopeB)` no grupo inteiro (**todas** exigem a chave). `<int:usuario_id>` como `{usuarioId}` + `NumericIdFilter` da 002 **antes** do `RequireApiKey` (404 HTML antes do 401; `int.MaxValue` ⇒ `FlaskNotFound`), como `AccountsModule`.
- `AdminUnauthorized.EnvelopeB(HttpContext)`: `Envelopes.B.Error("Não autorizado", 401)`; com P-009-7 aprovada, antes resolve `ISecurityStore`/`IClock` do `RequestServices` e chama `TrackAttemptAsync(ip, "Invalid API key")` **se** `!ClientIp.Resolve(ctx).Internal` e `BOT_PROTECTION_ENABLED`.
- Corpo: `JsonBody.ReadObjectAsync` (024); `GetText`; `GetRaw` para `usuario_id`; `ativo` booleano por `JsonElement` local (`AdminBody.GetBool`, devolve `null` se ausente/nulo/tipo errado). Corpo vazio vs inválido em `gemini-cache-clear`: `AdminBody.ReadOptionalObjectAsync` (devolve `Empty` se `ContentLength == 0`/sem bytes).
- Envelope B em tudo: `Envelopes.B.Success(mensagem, extras)` com as chaves na ordem da spec; `Envelopes.B.Error`. Texto/HTML: `Results.Content(text, "text/html; charset=utf-8")`. Os 500 usam `AdminMessages.<Rota>Failed` (texto fixo).
- `AdminLog` (`[LoggerMessage]`, EventIds 9101+, `ILogger<AdminModule>`): só `Error` (`SafeError.Describe`) e ids numéricos; mensagens `[ADMIN-FIX]` e `[MOTOR]` do Flask preservadas onde a spec cita.
- `SetupRoutes`: tabela `(método, caminho, formato)` das 13 rotas no-op; um único handler parametrizado devolve a mensagem fixa; sem acesso a banco (teste de arquitetura impede injeção de `IDbConnectionFactory` nesse handler: o handler só recebe `IResult`-factory).

## 6. Edição de `Program.cs` (exata)

Estado atual relevante (`Program.cs`): `UseSerilog(...)` com `WriteTo.Console` e arquivo condicional; depois de `--migrate`, `app.UseMiddleware<StrictRoutingMiddleware>();` seguido do laço de `MapEndpoints`.

**Edição 1 — `security.log` (dentro do lambda de `UseSerilog`, depois do bloco `if (settings is { LogToFile: true })`):**
```csharp
    // 009: security.log (mesmo formato do Flask) só com a categoria de segurança.
    var security = services.GetService<IOptions<SecurityOptions>>()?.Value;
    if (security is not null)
    {
        logger.WriteTo.Logger(sub => sub
            .MinimumLevel.Is(security.LogLevel)
            .Filter.ByIncludingOnly(Matching.FromSource(SecurityLog.Category))
            .WriteTo.File(
                security.LogFile ?? Path.Combine(settings?.LogDirectory ?? "logs", "security.log"),
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Level:u}: {Message:lj}{NewLine}",
                formatProvider: CultureInfo.InvariantCulture));
    }
```
(`using Serilog.Filters;`, `using Microsoft.Extensions.Options;`, `using MeuSecretario.Api.Security;`.) O console já recebe tudo.

**Edição 2 — pipeline (substitui a linha `app.UseMiddleware<StrictRoutingMiddleware>();`):**
```csharp
// 009: IP real, rate limit, filtro de segurança, 404 do Flask. A ordem importa.
app.UseForwardedHeaders();                              // 1. IP do cliente (desvio 023)
app.UseMiddleware<SecurityHeadersMiddleware>();         // 2. headers em todas as respostas, inclusive 403/404 do filtro
app.UseRateLimiter();                                   // 3. antes do filtro, como o Limiter do Flask
app.UseMiddleware<SecurityFilterMiddleware>();          // 4. blacklist, bloqueio, suspeito (usa GetEndpoint())
app.UseMiddleware<StrictRoutingMiddleware>();           // 5. (já existia) barra final ⇒ 404
app.UseMiddleware<UnmatchedRouteMiddleware>();          // 6. sem endpoint ⇒ 404 HTML do Flask
```
O `WebApplication` já insere o `UseRouting` no início da pipeline (e o `UseEndpoints` no fim) quando há endpoints mapeados; por isso `GetEndpoint()` está preenchido a partir da linha 4. **Teste de regressão obrigatório (T3):** um endpoint de teste confirma que o filtro enxerga o endpoint antes de `MapEndpoints` (a ordem das chamadas no arquivo não altera onde o roteamento roda).
**Edição 3:** `using MeuSecretario.Api.Security;`. Nenhuma outra linha de `Program.cs` muda; `AddInfrastructure` e a descoberta de módulos permanecem.

## 7. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário | `Domain/Security/SuspiciousRulesTests` | os 30 padrões e os 10 UAs com um caminho exemplo cada; registrada × não registrada; razões exatas; `Unknown endpoint` (AC-009-07, 08) |
| Unitário | `Domain/Security/IpClassifierTests`, `AttemptWindowTests`, `PyTimedeltaTests`, `SecurityLogTextTests`, `CacheClearPlanTests` | AC-009-06, 14, 17, 25 |
| Unitário | `Application/Security/*` com `ISecurityStore` fake | ordem 403/403/404/403-na-5ª (AC-009-01 a 04) |
| Unitário | `Application/Admin/*UseCaseTests` (NSubstitute, `FakeTimeProvider`) | validação na ordem da spec, resultados tipados (AC-009-22 a 29, 34 a 38) |
| Integração (Redis, `RedisFixture`) | `Security/RedisSecurityStoreTests` | codificação dupla (AC-009-12), leitura de fixture do Python (AC-009-13), TTLs, JSON corrompido, `SCAN`, sem Redis (AC-009-10, 11) |
| Integração (`WebApplicationFactory` + Redis + Postgres) | `Security/SecurityFilterEndpointsTests` | filtro de ponta a ponta: IP via `X-Forwarded-For` (o `RemoteIpAddress` do `TestServer` é definido por um `IStartupFilter` de teste), bypass interno, 5 tentativas, blacklist, rota registrada com trecho suspeito, `/api-keys` com flag (AC-009-01 a 09, 15, 16, 20) |
| Integração | `Security/ForwardedHeadersTests` | `X-Forwarded-For: forjado, real` com peer privado resolve o real; peer público ignora o header (AC-009-06) |
| Integração | `Security/RateLimitTests` | AC-009-18 (limite 3, dois IPs, isentos, 429 JSON + `Retry-After`) |
| Integração | `Security/SecurityHeadersAndLogTests` | AC-009-17, 19; `security.log` num diretório temporário com o formato `[yyyy-MM-dd HH:mm:ss] WARNING: ...` |
| Integração (Postgres) | `Admin/AdminAuthTests` | AC-009-21 nas 33 rotas (teoria por tabela de rotas), corpo não lido, id `abc`/`-1`/`int.MaxValue+1` ⇒ 404 antes da auth |
| Integração | `Admin/SecurityAdminEndpointsTests` (Redis) | AC-009-22 a 24 |
| Integração | `Admin/CacheAdminEndpointsTests` (Redis + `LlmCacheAdmin` real) | AC-009-25, 26 |
| Integração | `Admin/BotSessionAndNotificationConfigTests` (Postgres) | AC-009-27 a 29 (`baileys_auth` semeada, linha criada, FK) |
| Integração | `Admin/SetupAndDiagnosticsTests` | AC-009-31 a 33 (spy de `IDbConnectionFactory` prova nenhum acesso; valores sentinela de segredo ausentes do corpo) |
| Integração | `Admin/DependentRoutesTests` (portas fake) | AC-009-34 a 39 |
| Integração | `Admin/ErrorLeakTests` | AC-009-30 (falha injetada com mensagem sentinela ausente do corpo e do log) |
| Arquitetura | NetArchTest existente | Application sem Npgsql/Serilog; handlers de setup sem `IDbConnectionFactory`; Api sem Infrastructure.Dapper |
| Contrato | corpos exatos nos testes de integração (como na 001/008) | spec §4 |

Fixtures: `RedisFixture` e `PostgresFixture` existentes. `PostgresFixture.ResetAsync` precisa truncar `baileys_auth` (arquivo de teste da feature semeia/limpa; não altera o fixture compartilhado). Fixture do Python para o valor de blacklist: `tests/Fixtures/security-redis-fixture.json` com duas linhas (valor bruto Redis com aspas externas e o JSON interno com `", "`), gerado uma vez por script (`json.dumps(json.dumps(...))`) e versionado; CI sem Python. IPs de teste nos blocos `192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24` (documentação).

## 8. Riscos e decisões

- **Ordem do pipeline.** O erro mais provável é o filtro rodar antes do roteamento (`GetEndpoint()` nulo ⇒ tudo "desconhecido" ⇒ bloqueio de todo mundo). Mitigação: teste T3 (o filtro enxerga o endpoint) e um teste de fumaça: `GET /` por IP externo exige 200 e nenhuma tentativa registrada.
- **Auto-bloqueio do operador.** P-009-7 pode bloquear o operador que erra a chave; `security-unblock-ip` precisa da chave (círculo): o desbloqueio de emergência é `redis-cli DEL security:blocked:<ip>` ou o bypass interno (chamada de dentro do container, sem `X-Forwarded-For`). Documentar no `cutover-runbook.md`.
- **Bypass interno amplo.** Qualquer processo com acesso à rede `rede-global` (peer privado, sem `X-Forwarded-For`) escapa do filtro, igual ao Flask. Um atacante dentro da rede não é o modelo de ameaça; registrado.
- **Rota derivada abre superfície.** `setup-*` e `trigger-*` ficam alcançáveis de fora (rotas fora da whitelist fixa do Flask, como `setup-reserva-emergencia` e `setup-budget-violations`, estavam bloqueadas para IP externo); a defesa passa a ser só `x-api-key` (mesmo segredo do JWT; desvio 006). Mitigações: no-op dos setups, P-009-7, e restringir `/admin/` no nginx nativo (recomendado no runbook; fora do código).
- **Concorrência das tentativas** (§5.5 da spec): leitura-modifica-grava sem atomicidade. Aceito por paridade; melhoria possível com script Lua (fora desta feature).
- **Relógio.** `blocked_until` em São Paulo naive; num rollback para o Flask (UTC naive) um bloqueio gravado pelo .NET expira 3 h "mais cedo" para o Python (inofensivo; o TTL do Redis continua mandando).
- **Rate limit em memória.** Reinício zera; com mais de uma instância os baldes se multiplicam. Hoje há uma instância.
- **`UseForwardedHeaders` e HTTPS.** `X-Forwarded-Proto` altera `Request.Scheme`; o app não redireciona para HTTPS (`force_https=False`), então não há efeito.
- **Dependência da 013/012.** A 009c entrega rotas + portas + `Unavailable*`; a 013/012 precisam adotar exatamente estas assinaturas (ou propor mudança reabrindo a spec).
