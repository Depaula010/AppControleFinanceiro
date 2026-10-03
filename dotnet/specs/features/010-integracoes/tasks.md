# Tasks 010 — Integrações

Regra: cada tarefa termina com testes verdes e um commit. Dentro de uma trilha a ordem importa; **entre trilhas** o que importa está na tabela abaixo. Cada critério citado (`AC-010-NN`) vira pelo menos um teste com `[Trait("AC","010-NN")]`. Testes de integração (Docker) levam `[Trait("Category","Docker")]`.

## Mapa de paralelismo

| Trilha | Tarefas | Pode começar quando | Roda em paralelo com | Merge |
|---|---|---|---|---|
| **010-0** | T1–T5 | 008-0 T3 em `main` (`IApiKeyProvider`) | nada (sequencial, PR pequeno) | primeiro |
| **010a** Redis | T6–T9 | 010-0 em `main` | b, c, d1, d2, e | logo depois da 010-0 |
| **010b** Bot | T10–T14 | 010-0 em `main` | a, c, d1, d2, e | depois da 010a (T14 exige a 010a) |
| **010c** Gemini | T15–T21 | 010-0 em `main` | a, b, d1, d2, e | qualquer ordem |
| **010d-1** OAuth e rotas | T22–T30 | 010-0 em `main`; **T29 (rota `disconnect`) exige 008-0 T2** (`RequireApiKey`) | a, b, c, d2, e | depois da 010b para o teste ponta a ponta com o bot real (os unitários usam o fake de `IBotClient`) |
| **010d-2** Calendar e Drive | T31–T37 | 010-0 em `main`; usa só a porta `IGoogleAccountService` (fake nos testes) | a, b, c, d1, e | qualquer ordem; teste de integração conjunto com a d1 depois que ambas estiverem em `main` |
| **010e** Clima e rotas | T38–T44 | 010-0 em `main` | a, b, c, d1, d2 | qualquer ordem (T43 compatibilidade `3`/`"3"` com Redis real exige a 010a) |
| **Fechamento** | T45 | todas as trilhas em `main` | — | último |

Dependências externas: **008-0 T3** (porta `IApiKeyProvider`) para 010-0, 010c e 010e; **008-0 T2** (`RequireApiKey`) para a T29 da 010d-1. Consumidores (009, 011, 012, 013, 014) só dependem da **010-0** para compilar contra as portas (usando `InMemoryCache`, `FakeHttpHandler` e substitutos).

## 010-0 — Contratos e pontos compartilhados (PR único)

- [x] **T1 — Pacotes.** `Microsoft.Extensions.Http.Resilience`, `Google.Apis.Auth`, `Google.Apis.Calendar.v3`, `Google.Apis.Drive.v3` em Infrastructure; `Testcontainers.Redis` em IntegrationTests; versões no `Directory.Packages.props`; linha em `tech-stack.md` (tabela "Testes") justificando o pacote novo. `dotnet build` verde.
- [x] **T2 — Portas, records e exceções** em `Application/Abstractions` (plan §1.2), `CacheKeys` e `PhoneMask`. Testes unitários: `CacheKeysTests` (AC-010-10) e `PhoneMaskTests`.
- [x] **T3 — `IIntegrationSettings`, `ProviderKeySelector` e `AppSettings.GeminiModel`.** Testes: `ProviderKeySelectorTests` com `IApiKeyProvider` falso (AC-010-123), `AppSettingsTests` (`GEMINI_MODEL` padrão e override).
- [x] **T4 — DI sem conflito.** Criar os métodos vazios `AddRedisCache`, `AddBotClient`, `AddGeminiClient`, `AddGoogleIntegration` (+ `AddGoogleOAuth`, `AddGoogleApiClients` e `GoogleHttpClientFactory`), `AddWeatherAndRoutes`; **uma** edição em `AddInfrastructure` registrando as peças de T3 e chamando os cinco métodos. Teste: o contêiner resolve `IIntegrationSettings` e `IProviderKeySelector`.
- [x] **T5 — Apoio de teste.** `FakeHttpHandler`, `InMemoryCache`, `FakeGoogleHttpClientFactory`, `RedisFixture`, esqueleto de `generate_integrations_fixtures.py` + `integrations-fixtures.json`. Cada fake com teste próprio; `ArchitectureTests`: Application não referencia Google, Redis, Npgsql nem `System.Net.Http`.

## 010a — Redis e cache

- [x] **T6 — `RedisConnectionOptions`.** Testes vermelhos ⇒ implementação: `redis://host:porta`, `redis://:senha@host:6380/2`, `rediss://`, usuário e senha, valor inválido sem eco do valor (AC-010-09).
- [x] **T7 — `CacheJson` e fixtures do Python.** Round trip, escapes, texto JSON com aspas, valor cru, codificação dupla; fixtures `json.dumps` commitadas (AC-010-02, 03, 12, 13).
- [x] **T8 — `RedisCache` + `AddRedisCache`.** Integração com `RedisFixture`: AC-010-01, 04 (50 chamadas concorrentes), 05, 06; fail-open e reconexão (AC-010-07, 08); boot sem Redis (AC-010-11).
- [x] **T9 — Logs e contrato com os consumidores.** `CacheLog` sem valores (AC-010-07, limite de 1 aviso por minuto); nota no `plan.md`/`spec.md` se a implementação mudar algo; avisar 009 (uso de `SetJsonAsync<string>`) e 012 (ordem de `KeysByPatternAsync`).

