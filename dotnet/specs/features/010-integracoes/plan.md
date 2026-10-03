# Plan 010 — Integrações

- **Spec:** `spec.md` (em revisão; este plano só vale depois da aprovação)
- **Estado:** em revisão

## 0. Visão geral e regras de paralelismo

Seis trilhas (010-0, 010a, 010b, 010c, 010d com d1 e d2, 010e), cada uma em worktree e branch próprios. **Nenhuma trilha edita arquivo de outra.** Os pontos compartilhados (pacotes, `DependencyInjection.cs`, `AppSettings`, interfaces) são resolvidos **uma vez**, no PR **010-0**, antes de abrir o paralelo (regra do `roadmap.md`: arquivo compartilhado entra por PR separado).

```
010-0 ──┬─► 010a (Redis)
        ├─► 010b (Bot)        merge depois da 010a (só o teste com Redis real depende dela)
        ├─► 010c (Gemini)     precisa de 008-0 T3 (IApiKeyProvider)
        ├─► 010d-1 (OAuth)    precisa de 008-0 T2 (RequireApiKey) só na rota disconnect
        ├─► 010d-2 (Calendar/Drive)
        └─► 010e (Clima/rotas) precisa de 008-0 T3
```

Convenções de camada (arquitetura): Domain sem I/O; Application só referencia Domain (portas e casos de uso); Infrastructure implementa as portas e é o único lugar com `HttpClient`, Redis, Dapper e SDK do Google; Api só tem as rotas OAuth. Os SDKs do Google **não** vazam para Application.

## 1. 010-0 — Contratos e pontos compartilhados (PR único, sequencial)

### 1.1 Pacotes (`Directory.Packages.props` + `.csproj`)

| Pacote | Projeto | Por quê |
|---|---|---|
| `Microsoft.Extensions.Http.Resilience` | Infrastructure | já previsto em `tech-stack.md` ("HTTP externo"): timeouts e retry declarativos por cliente, testáveis com `TimeProvider` falso |
| `Google.Apis.Auth`, `Google.Apis.Calendar.v3`, `Google.Apis.Drive.v3` | Infrastructure | já previstos em `tech-stack.md` ("Google"): SDKs oficiais, escopos `calendar` e `drive.file` |
| `Testcontainers.Redis` | IntegrationTests | **novo**: Redis real nos testes do `ICache` (SET NX EX atômico, TTL, formato). Acrescentar a linha na tabela "Testes" de `tech-stack.md` com a justificativa |

Versões: a estável mais recente na data do PR (`dotnet package search` / `dotnet add package`), registradas no `Directory.Packages.props` (gerenciamento central). `StackExchange.Redis` já está referenciado em Infrastructure.

### 1.2 Interfaces e tipos (`Application/Abstractions`, um arquivo por tipo; sem implementação)

| Arquivo | Conteúdo |
|---|---|
| `ICache.cs` | a porta de spec §4.1 |
| `Caching/CacheKeys.cs` (Application) | construtores de chave (RN-010-07) |
| `IBotClient.cs` | `SendTextAsync`, `SendImageAsync` |
| `ILlmClient.cs` | `ILlmClient`, `LlmRequest`, `LlmCachePolicy`, exceções (`LlmQuotaExceededException`, `LlmPermissionDeniedException`, `LlmBlockedException`, `LlmKeyUnavailableException`, `LlmException`) |
| `ILlmCacheAdmin.cs` | `ILlmCacheAdmin`, `LlmCacheMetrics` (mesma forma do dicionário do Python) |
| `IGeminiTransport.cs` | `Task<string> GenerateAsync(string prompt, string apiKey, ct)` (a ponte HTTP; só a 010c implementa) |
| `IGoogleAccountService.cs` | `GetCredentialsAsync` → `GoogleCredentialResult { Status (Connected, NotConnected, NeedsReconnect, Unavailable), AccessToken?, Scopes }`; `IsConnectedAsync`; `HasDriveScopeAsync`; `RevokeAsync` |
| `IGoogleTokenRepository.cs`, `IGoogleOAuthClient.cs`, `IGoogleUserLookup.cs` | portas internas da 010d-1 (declaradas aqui para a d2 e os testes compilarem) |
| `ICalendarClient.cs` | operações de RN-010-47 a 49 + records (`CalendarInfo`, `CalendarEvent`, `NewCalendarEvent`, `EventUpdate`, `ListEventsQuery`) |
| `IDriveClient.cs` | `UploadFileAsync` + `DriveUploadResult` |
| `IWeatherClient.cs`, `IRouteClient.cs`, `ITravelRateLimiter.cs`, `IProviderKeyValidator.cs` | portas da 010e + records (`WeatherInfo`, `ForecastInfo`, `GeocodeResult`, `TravelResult`, `KeyValidation`) e exceções (`ProviderUnavailableException`, `ApiKeyUnavailableException`) |
| `IIntegrationSettings.cs` | leitura (somente propriedades) dos valores de `AppSettings` que o Application precisa: `GeminiApiKey`, `GeminiModel`, `ApiSecretKey`, `BotWhatsappUrl`, `BotApiKey`, `BotSessionName`, `BotSessionId`, `GoogleClientId`, `GoogleClientSecret`, `GoogleRedirectUri`, `WeatherApiKey`, `OpenRouteApiKey`, `OpenRouteBaseUrl`, `TravelTimeDailyLimit`. Implementação `AppSettingsIntegrationSettings` em Infrastructure (adaptador de uma linha por propriedade) |
| `IProviderKeySelector.cs` + `ProviderKeySelector.cs` (Application) | RN-010-66; usa `IApiKeyProvider` (008-0 T3) e `IIntegrationSettings`; `ProviderKeyResult { string? Key; ApiKeyFailureInfo? Failure }` |
| `Domain/Common/PhoneMask.cs` | `static string Last4(string? number)` ⇒ `…1072` (usado por todos os logs) |