## 010b — Bot WhatsApp

- [x] **T10 — `BotSessionResolver`.** Testes com `FakeHttpHandler` e `InMemoryCache`: header `X-API-Key`, correspondência exata do `session_name`, cache de 600 s, override `BOT_SESSION_ID`, sem correspondência não cacheia, erro ⇒ sem sessão (AC-010-20, 21, 22, 29, 33).
- [x] **T11 — `BotClient.SendTextAsync`.** URL, headers, corpo exato `{"numero","mensagem"}`, sucesso só com 200, falha de rede sem retry, URL vazia, barra final (AC-010-23, 25, 28, 30, 31).
- [x] **T12 — Retry de 404 e fallback legado.** Apagar cache, ressolver ignorando cache, 2 POSTs no máximo; `/enviar-mensagem` e `/enviar-imagem` com `x-api-key = API_SECRET_KEY` (AC-010-26, 27).
- [x] **T13 — `SendImageAsync`.** Base64 com padding, `legenda` nula, timeout de 30 s (AC-010-24); logs mascarados e sem corpo de resposta (AC-010-32); `AddBotClient` com os dois `HttpClient` nomeados e resiliência só de timeout.
- [x] **T14 — Integração com Redis real** (depende da 010a): valor `"<uuid>"` no Redis, TTL ≈ 600 s, ciclo 404 → apagar → ressolver (AC-010-34).

## 010c — Gemini

- [x] **T15 — Domain: `LlmCacheKey`, `LlmCacheKinds`, `LlmJson`.** Vetor `abc` ⇒ `900150983cd2`, prefixos e TTL, `LlmJson` com e sem cercas de código (AC-010-50, 58).
- [x] **T16 — `GeminiResponseParser` e `GeminiHttpTransport`.** Corpo exato `{"contents":[{"role":"user","parts":[{"text":...}]}]}`, header `x-goog-api-key` na requisição, modelo na URL, partes concatenadas, bloqueio, 429/403/400/503, retry só onde permitido (AC-010-40, 41, 45 a 48); teste concorrente com chaves distintas (AC-010-42).
- [x] **T17 — `LlmClient` (chave).** `IProviderKeySelector` antes do cache; `Failed` ⇒ `LlmKeyUnavailableException` sem requisição; usuário nulo usa a chave do sistema (AC-010-43, 44).
- [x] **T18 — `LlmClient` (cache) e `LlmCacheMetricsCollector`.** HIT/MISS, TTL, valor `{text, prompt_feedback, cached_at}`, bypass sem Redis, não grava erro, lê entrada do Python, `Cache` nulo (AC-010-51 a 55, 57).
- [x] **T19 — `LlmCacheAdmin`.** Padrões de varredura e contagem, Redis fora (AC-010-56); avisar a 009.
- [x] **T20 — DI, resiliência e logs.** `AddGeminiClient` com timeout por tentativa de 45 s, total de 90 s, 1 retry em 503/504/rede (AC-010-49); logs sem prompt, resposta ou chave (AC-010-61).
- [x] **T21 — Conferência do catálogo (apêndice A).** Rodar `grep` por `prompt =` e pelas funções `generate_content` e confirmar as 29 linhas, os arquivos:linha e os consumidores; ajustar o apêndice se algo mudou. Avisar a 012, a 013 e a 011 do que cada uma porta (sem criar arquivo de prompt aqui).

## 010d-1 — OAuth, credenciais e rotas

- [ ] **T22 — `OAuthStateCodec`.** Round trip, adulterado, outra chave, expirado com `FakeTimeProvider`, formato antigo, sem ponto (AC-010-75).
- [ ] **T23 — `GoogleTokenRepository` e `GoogleUserLookup`.** Integração em PostgreSQL (Testcontainers): upsert com `COALESCE` do refresh, `needs_reconnect`, `scopes` JSON, `updated_at`, FK de usuário inexistente, `HasDriveScope` (AC-010-78, 87 e a parte de banco de 77, 85).
- [ ] **T24 — `GoogleOAuthClient`.** Com `FakeGoogleHttpClientFactory`: URL de autorização (parâmetros e ausência de `code_challenge`), corpo de `/token` na troca e na renovação, `/revoke` com o token no corpo, `invalid_grant` (AC-010-72, 77, 81, 84).
- [ ] **T25 — `GoogleAccountService` e `TokenRevokedNotifier`.** Regras de RN-010-37 a 40 com fakes: sem linha, não expirado, expiry nulo, renovação, `invalid_grant`, erro temporário, sem refresh, notificação com dedupe de 7 dias e as duas variantes de texto (AC-010-83 a 86, 89).
- [ ] **T26 — `GoogleLinks` e textos.** `ConnectUrl` e `Base`; mensagens literais B.5 e de token revogado copiadas do Python (AC-010-88).
- [ ] **T27 — Páginas HTML.** Script `generate_integrations_fixtures.py` (parte das páginas, via `ast`) gera `Api/Google/GooglePages.cs`; testes comparam byte a byte (AC-010-71, 76, 77, 80).
- [ ] **T28 — Casos de uso `ConnectCalendar`, `HandleOAuthCallback`, `DisconnectCalendar`.** Ordem das verificações e resultados tipados (AC-010-70 a 81 no nível de caso de uso).
- [ ] **T29 — `GoogleModule` e rotas.** Integração com `WebApplicationFactory`, PostgreSQL real e fakes de Google e bot: 503, 404, 200 HTML, 302 e `Location`, `force`, callback ponta a ponta, disconnect com e sem `x-api-key` (**exige 008-0 T2**), roteamento `abc`/`-1`/barra final/405 (AC-010-70 a 82).
- [ ] **T30 — Revisão de segredos e desvios.** Nenhum log com token, `code`, `state` ou secret (AC-010-89); desvios 021, 003, 029, 008 conferidos; avisar a 012 e a 014 para usar `GoogleLinks`; perguntas 2, 3 e 5 da spec respondidas ou registradas.