`IApiKeyProvider` e `ApiKeyResolution` **não** são criados aqui: vêm do 008-0 T3. Se a 008-0 ainda não estiver em `main`, a 010-0 espera por ela (é a única dependência externa).

### 1.3 DI sem conflito entre agentes paralelos

- **Um método de extensão por trilha, cada um em arquivo próprio**, todos criados vazios (retornam `services`) na 010-0:

  | Arquivo | Método |
  |---|---|
  | `Infrastructure/Caching/CachingServiceCollectionExtensions.cs` | `AddRedisCache(this IServiceCollection)` |
  | `Infrastructure/Bot/BotServiceCollectionExtensions.cs` | `AddBotClient(...)` |
  | `Infrastructure/Llm/LlmServiceCollectionExtensions.cs` | `AddGeminiClient(...)` |
  | `Infrastructure/Google/GoogleServiceCollectionExtensions.cs` | `AddGoogleIntegration(...)` (fechado na 010-0; chama `AddGoogleOAuth` e `AddGoogleApiClients`, ver abaixo) |
  | `Infrastructure/External/ExternalServiceCollectionExtensions.cs` | `AddWeatherAndRoutes(...)` |

- **Uma única edição** em `DependencyInjection.AddInfrastructure`: registra `IIntegrationSettings` e `IProviderKeySelector` e chama os cinco métodos acima. Depois da 010-0 ninguém mais edita `DependencyInjection.cs`.
- **d1 e d2 no mesmo método** gera conflito de arquivo. Para evitar, a 010-0 cria dois métodos em arquivos separados e `AddGoogleIntegration` (também da 010-0, fechado) apenas os chama: `AddGoogleOAuth()` (`Infrastructure/Google/GoogleOAuthRegistration.cs`, d1) e `AddGoogleApiClients()` (`Infrastructure/Google/GoogleApiClientsRegistration.cs`, d2). O que as duas trilhas compartilham, a `Google.Apis.Http.IHttpClientFactory` própria (`Infrastructure/Google/GoogleHttpClientFactory.cs`, timeout de 60 s, substituível em teste), nasce na 010-0 e é registrada por `AddGoogleIntegration`.
- **Api**: rotas OAuth em `MeuSecretario.Api/Google/GoogleModule.cs`, `IFeatureModule` descoberto por reflexão (`Program.cs` não muda). Os módulos de feature rodam **depois** de `AddInfrastructure` (`Program.cs:36-45`); então a 008a, que registra a implementação real de `IApiKeyProvider`, prevalece sobre qualquer fake.
- **Políticas de `HttpClient` e `Resilience`** ficam dentro do método de cada trilha (`AddHttpClient("bot-text")` etc.), nunca em um lugar comum.
- Constantes de timeout/retry em uma classe estática **por trilha** (`BotHttpOptions`, `GeminiHttpOptions`, ...), para os testes de configuração (AC-010-49).

### 1.4 `AppSettings` (arquivo compartilhado, uma linha)

Acrescentar `public string GeminiModel { get; init; } = "gemini-2.5-flash-lite";` e a leitura opcional de `GEMINI_MODEL` em `Load`. As demais variáveis já existem (spec §9). Teste em `AppSettingsTests` (padrão e override).

### 1.5 Apoio de teste (`tests/`, arquivos novos; cada trilha só **adiciona** os dela depois)

| Arquivo | Para quê |
|---|---|
| `UnitTests/TestSupport/FakeHttpHandler.cs` | `HttpMessageHandler` que enfileira respostas ou funções, registra `RecordedRequest(Method, Uri, Headers, BodyText)`, simula atraso e exceção; helper `AssertJsonBody(...)` |
| `UnitTests/TestSupport/InMemoryCache.cs` | `ICache` em memória com TTL por `FakeTimeProvider`, glob simples, e modo "indisponível" |
| `UnitTests/TestSupport/FakeGoogleHttpClientFactory.cs` | `Google.Apis.Http.IHttpClientFactory` sobre o `FakeHttpHandler` (d1 e d2 usam) |
| `IntegrationTests/Support/RedisFixture.cs` | Redis 7 em Testcontainers (`Testcontainers.Redis`), com `ConnectionString` e `RedisUrl` no formato `redis://host:porta` |
| `Fixtures/generate_integrations_fixtures.py` e `integrations-fixtures.json` | ver §6 |