## 010d-2 — Calendar, Drive e formatadores

- [ ] **T31 — `CalendarFormatter` e fixtures do Python.** `StripHtml`, `FormatTime`, dia e período, com `IClock` (AC-010-97).
- [ ] **T32 — `CalendarEventPayloads`.** Corpo do `insert` nos 3 casos e formato `dateTime` do `isoformat()` (AC-010-92); atualização (AC-010-93).
- [ ] **T33 — `DriveFilePolicy` e `DriveQuery`.** Validação, 17 MIME, mensagens, escape de `'` (AC-010-94, 95).
- [ ] **T34 — `CalendarClient`.** Com o fake do Google: parâmetros de `events.list`, `GetEventsInRange` (fallback `primary`, calendário com erro pulado, só `selected`, ordenação), `GoogleNotConnectedException` (AC-010-90, 91, 98).
- [ ] **T35 — `CalendarClient` (insert, update, delete, busca por título).** Corpo, 404 ⇒ "não encontrado", janela de 90 dias com `q` e `maxResults=10` (AC-010-92, 93).
- [ ] **T36 — `DriveClient`.** Busca e criação de pastas aninhadas, upload com `fields`, mapeamento de erros por HTTP e razão (AC-010-95, 96).
- [ ] **T37 — Registro e logs.** `AddGoogleApiClients`; sem URL nem token em log (AC-010-89 para os clientes); avisar 012 e 014 do que consomem.

## 010e — Clima e rotas

- [ ] **T38 — Domain: `WeatherConditions`, `WeatherFormatter`, `BrazilBounds`, `TravelMath`.** Fixtures do Python (mapa de 22 entradas, formatação, arredondamentos) (AC-010-114, 118).
- [ ] **T39 — `WeatherApiClient`.** HTTPS, `q`, `lang=pt`, truncamento, mapa em inglês × texto em português, `null` em erro, previsão com `days = min(dias, 3)` (AC-010-110 a 113, 115, 122).
- [ ] **T40 — `ProviderKeyValidator`.** Gemini, weather (desvio 019) e openroute, status por regra, `NotSupported`, `ProviderUnavailableException` (AC-010-116).
- [ ] **T41 — `OpenRouteClient`.** Geocode e rota: URL, query, header, corpo exato, `[lon, lat]`, limites do Brasil, `routes` vazio sem retry, truncamento e arredondamento (AC-010-117, 118).
- [ ] **T42 — Retry do OpenRoute e chave por usuário.** 2 tentativas incluindo status não-2xx, JSON inválido sem retry, `ApiKeyUnavailableException` (AC-010-119).
- [ ] **T43 — `TravelRateLimiter`.** `3` e `"3"`, `(true, 10, 0)` em erro, data de São Paulo, TTL de 86400 s; integração com Redis real para a compatibilidade (AC-010-120, 121).
- [ ] **T44 — DI, resiliência e logs.** `AddWeatherAndRoutes` com timeouts (5 s e 15 s) e retry do `openroute`; logs sem chave, URL, endereço ou coordenadas (AC-010-122).

## Fechamento

- [ ] **T45 — Contrato, desvios e PR(s).** Revisar cada chamada de saída e cada resposta de rota contra o código Python lido; marcar `implementado` os desvios 021, 022, 019, 026 (porta pronta; o uso é da 012), 029 (paridade), 008 e 003 (`disconnect-calendar`); **propostas P-010-1 a P-010-12 aprovadas viram arquivos de desvio** (não antes; a numeração continua depois do 052, que é o último existente); spec e plan atualizados se a implementação revelou algo (parar e pedir aprovação antes de seguir, como manda `specs/README.md`); cobertura Domain ≥ 90% e Application ≥ 80%; `cutover-runbook.md` com o passo de conferir `GOOGLE_REDIRECT_URI`, `BOT_*` e a limpeza opcional de `gemini_cache:*`; PRs abertos com CI verde.

Definição de pronto: `constitution.md` §10.