### 1.6 Testes da 010-0

`CacheKeysTests` (AC-010-10), `PhoneMaskTests`, `ProviderKeySelectorTests` (AC-010-123), `AppSettingsTests` (GEMINI_MODEL), `InMemoryCacheTests` e `FakeHttpHandlerTests` (os fakes têm teste próprio), `ArchitectureTests` (a camada Application não referencia Google/Redis/Npgsql).

## 2. 010a — Redis

**Infrastructure/Caching/**

| Classe | Papel |
|---|---|
| `RedisConnectionOptions` | `Parse(string redisUrl)` → `ConfigurationOptions`: esquemas `redis`/`rediss`; usuário/senha da `userinfo`; banco do caminho; `AbortOnConnectFail = false`, `ConnectTimeout = 2000`, `AsyncTimeout = 2000`, `SyncTimeout = 2000`, `ConnectRetry = 1`, `ReconnectRetryPolicy` exponencial. URL inválida lança `FormatException` com mensagem sem o valor. Validada em `AppSettings`? **Não** (arquivo compartilhado): a validação roda no construtor de `RedisCache`, que é criado por `GetRequiredService<ICache>()` no teste de boot e na primeira resolução; o `Program.cs` já resolve `AppSettings` no boot, e um teste de integração `Program` cobre "REDIS_URL inválida falha cedo" resolvendo `ICache` |
| `RedisCache : ICache` | singleton; `Lazy<Task<IConnectionMultiplexer>>`; cada operação: obtém o multiplexador, se `!IsConnected` devolve o neutro e registra aviso limitado (1/min); senão executa com `try/catch` por operação; `SetJsonAsync` = `StringSetAsync(key, json, ttl)`; `SetIfNotExistsAsync` = `StringSetAsync(key, raw, ttl, When.NotExists)` (equivale a `SET NX EX/PX`); `ExistsAsync`, `DeleteAsync`; `KeysByPatternAsync` percorre os servidores mestres com `server.KeysAsync(pattern: ..., pageSize: 250)` (SCAN) |
| `CacheJson` | opções do `System.Text.Json`: `PropertyNamingPolicy = null`, `JavaScriptEncoder.Default` (escapa não-ASCII como o `ensure_ascii` do Python), sem indentação; `JsonElement` para leitura genérica; texto simples serializado como JSON de texto |
| `CacheLog` | `[LoggerMessage]` EventIds 10100 a 10199: `Unavailable(operation)` (limitado), `OperationFailed(operation, error)`; só chave truncada e `SafeError`-like (`ex.GetType().Name`) |
| `CachingServiceCollectionExtensions.AddRedisCache` | `AddSingleton<ICache, RedisCache>()`; `RedisCacheOptions` lido de `AppSettings.RedisUrl` |

Sem Api. Não há migration. **Implementação (010a):** `UserCacheCleaner` (Dapper só para ler `usuarios.numero_whatsapp`) apaga `pending_tx`, `last_pending`, `pending_event`, `nightly_checkin*` do usuário; como `DeleteAccount` (008) chama `ClearAsync(userId)` DEPOIS do commit, a linha já não existe e a limpeza vira no-op registrado em log (o TTL de até 1 h expira as chaves). Para limpar de fato a 008 precisa ler o número antes de deletar (pendência registrada no relatório da 010a).

**Testes:** unitários de `RedisConnectionOptions` (AC-010-09) e de `CacheJson` (round trip, escapes, AC-010-02, 03, 12, 13 com vetores literais do Python); integração com `RedisFixture` (AC-010-01 a 08, 11); o fail-open usa um `RedisCache` apontado para uma porta fechada (rápido: timeout de 2 s só no primeiro acesso; o teste usa `ConnectTimeout` pequeno via opções).

## 3. 010b — Bot WhatsApp

**Infrastructure/Bot/**

| Classe | Papel |
|---|---|
| `BotHttpOptions` | constantes: `TextTimeout = 10 s`, `ImageTimeout = 30 s`, `SessionsTimeout = 10 s`; mutáveis por `IOptions` nos testes |
| `BotSessionResolver` | `ResolveAsync(bool forceRefresh, ct)` → `string?` (RN-010-11): override, cache (`CacheKeys.BotSessionId`, `GetJsonAsync<string>`), `GET /api/v1/sessions`, escolha exata por `session_name`, `SetJsonAsync(id, 600 s)`; qualquer exceção ⇒ `null` e log sem corpo |
| `BotClient : IBotClient` | `SendTextAsync`/`SendImageAsync` → `PostAsync(v1Suffix, legacyPath, payload, timeout)`: monta URL/headers (RN-010-12), faz o POST, aplica o retry de 404 (RN-010-13) com `cache.DeleteAsync` + `ResolveAsync(force: true)`; devolve `status == 200`; `catch` ⇒ `false`. Payload serializado com `Utf8JsonWriter` (ordem `numero`, `mensagem` | `numero`, `imagem`, `legenda`), base64 padrão com padding. `BOT_WHATSAPP_URL` vazia ⇒ `false` sem requisição |
| `BotLog` | EventIds 10200 a 10299; só status, tipo (`texto`/`imagem`), `PhoneMask.Last4` |
| `BotServiceCollectionExtensions.AddBotClient` | dois `HttpClient` nomeados: `bot-text` (timeout 10 s) e `bot-image` (30 s), cada um com `AddResilienceHandler` **só com `AddTimeout`** (sem retry: RN-010-15); a consulta de sessões usa `bot-text`; `AddSingleton<BotSessionResolver>()`, `AddSingleton<IBotClient, BotClient>()` |

Os dois clientes nomeados existem porque os timeouts diferem (10 s e 30 s) e a política é por cliente. O retry de 404 é de negócio (invalida cache), por isso fica no `BotClient`, não na resiliência.

**Testes:** unitários com `FakeHttpHandler` + `InMemoryCache` (AC-010-20 a 33, validando método, URL, headers e corpo exatos; timeouts por `FakeTimeProvider` + pipeline); integração com `RedisFixture` (AC-010-34) depois que a 010a estiver em `main`.

## 4. 010c — Gemini

**Domain/Llm/** (puro): `LlmCacheKey.Build(prefix, userId, userSpecific, prompt)` (MD5 UTF-8, 12 hex, `gemini_cache:{prefix}:{uid|global}:{hash}`), `LlmCacheKinds` (tabela de TTL de RN-010-27), `LlmJson.Parse`.

**Application/Llm/**

| Classe | Papel |
|---|---|
| `LlmClient : ILlmClient` | (1) `IProviderKeySelector.SelectAsync(userId, "gemini")` **antes do cache** (RN-010-23); `Failed` ⇒ `LlmKeyUnavailableException(Message)`; (2) sem política: `IGeminiTransport.GenerateAsync`; (3) com política: validação de `UserSpecific`, `ICache.IsAvailableAsync` (bypass), `GetJsonElementAsync`, HIT/MISS, `SetJsonAsync({text, prompt_feedback, cached_at}, ttl)`; erros de cache engolidos e contados; erro do transporte relançado; só grava texto bem-sucedido |
| `LlmCacheMetricsCollector` | contadores com `Interlocked` (total e por prefixo); `Snapshot()` |
| `LlmCacheAdmin : ILlmCacheAdmin` | `InvalidateUserAsync`, `InvalidatePatternAsync` (varre com `KeysByPatternAsync` e apaga uma a uma, como o Python), `GetMetrics()` com `hit_rate` formatado em cultura invariante |

**Infrastructure/Llm/**

| Classe | Papel |
|---|---|
| `GeminiHttpTransport : IGeminiTransport` | monta `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` com header `x-goog-api-key` **na requisição** (nunca em `DefaultRequestHeaders`) e corpo `{"contents":[{"role":"user","parts":[{"text":prompt}]}]}`; lê candidatos, `promptFeedback.blockReason` e `error` |
| `GeminiResponseParser` | texto concatenado; `LlmBlockedException`; mapeamento 429/403/outros (RN-010-24) |
| `GeminiHttpOptions` | modelo (`AppSettings.GeminiModel`), timeout por tentativa 45 s, total 90 s, 1 retry em 503/504/rede com 1 s de atraso |
| `LlmLog` | EventIds 10300 a 10399: prefixo, HIT/MISS, ms, tipo do erro, `user_id`; nunca prompt, resposta ou chave |
| `LlmServiceCollectionExtensions.AddGeminiClient` | `AddHttpClient("gemini")` + `AddResilienceHandler` (timeout por tentativa, retry com `ShouldHandle` só 503/504/`HttpRequestException`/timeout, timeout total); `AddSingleton<IGeminiTransport, GeminiHttpTransport>()`, `AddSingleton<ILlmClient, LlmClient>()`, `AddSingleton<LlmCacheMetricsCollector>()`, `AddSingleton<ILlmCacheAdmin, LlmCacheAdmin>()` |

**Catálogo de prompts:** só o apêndice A da spec; **nenhum arquivo de prompt** nesta trilha. A 012 cria `Infrastructure/Llm/Prompts/*.txt` e `PromptCatalog` (o diretório é da 012 para não haver conflito).

**Testes:** unitários de `LlmCacheKey`/`LlmCacheKinds`/`LlmJson` (AC-010-50, 58; vetor `abc` ⇒ `900150983cd2`); do `GeminiResponseParser` e do `GeminiHttpTransport` com `FakeHttpHandler` (AC-010-40 a 48, incluindo o teste concorrente de chaves, AC-010-42); do `LlmClient` com `InMemoryCache`, `FakeTimeProvider` e `IApiKeyProvider` falso (AC-010-43, 44, 51 a 55, 57, 61); `LlmCacheAdmin` com `InMemoryCache` (AC-010-56).

## 5. 010d — Google

### 5.1 d1 — OAuth, credenciais e rotas

**Application/Google/**

| Classe | Papel |
|---|---|
| `OAuthStateCodec` | `Create(userId)` / `Verify(state)` (RN-010-35): HMAC-SHA256 com a chave do `IIntegrationSettings.ApiSecretKey`, `TimeProvider`, `CryptographicOperations.FixedTimeEquals`, payload `{"p":"gcal-oauth","u":..,"e":..}`; `Verify` devolve `userId` ou `null` |
| `GoogleLinks` | `ConnectUrl(userId)` (RN-010-41) e `Base()`; puro, usado por 012 e 014 |
| `GoogleAccountService : IGoogleAccountService` | `GetCredentialsAsync` (RN-010-37), `IsConnectedAsync`, `HasDriveScopeAsync` (RN-010-39), `RevokeAsync` (RN-010-40), `ExchangeAndSaveAsync(code, state)` (RN-010-36); marca `needs_reconnect` e chama `TokenRevokedNotifier` |
| `TokenRevokedNotifier` | RN-010-38 (dedupe por `CacheKeys.CalendarTokenRevokedAlert`, `IGoogleUserLookup`, `IBotClient`, textos literais em `GoogleNotificationMessages`) |
| `ConnectCalendar`, `HandleOAuthCallback`, `DisconnectCalendar` | casos de uso (`ExecuteAsync`), resultados tipados: `ConnectResult` (`DatabaseUnavailable`, `UserNotFound`, `AlreadyConnected`, `Redirect(url)`, `Failed`), `CallbackResult` (`Denied`, `MissingParams`, `Connected(nome)`, `InvalidState`, `Failed`), `DisconnectResult` (`Disconnected`, `Failed`). Sem referência a HTTP |

**Infrastructure/Google/**

| Classe | Papel |
|---|---|
| `GoogleOAuthClient : IGoogleOAuthClient` | `GoogleAuthorizationCodeFlow` (Google.Apis.Auth) com `ClientSecrets`, `Scopes`, `AuthorizationServerUrl = https://accounts.google.com/o/oauth2/auth` (mesma do Flask), `TokenServerUrl = https://oauth2.googleapis.com/token`, `HttpClientFactory` injetado (`Google.Apis.Http.IHttpClientFactory`). `BuildAuthorizationUrl(state)` usa `GoogleAuthorizationCodeRequestUrl` com `AccessType = "offline"`, `Prompt = "consent"`, `IncludeGrantedScopes = "true"`, sem PKCE. `ExchangeCodeAsync` e `RefreshAsync` convertem `TokenResponse` em `GoogleTokenSet`; `TokenResponseException` com `Error == "invalid_grant"` ⇒ `GoogleInvalidGrantException`, outros ⇒ `GoogleAuthException`. `RevokeAsync` usa `flow.RevokeTokenAsync(...)` (POST ao `https://oauth2.googleapis.com/revoke`, token no corpo), com `CancellationToken` de 10 s |
| `GoogleTokenRepository : IGoogleTokenRepository` | Dapper, SQL em §5.3 |
| `GoogleUserLookup : IGoogleUserLookup` | `SELECT nome, numero_whatsapp FROM usuarios WHERE id = @id` (e a variante só com `nome`) |
| `GoogleOAuthRegistration.AddGoogleOAuth` | registra `IGoogleOAuthClient`, `IGoogleTokenRepository`, `IGoogleUserLookup`, `OAuthStateCodec`, `GoogleAccountService`, `TokenRevokedNotifier` (a `IHttpClientFactory` do Google já vem da 010-0) |
| `GoogleLog` | EventIds 10400 a 10449; só `user_id`, tipo/`SqlState` do erro e status; nunca token, `code`, `state`, secret |

**Api/Google/**

| Classe | Papel |
|---|---|
| `GoogleModule : IFeatureModule` | `AddServices`: `AddScoped<ConnectCalendar>()` etc. `MapEndpoints`: `GET /connect-calendar/{usuarioId:regex(^\d+$)}` (conversão `int.TryParse`; acima de `int.MaxValue` ⇒ `FlaskNotFound.WriteAsync`), `GET /oauth2callback`, `POST /disconnect-calendar/{usuarioId:regex(^\d+$)}` com `.RequireApiKey(ctx => Envelopes.B.Error("Não autorizado", 401))` (filtro da 008-0 T2; o preset do 401 em envelope B é local à rota, sem editar a classe `ApiKeyUnauthorized`) |
| `GooglePages` | quatro constantes de texto HTML (`AlreadyConnected`, `Denied`, `Connected` com marcador `{nome}`, `Error` com marcador `{texto}`) como *raw string literals*, **geradas por script a partir do Python** (§6) e commitadas; evita editar o `.csproj` da Api (sem `EmbeddedResource`) |
| `GoogleMessages` | textos fixos (`Erro: Usuario nao encontrado`, `Erro: codigo ou state faltando`, `Erro ao conectar`, mensagens do disconnect) e a mensagem de WhatsApp B.5 |
| `GoogleResponses` | `Html(status, body)` = `Results.Content(body, "text/html; charset=utf-8", Encoding.UTF8, status)`; 503 com `Envelopes.E.Error("Banco de dados temporariamente indisponivel", 503)` (que produz `{"status":"erro","resposta":...}`); redirecionamento com `Results.Redirect(url)` (302) |

O endpoint do callback lê só a query (`code`, `state`, `error`); o de `connect-calendar` lê `force`; nenhum lê corpo (desvio 024 não se aplica). `ensure_db_connection()` equivale a `IDatabaseHealth` (já existe) ⇒ 503.

### 5.2 d2 — Calendar, Drive e formatadores

**Domain/Google/** (puro, com `IClock` quando precisa de "hoje"): `CalendarEventPayloads` (RN-010-48: dia inteiro, com horário, +1 h, formato `dateTime` do `isoformat()`; `DateTimeRaw`), `CalendarFormatter` (RN-010-53), `DriveFilePolicy` (RN-010-50: validação, 17 MIME, mensagens), `DriveQuery` (`FolderSearch(name, parentId)` com escape de `'`).

**Infrastructure/Google/**

| Classe | Papel |
|---|---|
| `CalendarClient : ICalendarClient` | obtém o token por `IGoogleAccountService.GetCredentialsAsync` (já renovado pela d1) e monta `CalendarService` com `GoogleCredential.FromAccessToken(token)`; não-conectado ⇒ `GoogleNotConnectedException` (textos de RN-010-46); `ListSelectedCalendarsAsync` (`CalendarList.List()` sem parâmetros, filtra `Selected == true`), `ListEventsAsync` (`TimeMinDateTimeOffset`/`TimeMaxDateTimeOffset` ou `TimeMinRaw`/`TimeMaxRaw` com o texto RFC 3339 `-03:00`, `SingleEvents = true`, `OrderBy = StartTime`, `Q`, `MaxResults`), `GetEventsInRangeAsync` (helper RN-010-47), `Get/Insert/Update/DeleteEventAsync`, `FindEventsByTitleAsync` |
| `DriveClient : IDriveClient` | `DriveService` com `GoogleCredential.FromAccessToken`; exige escopo (RN-010-46); `Files.List` (`Q`, `Spaces = "drive"`, `Fields = "files(id, name)"`, `PageSize = 1`), `Files.Create` (pasta e arquivo; upload com `CreateMediaUpload(metadata, stream, mimeType)` e `Fields = "id, name, webViewLink, webContentLink"`); mapeamento de erro RN-010-52 por `GoogleApiException` (`HttpStatusCode`, `Error.Errors[].Reason`) |
| `GoogleApiClientsRegistration.AddGoogleApiClients` | `AddSingleton<ICalendarClient, CalendarClient>()`, `AddSingleton<IDriveClient, DriveClient>()`; `BaseClientService.Initializer.HttpClientFactory` = o `Google.Apis.Http.IHttpClientFactory` da 010-0 (timeout de 60 s, substituível em teste); `ApplicationName = "MeuSecretario"` |

**Decisão:** o token é pedido por chamada à d1 (que renova), e o cliente do SDK recebe só o `access_token` (`GoogleCredential.FromAccessToken`), sem `UserCredential` com refresh próprio. Assim a regra de renovação, `needs_reconnect` e notificação continua em **um** lugar (`GoogleAccountService`), idêntico ao `get_credentials` do Python, e d1 e d2 não compartilham arquivo.

### 5.3 SQL (Dapper, `googlecalendartokens`)

```sql
-- localizar
SELECT access_token AS AccessToken, refresh_token AS RefreshToken, token_expiry AS TokenExpiry,
       scopes AS Scopes, needs_reconnect AS NeedsReconnect
FROM googlecalendartokens WHERE usuario_id = @uid;

-- gravar (troca de código e renovação)
INSERT INTO googlecalendartokens (usuario_id, access_token, refresh_token, token_expiry, scopes, needs_reconnect, updated_at)
VALUES (@uid, @access, @refresh, @expiry, @scopes, FALSE, CURRENT_TIMESTAMP)
ON CONFLICT (usuario_id) DO UPDATE SET
  access_token = EXCLUDED.access_token,
  refresh_token = COALESCE(EXCLUDED.refresh_token, googlecalendartokens.refresh_token),
  token_expiry = EXCLUDED.token_expiry,
  scopes = EXCLUDED.scopes,
  needs_reconnect = FALSE,
  updated_at = CURRENT_TIMESTAMP;

-- marcar
UPDATE googlecalendartokens SET needs_reconnect = TRUE, updated_at = CURRENT_TIMESTAMP WHERE usuario_id = @uid;

-- escopo do Drive
SELECT scopes FROM googlecalendartokens WHERE usuario_id = @uid;

-- remover
DELETE FROM googlecalendartokens WHERE usuario_id = @uid;
```

`token_expiry` é `timestamptz` (`DateTime` UTC, Npgsql 6+); `scopes` é texto (array JSON); o upsert exige o UNIQUE de `usuario_id` (existe). `PostgresException` `23503` (usuário inexistente) sobe como erro do caso de uso ⇒ página de erro. Sem migration.

### 5.4 Testes (010d)

| Tipo | O que cobre | AC |
|---|---|---|
| Unitário | `OAuthStateCodec` (round trip, adulterado, expirado com `FakeTimeProvider`, formato antigo) | 75 |
| Unitário | `GoogleAccountService` com `NSubstitute` (repositório, `IGoogleOAuthClient`, `IBotClient`, `InMemoryCache`, `FakeTimeProvider`): leitura, renovação, `invalid_grant`, sem refresh, notificação | 83 a 87 |
| Unitário | `GoogleLinks`, `GoogleNotificationMessages` (texto literal) | 88 |
| Unitário | `GoogleOAuthClient` com `FakeGoogleHttpClientFactory`: URL de autorização (parâmetros), corpo do `/token` (troca e renovação), `/revoke`, `invalid_grant` | 72, 77, 84, 81 |
| Unitário | `ConnectCalendar`, `HandleOAuthCallback`, `DisconnectCalendar` (ordem e resultados) | 70 a 81 |
| Unitário | `CalendarEventPayloads`, `CalendarFormatter` (fixtures do Python), `DriveFilePolicy`, `DriveQuery` | 92, 94, 95, 97 |
| Unitário | `CalendarClient`/`DriveClient` com o fake do Google: parâmetros de `events.list`, corpo do `insert`/`update`, `files.list`/`create` | 90 a 96, 98 |
| Integração (Docker) | `GoogleTokenRepository` em PostgreSQL real (upsert com `COALESCE`, `needs_reconnect`, `scopes`, FK) | 77, 78, 85, 87 |
| Integração (Docker) | Rotas com `WebApplicationFactory`: 503, 404, 200 HTML idêntico à fixture, 302 e `Location`, callback ponta a ponta com fake do Google e do bot, disconnect com e sem `x-api-key`, roteamento (`abc`, `-1`, barra final, 405) | 70 a 82 |
| Contrato | HTML das 4 páginas comparado byte a byte com a saída do Python (§6) | 71, 76, 77, 80 |

## 6. 010e — Clima e rotas

**Domain/Weather/** `WeatherConditions` (o mapa literal de 22 entradas de `weather_service.py:18-54`, com padrão `(texto, termômetro)`), `WeatherFormatter` (RN-010-57). **Domain/Routes/** `BrazilBounds.Contains(lat, lon)` (`-33..5`, `-74..-34`), `TravelMath.DurationMinutes(seconds)` (trunca) e `DistanceKm(meters)` (`Math.Round(x, 1, MidpointRounding.ToEven)`).

**Application/Routes/** `TravelRateLimiter : ITravelRateLimiter` (`ICache`, `IClock.Today`, `IIntegrationSettings.TravelTimeDailyLimit`; lê `3` e `"3"`; grava `"N"` com TTL 86400 s).

**Infrastructure/External/**

| Classe | Papel |
|---|---|
| `WeatherApiClient : IWeatherClient` | `GET https://api.weatherapi.com/v1/current.json` e `forecast.json` (RN-010-55/56); chave por `IProviderKeySelector` (RN-010-58); `Uri.EscapeDataString` nos parâmetros; qualquer exceção ⇒ `null`; log sem URL |
| `OpenRouteClient : IRouteClient` | `GeocodeAsync` e `CalculateTravelTimeAsync` (RN-010-60/61), JSON lido com `JsonDocument`; coordenadas escritas com `Utf8JsonWriter` (`[lon, lat]`); falha de chave propaga `ApiKeyUnavailableException` |
| `ProviderKeyValidator : IProviderKeyValidator` | RN-010-59; `HttpClient` `provider-validation` (5 s, sem retry); exceção de rede ⇒ `ProviderUnavailableException` |
| `ExternalLog` | EventIds 10500 a 10599 |
| `ExternalServiceCollectionExtensions.AddWeatherAndRoutes` | `AddHttpClient("weather")` (timeout 5 s, sem retry); `AddHttpClient("openroute")` com `AddResilienceHandler` (timeout 15 s por tentativa, retry **1** vez sem atraso para timeout, exceção de rede e **qualquer status não-2xx**, RN-010-62; **P-010-12 aprovado** reduz para 5xx/timeout/rede); `AddHttpClient("provider-validation")` (5 s); `AddSingleton` das portas e do `TravelRateLimiter` |

**Testes:** unitários de `WeatherConditions`/`WeatherFormatter` (fixtures geradas pelo Python, AC-010-114), `WeatherApiClient` e `OpenRouteClient` com `FakeHttpHandler` (AC-010-110 a 113, 115, 117 a 119, 122), `ProviderKeyValidator` (AC-010-116), `TravelRateLimiter` com `InMemoryCache` e `FakeTimeProvider` (AC-010-120, 121; inclui "23:30 em São Paulo é o dia seguinte em UTC"); integração com Redis real para a compatibilidade `3`/`"3"` (AC-010-120).

## 7. Fakes e fixtures

- **HTTP:** `FakeHttpHandler` valida o **payload enviado** (método, URL, query, headers, corpo) em todos os clientes (regra de `testing-strategy.md`). Teste de concorrência de chaves do Gemini usa respostas com atraso controlado.
- **Google:** `FakeGoogleHttpClientFactory` injeta o handler nos SDKs (`BaseClientService.Initializer.HttpClientFactory` e `GoogleAuthorizationCodeFlow.Initializer.HttpClientFactory`).
- **Relógio:** `FakeTimeProvider` em todos (TTL de cache falso, expiração do `state`, token expirado, data do limite de rotas).
- **Redis:** `InMemoryCache` nos unitários; `RedisFixture` (Testcontainers) nos de integração (`[Trait("Category","Docker")]`, mesmo critério do `ci.yml`).
- **Fixtures geradas pelo Python** (`tests/Fixtures/generate_integrations_fixtures.py`; saída `integrations-fixtures.json` versionada; o CI não precisa de Python): (a) valores de `json.dumps` para o Redis (texto, booleano, número, objeto com acento e emoji); (b) `hash12` do Gemini e MD5 do `calendar_alert`; (c) saídas de `CalendarFormatter` carregando `google_calendar_service.py` **por caminho de arquivo** (`importlib.util.spec_from_file_location`, sem importar o pacote `app`, que exige variáveis de ambiente); (d) `WeatherService.format_weather_for_briefing` e a tabela de tradução (idem, instanciando com `__new__`); (e) as 4 páginas HTML e a mensagem de WhatsApp, extraídas do `calendar_handler.py` com `ast` (renderizando o f-string com um nome de teste) e gravadas em `Api/Google/GooglePages.cs`. As chaves e números são fictícios.

## 8. Riscos e decisões

**Decisões que precisam de aprovação**

- **D-1 Gemini por REST, não pelo SDK.** `tech-stack.md` diz "SDK oficial do Gemini atrás de `ILlmClient`". Proponho `HttpClient` direto no `generateContent`: (a) a chave por chamada vai num header da requisição (os SDKs .NET prendem a chave no cliente na construção, exigindo um cliente por chave); (b) o fake de HTTP valida o payload exato; (c) só há **um** endpoint; (d) nenhum pacote novo. Custo: manter a forma do JSON (pequena). Se o Rafael preferir o SDK, a única mudança é `GeminiHttpTransport` (a porta `IGeminiTransport` isola).
- **D-2 `Testcontainers.Redis`** como pacote novo de teste (spec e §1.1).
- **D-3 Timeouts e 1 retry no Gemini** (45 s por tentativa, 90 s total, 1 retry em 503/504/rede) são decisão desta spec; o Python não define timeout. Valores iniciais, ajustáveis em `GeminiHttpOptions`. O nginx corta em 120 s; um webhook que faz 2 chamadas ao Gemini pode estourar o corte no pior caso (a 012 deve considerar).
- **D-4 Token Google entregue aos SDKs como `access_token`** (§5.2), sem `UserCredential` com refresh próprio.
- **D-5 `IIntegrationSettings` e `IProviderKeySelector`** como peças novas da 010-0 (evitam que cada cliente releia `AppSettings` e repita a regra de chave).

**Riscos**

- **Compatibilidade de valores no Redis** é testável só com fixtures: qualquer divergência de nome de campo em objetos gravados pelas features 012/013 desfaz o rollback para o Python. Responsabilidade das specs 012/013, mas o `CacheJson` impõe `PropertyNamingPolicy = null`.
- **Fluxo OAuth de produção possivelmente quebrado hoje** (PKCE, pergunta 3 da spec). O .NET não replica isso; se o fluxo atual funciona por outro motivo, o .NET ainda deve funcionar porque cliente web com `client_secret` não exige PKCE.
- **Tokens do Google em texto puro** (desvio 029): o log de erro dos SDKs pode incluir URL/corpo; `GoogleLog` e o `SafeError` evitam, e há teste de log (AC-010-89).
- **`is_connected` pode renovar o token e disparar WhatsApp** (RN-010-39): é o comportamento do Flask; coberto por teste para ninguém "otimizar" sem querer.
- **Limite de 4096 caracteres do bot** (P-010-7): sem aprovação, mensagens longas continuam falhando em silêncio.
- **Mudança de modelo do Gemini** não invalida o cache (a chave não inclui o modelo); documentado no `GeminiHttpOptions`.
- **Migrations:** nenhuma; 0040 a 0049 permanecem livres.
