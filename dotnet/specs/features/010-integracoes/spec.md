# Spec 010 — Integrações (Redis, bot WhatsApp, Gemini, Google, clima e rotas)

- **Estado:** aprovada (2026-10-02), com as decisões de `foundation/decisoes-onda-1.md` §7
- **Onda:** 1 (a mais ampla da onda; dividida em sub-entregas independentes, §2)
- **Origem (Python):** `app/services/redis_service.py:1-131`; `app/services/notification_service.py:1-204`; `app/services/gemini_service.py:1-1888`; `app/services/gemini_cache_service.py:1-366`; `app/services/google_calendar_oauth_service.py:1-669`; `app/services/google_calendar_service.py:1-185`; `app/services/google_drive_service.py:1-370`; `app/services/calendar_management_service.py:1-312` (só as chamadas à API do Google); `app/routes/webhooks/__init__.py:81-96`; `app/routes/webhooks/handlers/calendar_handler.py:1-359`; `app/services/weather_service.py:1-250`; `app/services/travel_time_service.py:1-533`; `app/routes/api.py:2630-2672` (validação de chave); contrato externo do bot em `bot-appfinanceiro-whatsapp/src/routes/messageRoutes.js` e `sessionRoutes.js` (somente leitura).
- **Depende de:** 001a (`IDbConnectionFactory`, `SafeError`, `Envelopes`, `AppSettings`, `IClock`); **008-0** (porta `IApiKeyProvider` e filtro `RequireApiKey`, ver `features/008-api-keys-lgpd/spec.md` §2 e §5)
- **Consumida por:** 007 (validação de chave), 009 (administração do cache do Gemini e Redis do filtro de segurança), 011 (automação), 012 (WhatsApp e intents), 013 (jobs), 014 (alertas de calendário)
- **Desvios aplicáveis:** 021, 022, 019, 026, 029, 008 e 003 (todos aprovados; §10). Propostas novas em §13.

## 1. Objetivo

Entregar, **sem nenhuma lógica de negócio financeira**, os clientes dos serviços externos e a infraestrutura compartilhada que as features 011 a 014 consomem, mais as 3 rotas de OAuth do Google:

1. cache e locks no Redis (`ICache`), compatível com os valores que o Python grava;
2. envio de texto e imagem pelo bot do WhatsApp (`IBotClient`);
3. chamadas ao Gemini com a chave do usuário passada **por chamada** (`ILlmClient`), com o cache por tipo e TTL e o **catálogo** dos prompts (o porte literal dos prompts fica com a 012);
4. Google: OAuth (`/connect-calendar`, `/oauth2callback`, `/disconnect-calendar`), credenciais por usuário, Calendar e Drive (`ICalendarClient`, `IDriveClient`);
5. clima (WeatherAPI) e rotas (OpenRouteService), mais a validação de chave por provedor que a 007 usa.

Fora as 3 rotas de OAuth, **nada aqui é rota HTTP**: são portas em `Application/Abstractions` e implementações em `Infrastructure`, testadas com fakes que validam o **payload enviado**.

## 2. Divisão em sub-entregas

Cada sub-entrega entra em PR próprio, em worktree/branch próprio (`feat/010a-redis` etc.), e **não edita arquivo de outra** (DI e pacotes ficam na 010-0; ver `plan.md` §1).

| Entrega | Conteúdo | Depende de | Consumidores |
|---|---|---|---|
| **010-0** (PR pequeno, primeiro, sequencial) | Contratos: interfaces, records e exceções em `Application/Abstractions`; `CacheKeys`; `IIntegrationSettings`; `IProviderKeySelector` (§3.1); máscara de número para log; stubs `Add<Nome>()` + uma única edição em `AddInfrastructure`; pacotes NuGet; `AppSettings.GeminiModel`; apoio de teste (`FakeHttpHandler`, `InMemoryCache`, `RedisFixture`) | 008-0 T3 (`IApiKeyProvider`) | todas as outras |
| **010a Redis/cache** | `ICache` sobre StackExchange.Redis; fail-open; formato JSON do Python; `CacheKeys` | 010-0 | 010b, 010c, 010d, 010e e features 009, 011 a 014 |
| **010b Bot WhatsApp** | `IBotClient`: sessão (cache 600 s), `send-message`, `send-image`, retry em 404, fallback legado | 010-0 (código); 010a só para o teste com Redis real | 010d, 011, 012, 013, 014 |
| **010c Gemini** | `ILlmClient` (REST, chave por chamada), cache por tipo/TTL, métricas, invalidação, `LlmJson`, catálogo dos prompts (apêndice A) | 010-0; 008-0 T3 | 012, 013, 011, 009 (admin do cache) |
| **010d Google** | **d1**: OAuth (3 rotas), state assinado, credenciais e `needs_reconnect`. **d2**: `ICalendarClient`, `IDriveClient`, formatadores de agenda | 010-0; d1 usa `IBotClient` e `ICache` só pelas portas; rota `disconnect-calendar` precisa do `RequireApiKey` (008-0 T2) | 012, 013, 014 |
| **010e Clima e rotas** | `IWeatherClient`, `IRouteClient` (geocode e directions), `ITravelRateLimiter`, `IProviderKeyValidator` | 010-0; 008-0 T3 | 007, 012, 013 |

d1 e d2 podem ser implementadas por agentes diferentes em paralelo (não compartilham arquivo). Ordem de merge recomendada: 010-0, depois 010a, 010b, e as demais em qualquer ordem. O grafo completo de dependências e as tarefas estão em `tasks.md`.

## 3. Convenções comuns a todas as sub-entregas

- **Portas** em `Application/Abstractions` (tipos simples; nunca tipos do SDK do Google nem `HttpClient`). Implementações em `Infrastructure/<Area>`.
- **HTTP de saída** sempre por `IHttpClientFactory` com `Microsoft.Extensions.Http.Resilience` (timeout e retry declarados por cliente, tabelas nas seções). Nenhum `HttpClient` estático, nenhum header de chave em `DefaultRequestHeaders` (a chave de um usuário nunca pode vazar para a requisição de outro).
- **Falha de integração não derruba a API.** Cada cliente devolve o valor neutro do Flask (`false`, `null`, lista vazia) onde o Python faz isso, e lança exceção tipada onde o Python propaga. O mapeamento está nas tabelas "Erros".
- **Segredos e dados pessoais nunca em log nem em mensagem de erro** (desvio 008): chaves de API, tokens do Google, `x-api-key`, URL com `key=` ou `api_key=`, prompt e resposta do Gemini, mensagem enviada, corpo de resposta do bot, número de WhatsApp completo (só os 4 últimos dígitos). Logs usam `SafeError.Describe` e o padrão `[LoggerMessage]` do `AuthLog` (EventIds 10100 a 10599, ver `plan.md`).
- **Tempo**: `IClock`/`TimeProvider`; nenhum `DateTime.Now`. Fuso `America/Sao_Paulo`, cultura `pt-BR` onde houver formatação.
- **Mensagens ao usuário** (WhatsApp, HTML das páginas OAuth) em português, copiadas literalmente do Python (ASCII sem acento nas páginas HTML, como no original).

### 3.1 Seleção da chave de API (010-0, usada por 010c e 010e)

- **RN-010-66** `IProviderKeySelector.SelectAsync(int? userId, string provider, ct)` decide a chave de uma chamada ao Gemini, ao WeatherAPI ou ao OpenRouteService: `userId` nulo ⇒ chave do sistema do provedor (`GEMINI_API_KEY`, `WEATHER_API_KEY`, `OPENROUTE_API_KEY`), **sem** consultar o 008 (é o que o Python faz sem `usuario_id`: modelo/chave globais); `userId` preenchido ⇒ `IApiKeyProvider.ResolveAsync(userId, provider)` (008): `Resolved(Key, KeyType)` ⇒ a chave; `Failed(Reason, Message)` ⇒ falha com o `Message` literal. Cada cliente decide o que fazer com a falha: Gemini lança `LlmKeyUnavailableException`, clima devolve `null`, rotas lançam `ApiKeyUnavailableException`. Chave do sistema vazia ⇒ "sem chave" (o cliente devolve o neutro do Python: clima `null`, rotas `null`; Gemini nunca fica vazia porque `GEMINI_API_KEY` é obrigatória). *Origem: `gemini_service.py:6-41`, `weather_service.py:56-81`, `travel_time_service.py:20-42`.*
- **AC-010-123** `userId` nulo devolve a chave do sistema do provedor e **não** chama `IApiKeyProvider`; `userId` com `Resolved` devolve a chave resolvida; com `Failed` devolve a falha com a mensagem idêntica; provedor desconhecido ⇒ `ArgumentException`; `ToString()` e logs do seletor nunca contêm a chave.

---

## 4. 010a — Redis e cache

### 4.1 Porta `ICache`

Equivale a `RedisService` (`redis_service.py`). Operações assíncronas, todas **fail-open**: sem Redis ou em erro devolvem o valor neutro e **nunca lançam**.

| Operação | Equivale a | Efeito no Redis | Retorno |
|---|---|---|---|
| `IsAvailableAsync()` | `is_connected()` | `PING` (ou estado da conexão) | `true` se há conexão utilizável |
| `SetJsonAsync<T>(key, value, ttl)` | `set_with_ttl` | `SETEX key ttl <json>` | `true` se gravou |
| `GetJsonAsync<T>(key)` / `GetJsonElementAsync(key)` | `get` | `GET key` + `json.loads` | valor; `null` se ausente, vazio, JSON inválido ou JSON `null` |
| `SetIfNotExistsAsync(key, rawValue, ttl)` | `set_if_not_exists` | `SET key rawValue NX EX ttl`, valor **cru** (sem JSON) | `true` só se o Redis confirmou `OK` (a chave não existia); `false` se já existia **ou** se o Redis está indisponível/erro |
| `ExistsAsync(key)` | `exists` | `EXISTS key` | `true`/`false` (`false` sem Redis) |
| `DeleteAsync(key)` | `delete` | `DEL key` | `true` se não houve erro (mesmo que a chave não existisse); `false` sem Redis |
| `KeysByPatternAsync(pattern)` | `get_keys_by_pattern` | glob `KEYS` (no .NET: `SCAN`, sem bloquear o servidor) | lista; vazia sem Redis |

`CacheKeys` (Application) concentra os construtores de chave de `integrations.md` §7, **menos as do filtro de segurança** (`security:*`, da 009). Os formatos estão em RN-010-07.

### 4.2 Compatibilidade de valores com o Python (Redis compartilhado na janela de virada)

O Redis é o mesmo e o Python pode voltar a rodar (rollback). Regras de formato:

| Quem grava | Formato no Redis | O .NET deve |
|---|---|---|
| `set_with_ttl` (Python) | `json.dumps(valor)`: objeto `{"a": 1}`, texto `"abc"` (**com aspas**), número `3`, `true`, `null`; `ensure_ascii` ligado (`ç`, pares substitutos para emoji) | ler com parser JSON (aceita as duas grafias de escape) e gravar JSON válido que o `json.loads` entenda; nomes de propriedade exatamente como o dict do Python (snake_case) |
| `set_if_not_exists` (Python) | valor **cru** (`running`, `processing`), sem JSON | gravar cru; ler só por existência. `GetJsonAsync` de valor cru não-JSON devolve `null` sem lançar (o Python imprime erro e devolve `None`) |
| `security:*` (Python) | JSON **duplamente codificado** (`json.dumps(json.dumps(x))`) | fora desta feature; a 009 usa `SetJsonAsync<string>(key, jsonJaSerializado)`, que produz exatamente esse formato (AC-010-12) |
| `travel_time_limit:*` | `"N"` (texto JSON com aspas, de `set_with_ttl(key, str(n))`) | ler `3` e `"3"` como inteiro 3 (o Python faz `int(json.loads(...))`); gravar `"N"` (010e) |
| `bot_session_id:{nome}` | `"<uuid>"` (texto JSON) | idem (010b) |
| `calendar_*_alert:*`, `invoice_*` | `"1"`, `true` | só existência; .NET grava `"1"`/`true` como o Python |
| `gemini_cache:*` | objeto `{"text","prompt_feedback","cached_at"}` | ler e gravar o mesmo objeto (010c) |

`integrations.md` §7 manda apagar `pending_*`, `nightly_checkin*`, `security:*`, `gemini_cache:*` e as chaves do Limiter na virada; com o formato acima a limpeza de `gemini_cache:*` é conservadora (o .NET lê o que o Python gravou), mas continua válida e inofensiva.

### 4.3 Regras de negócio

- **RN-010-01** `SetJsonAsync` grava com `SETEX` o JSON do valor (equivalente a `json.dumps`). Texto simples vira JSON de texto (com aspas). *Origem: `redis_service.py:47-56` (`:52`).*
- **RN-010-02** `GetJsonAsync` devolve o valor desserializado; chave ausente, valor vazio, texto que não é JSON ou JSON `null` devolvem `null`, sem exceção. *Origem: `redis_service.py:58-67`.*
- **RN-010-03** `SetIfNotExistsAsync` usa uma única operação atômica `SET NX EX` com valor cru e devolve `true` apenas se o Redis respondeu `OK`. Indisponibilidade ou erro devolvem `false` (o chamador decide o que fazer; ver abaixo). *Origem: `redis_service.py:90-110` (`:106-107`).*
- **RN-010-04** `ExistsAsync`: `false` sem Redis. `DeleteAsync`: `true` se não houve erro, inclusive quando a chave não existia. *Origem: `redis_service.py:69-88`.*
- **RN-010-05** `KeysByPatternAsync` aceita glob do Redis e devolve as chaves em ordem **não garantida** (o Python usa `KEYS`, também sem ordem). Consumidores não podem assumir "a mais recente" (o `get_latest_pending_event` da 012 faz isso hoje, `event_confirmation_service.py:82`; a 012 precisa ordenar de outro modo, p. ex. guardando a lista em ordem; não é desvio desta feature). *Origem: `redis_service.py:112-128`.*
- **RN-010-06** **Fail-open e reconexão.** Sem Redis nenhuma operação lança; o aviso é registrado com frequência limitada (no máximo 1 por minuto, não 1 por chamada). A conexão se recupera sozinha quando o Redis volta (o Python faz "lazy reconnect" a cada operação com `socket_connect_timeout=2`). O app **sobe** com o Redis fora (o Python também sobe). Timeout de conexão e de comando: 2 s. Quem precisa distinguir "indisponível" de "falso" usa `IsAvailableAsync` (jobs e alertas: sem Redis executam sem lock, `base_job.py:96-103`, `calendar_alert_service.py:54-57`). *Origem: `redis_service.py:13-45`.*
- **RN-010-07** **Chaves** (`CacheKeys`), strings idênticas às do Python:

  | Construtor | Chave | Observação |
  |---|---|---|
  | `PendingTx(whatsapp, txId)` | `pending_tx:{whatsapp}:{txId}` | |
  | `LastPending(whatsapp)` | `last_pending:{whatsapp}` | |
  | `PendingEvent(whatsapp, eventId)` | `pending_event:{whatsapp}:{eventId}` | padrão de busca `pending_event:{whatsapp}:*` |
  | `NightlyCheckin(whatsapp, id)` / `NightlyCheckinActive(whatsapp)` | `nightly_checkin:{whatsapp}:{id}` / `nightly_checkin_active:{whatsapp}` | |
  | `InvoiceClosed(faturaId)` / `InvoiceDueWarning3d(faturaId)` | `invoice_closed:{id}` / `invoice_due_warning:{id}:3d` | |
  | `CalendarDisconnectedAlert(uid)` / `CalendarTokenRevokedAlert(uid)` | `calendar_disconnected_alert:{uid}` / `calendar_token_revoked_alert:{uid}` | |
  | `CalendarAlert(uid, eventId, start)` | `calendar_alert:{md5hex}` com `md5hex = MD5(UTF8("{uid}:{eventId}:{start}"))` em hexadecimal minúsculo de 32 caracteres | *Origem: `calendar_alert_service.py:35-37`* |
  | `JobLock(nome)` | `job_lock:{nome}` | *`base_job.py:83`* |
  | `TravelTimeLimit(uid, data)` | `travel_time_limit:{uid}:{yyyy-MM-dd}` | |
  | `BotSessionId(nome)` | `bot_session_id:{nome}` | |
  | `GeminiCache(prefixo, uid?, hash12)` | `gemini_cache:{prefixo}:{uid ou "global"}:{hash12}` | |

- **RN-010-08** `REDIS_URL` aceita `redis://host:porta`, `redis://:senha@host:porta/db`, `redis://usuario:senha@host`, `rediss://host` (TLS). Formato inválido derruba o boot **sem imprimir o valor** (pode conter senha). *Origem: `config.py:33`; `integrations.md` §2.*
- **RN-010-09** Logs do cache nunca contêm valores (podem ser dados de transação, número de WhatsApp, texto do Gemini); só a chave com o número de WhatsApp truncado nos 4 últimos dígitos e o tipo do erro (`SafeError.Describe`). O Python imprime erro cru (`redis_service.py:55, 66, 77`).
- **RN-010-10** O `ICache` não conhece o filtro de segurança: nenhuma chave `security:*` é criada ou lida aqui.

### 4.4 O que muda de comportamento (010a)

- Uma operação por chamada (o Python faz `PING` antes de cada operação: 2 viagens).
- `KEYS` vira `SCAN` (não bloqueia o Redis).
- Timeout de comando de 2 s (o Python não tem timeout de comando; podia travar uma requisição).

### 4.5 Critérios de aceite (010a)

- **AC-010-01** Dado `SetJsonAsync("k", {a:1}, 300 s)`, então o Redis guarda um JSON equivalente a `{"a": 1}`, com TTL entre 295 e 300 s.
- **AC-010-02** Dados valores gravados pelo Python (fixtures: `"abc"`, `true`, `3`, `{"text": "ação", "x": [1, 2]}`), então `GetJsonAsync` os lê com os tipos corretos.
- **AC-010-03** Dados chave ausente, valor vazio, texto cru `running` e JSON `null`, então `GetJsonAsync` devolve `null` sem lançar.
- **AC-010-04** Dado `SetIfNotExistsAsync` duas vezes seguidas, então a 1ª devolve `true` e a 2ª `false`; o valor lido por `GET` é o texto cru (sem aspas); o TTL é aplicado. Dadas 50 chamadas concorrentes na mesma chave, exatamente 1 devolve `true`.
- **AC-010-05** `ExistsAsync` reflete a existência; `DeleteAsync` remove e devolve `true`, inclusive para chave inexistente.
- **AC-010-06** `KeysByPatternAsync("pending_event:5531:*")` devolve só as chaves que casam; sem correspondência, lista vazia.
- **AC-010-07** Dado Redis indisponível (porta fechada), então todas as operações devolvem o valor neutro (`false`, `null`, lista vazia) em no máximo ~2 s cada e sem lançar; o aviso é registrado no máximo 1 vez por minuto.
- **AC-010-08** Dado o Redis que volta a responder depois de uma falha, então a operação seguinte funciona sem reiniciar a aplicação.
- **AC-010-09** `REDIS_URL` nas 4 formas de RN-010-08 gera as opções esperadas (host, porta, senha, usuário, banco, TLS); valor inválido falha no boot e a mensagem não contém o valor.
- **AC-010-10** `CacheKeys` devolve as strings da tabela de RN-010-07; vetor: `CalendarAlert(7, "abc", "2026-10-02T10:00:00-03:00")` é `calendar_alert:` seguido do MD5 de `7:abc:2026-10-02T10:00:00-03:00`.
- **AC-010-11** `AddRedisCache` registra `ICache` como singleton e **não conecta no boot**: a API sobe com o Redis fora.
- **AC-010-12** `SetJsonAsync<string>("k", "{\"a\":1}")` grava o texto JSON `"{\"a\":1}"` (codificação dupla do Python) e `GetJsonAsync<string>` devolve `{"a":1}`; base do filtro de segurança da 009.
- **AC-010-13** Valores com acento e emoji fazem ida e volta, e o JSON gravado é interpretado igual por um `json.loads` (vetor literal commitado).

---

## 5. 010b — Cliente do bot WhatsApp

### 5.1 Porta `IBotClient`

```
Task<bool> SendTextAsync(string number, string message, CancellationToken ct)
Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken ct)
```

Devolvem `true` somente se o bot respondeu **HTTP 200**. Nunca lançam por falha de rede. A URL do bot, a chave e o nome da sessão vêm de `AppSettings` (os chamadores do Python passam `BOT_WHATSAPP_URL` e `API_SECRET_KEY` em cada chamada; no .NET ninguém passa nada). Só existe o envio de imagem por **bytes**: `enviar_imagem_whatsapp` (por arquivo, com remoção do arquivo temporário, `notification_service.py:118-167`) não tem chamador e não é portado.

### 5.2 Chamadas de saída

| # | Chamada | URL | Headers | Corpo | Timeout |
|---|---|---|---|---|---|
| 1 | Listar sessões | `GET {BOT_WHATSAPP_URL}/api/v1/sessions` | `X-API-Key: {BOT_API_KEY}` | — | 10 s |
| 2 | Texto (v1) | `POST {url}/api/v1/sessions/{session_id}/send-message` | `X-API-Key: {BOT_API_KEY}`, `Content-Type: application/json` | `{"numero":"…","mensagem":"…"}` | 10 s |
| 3 | Imagem (v1) | `POST {url}/api/v1/sessions/{session_id}/send-image` | idem | `{"numero":"…","imagem":"<base64>","legenda":"…"}` | 30 s |
| 4 | Texto (legado) | `POST {url}/enviar-mensagem` | `x-api-key: {API_SECRET_KEY}`, `Content-Type: application/json` | idem 2 | 10 s |
| 5 | Imagem (legado) | `POST {url}/enviar-imagem` | idem 4 | idem 3 | 30 s |

- Resposta de (1) esperada: `{"status":"success","data":[{"session_id","session_name",...}],"count":N}` (`sessionRoutes.js:98-120`). O Flask só lê `data[].session_name` e `data[].session_id`.
- `imagem` é base64 padrão (com padding) dos bytes; `legenda` é `null` quando não há legenda.
- `numero` e `mensagem` seguem exatamente como o chamador passou (sem normalização).
- Os dois caminhos (v1 e legado) usam `Content-Type: application/json`.

### 5.3 Regras de negócio

- **RN-010-11** **Resolução da sessão.** (a) `BOT_SESSION_ID` definido ⇒ é o id, sem consultar cache nem o bot. (b) Senão, lê `bot_session_id:{BOT_SESSION_NAME}` no cache (texto JSON, ver 4.2); se houver, usa. (c) Senão, `GET /api/v1/sessions`, escolhe a **primeira** com `session_name` **igual** (exato) a `BOT_SESSION_NAME`, grava `session_id` no cache com TTL **600 s** e usa. (d) Sem correspondência: não cacheia, devolve "sem sessão". (e) Qualquer erro (rede, 401, JSON inválido, `raise_for_status`) ⇒ "sem sessão". O `X-API-Key` desta consulta é sempre `BOT_API_KEY`, mesmo vazio (então o bot responde 401 e cai no legado). *Origem: `notification_service.py:12-13, 16-51`.*
- **RN-010-12** **URL e headers.** Com sessão: v1 (`X-API-Key: BOT_API_KEY`). Sem sessão: legado (`x-api-key: API_SECRET_KEY`). *Origem: `notification_service.py:54-67`.*
- **RN-010-13** **Retry em 404.** Se o POST responder **404** e `BOT_SESSION_ID` **não** estiver definido: apaga a chave de cache, resolve de novo **ignorando o cache** (nova consulta ao bot, regrava o cache) e refaz o POST **uma única vez**; a resposta do 2º POST é a que vale (um 2º 404 não gera 3º POST). Vale também para 404 no endpoint legado (o Python refaz a resolução igualmente). Com `BOT_SESSION_ID` definido não há retry. *Origem: `notification_service.py:70-86` (`:79-84`).*
- **RN-010-14** **Sucesso = status 200 exato** (201, 204 etc. contam como falha). O corpo da resposta não é lido para decidir. *Origem: `notification_service.py:106, 150, 195`.*
- **RN-010-15** **Erros.** Qualquer exceção (timeout, conexão recusada, DNS) ⇒ `false`, sem propagar. **Não há retry automático** por falha de rede ou 5xx (reenviar um `send-message` que o bot já aceitou duplicaria a mensagem; o Python também não repete). *Origem: `notification_service.py:113-115, 165-167, 202-204`.*
- **RN-010-16** **Origem da configuração.** `BOT_WHATSAPP_URL`, `BOT_API_KEY`, `BOT_SESSION_NAME`, `BOT_SESSION_ID` e `API_SECRET_KEY` vêm de `AppSettings`. O envio do gráfico (intent da 012) usa este mesmo cliente: o Python usava `WHATSAPP_BOT_URL` com padrão `http://localhost:3003`, variável que não existe (desvio 026). *Origem: `analytics_intents.py:185`.*
- **RN-010-17** `BOT_WHATSAPP_URL` vazia (o `AppSettings` não tem o padrão do Render que o Python tem em `config.py:25`) ⇒ `false`, aviso no log, **nenhuma requisição**. Barra final da URL é removida antes de montar o caminho (o Python não remove; ficaria `//`).
- **RN-010-18** **Logs** (desvio 008): só `status` HTTP, tipo de chamada (`texto`/`imagem`), `session_id` não é segredo e pode ser logado, número com os 4 últimos dígitos. Nunca a mensagem, o corpo da resposta do bot, a chave ou o número completo. O Python imprime número, corpo da resposta e exceção cru (`notification_service.py:107-114`).
- **RN-010-19** (desvio 059 aprovado: `SendTextAsync` divide em partes de até 4096 caracteres, em quebra de linha, e envia em sequência; falha em uma parte interrompe e devolve `false`) O bot recusa `mensagem` com mais de 4096 caracteres (HTTP 400, `messageRoutes.js:46`) e imagens que não sejam JPEG, PNG ou GIF (`:160-167`); o Flask trata como falha (`false`). Sem mudança aqui; ver P-010-7.

### 5.4 O que muda de comportamento (010b)

- Número, mensagem e corpo da resposta deixam de ir ao log.
- `BOT_WHATSAPP_URL` sem padrão e com barra final normalizada.
- Envio de gráfico passa a funcionar (desvio 026).

### 5.5 Critérios de aceite (010b) — fake de HTTP que valida o payload

- **AC-010-20** Dado cache vazio e `BOT_SESSION_ID` vazio, quando enviar texto, então há `GET {url}/api/v1/sessions` com header `X-API-Key = BOT_API_KEY`, e o cache passa a ter `bot_session_id:{nome}` com o id (texto JSON) e TTL 600 s.
- **AC-010-21** Dado o id no cache, então **não** há `GET /api/v1/sessions`.
- **AC-010-22** Dado `BOT_SESSION_ID` definido, então não há consulta ao bot nem leitura/escrita de cache, a URL usa o id, e um 404 **não** causa retry.
- **AC-010-23** Texto: `POST {url}/api/v1/sessions/{id}/send-message`, headers `X-API-Key` e `Content-Type: application/json`, corpo **exatamente** `{"numero":"5531999990000","mensagem":"ola"}` (as duas chaves, nesta ordem); resposta 200 ⇒ `true`.
- **AC-010-24** Imagem: `.../send-image`, corpo com `numero`, `imagem` (base64 dos bytes, decodificável para os mesmos bytes) e `legenda` (texto ou `null`); timeout de 30 s configurado.
- **AC-010-25** Status 201, 204, 400, 401, 500 e 503 ⇒ `false`; o corpo da resposta não aparece em nenhum log.
- **AC-010-26** Dado 404 sem `BOT_SESSION_ID`, então a chave de cache é apagada, há novo `GET /sessions`, e o POST é repetido **uma vez** com o novo id; 200 no 2º ⇒ `true`; 404 de novo ⇒ `false` e **exatamente 2 POSTs**.
- **AC-010-27** Dado que a resolução falhe (GET 401, sem correspondência, rede) ou `BOT_API_KEY` vazia, então o envio cai em `POST {url}/enviar-mensagem` (ou `/enviar-imagem`) com `x-api-key = API_SECRET_KEY` e o mesmo corpo.
- **AC-010-28** Timeout, conexão recusada e exceções ⇒ `false` sem lançar e **sem 2ª tentativa** (1 requisição só).
- **AC-010-29** Sessão sem correspondência não é cacheada: o envio seguinte consulta o bot de novo.
- **AC-010-30** `BOT_WHATSAPP_URL` vazia ⇒ `false`, nenhuma requisição, aviso no log.
- **AC-010-31** URL com barra final gera caminho sem `//`.
- **AC-010-32** Os logs de qualquer cenário não contêm a mensagem, a chave, o corpo da resposta nem o número completo (só os 4 últimos dígitos).
- **AC-010-33** Redis indisponível: o envio funciona, resolvendo a sessão a cada chamada.
- **AC-010-34** (integração, Redis real) O ciclo 404 → apagar → ressolver funciona contra o Redis; o valor gravado é lido por `GET` como `"<uuid>"` (com aspas).

---

## 6. 010c — Gemini (`ILlmClient`)

### 6.1 Portas

- `ILlmClient.GenerateTextAsync(LlmRequest request, ct)` → `string`. `LlmRequest { string Prompt; int? UserId; LlmCachePolicy? Cache; }`. `UserId` nulo = chave do sistema; preenchido = chave resolvida por `IApiKeyProvider` (008). `LlmCachePolicy { string Prefix; TimeSpan Ttl; bool UserSpecific; }`; `Cache` nulo = chamada direta, sem cache.
- `ILlmCacheAdmin`: `InvalidateUserAsync(int userId, string? pattern)`, `InvalidatePatternAsync(string pattern)`, `GetMetrics()` (usado pela 009 em `/admin/gemini-cache-clear` e `/admin/gemini-cache-stats`).
- `LlmJson.Parse(string text)` → `JsonElement` (RN-010-25).
- Exceções (Application): `LlmQuotaExceededException`, `LlmPermissionDeniedException`, `LlmBlockedException`, `LlmKeyUnavailableException` (mensagem = texto do 008 para o usuário), `LlmException` (demais falhas).

O **porte literal dos ~30 prompts não acontece aqui**: o catálogo (apêndice A) diz o que cada um faz; os textos vão para arquivos em `Infrastructure/Llm/Prompts/` junto com a feature **012** (que consome quase todos; a 013 consome o resumo matinal; a 011 a extração de notificação), cada um com teste de snapshot contra o texto do Python.

### 6.2 Chamada de saída

| Item | Valor |
|---|---|
| URL | `POST https://generativelanguage.googleapis.com/v1beta/models/{modelo}:generateContent` |
| Modelo | `gemini-2.5-flash-lite` (padrão, `AppSettings.GeminiModel`/`GEMINI_MODEL` opcional) |
| Headers | `x-goog-api-key: {chave da chamada}`, `Content-Type: application/json` |
| Corpo | `{"contents":[{"role":"user","parts":[{"text":"<prompt>"}]}]}` — sem `generationConfig`, sem `safetySettings` (o Python não envia; usa os padrões do serviço) |
| Resposta lida | `candidates[0].content.parts[].text` (concatenado), `promptFeedback.blockReason`, `error.status`/`error.message` |
| Timeout / retry | 45 s por tentativa, até 1 retry (atraso de 1 s) **só** para HTTP 503, 504, timeout e falha de rede; **nunca** para 400, 403, 429. Teto total de 90 s (o nginx corta em 120 s) |

### 6.3 Regras de negócio

- **RN-010-21** **Modelo, URL, headers e corpo** como na tabela acima; o prompt é enviado byte a byte como o chamador montou. *Origem: `app/__init__.py:150-152`; `gemini_service.py:37, 59`.*
- **RN-010-22** **Chave por chamada.** `UserId` nulo ⇒ `AppSettings.GeminiApiKey`, sem consultar `IApiKeyProvider`. `UserId` preenchido ⇒ `IApiKeyProvider.ResolveAsync(userId, "gemini")`: `Resolved(Key, KeyType)` usa `Key` (própria ou do sistema, tanto faz para a chamada); `Failed(Reason, Message)` lança `LlmKeyUnavailableException(Message)` (o WhatsApp exibe esse texto literal ao usuário, como o `str(e)` do Python). A chave vai **só** no header da requisição daquela chamada. O Flask usa `genai.configure(api_key=...)` **global** (`gemini_service.py:36`): depois que um usuário com chave própria faz uma chamada, as chamadas seguintes com o modelo global do sistema passam a usar a chave dele (a configuração é process-wide). No .NET isso não existe. *Origem: `gemini_service.py:6-41`.*
- **RN-010-23** **A chave é resolvida antes de olhar o cache** (no Python `_obter_modelo_gemini_para_usuario` roda antes de `cached_generate_content`), então o uso mensal (008) é contado mesmo em *cache hit*. *Origem: `gemini_service.py:129-151`.*
- **RN-010-24** **Mapeamento de erros** (replica `safe_generate_content`, que olha o texto da exceção): HTTP 429, `error.status = RESOURCE_EXHAUSTED` ou mensagem contendo `quota` ⇒ `LlmQuotaExceededException("429 Quota exceeded - Gemini API rate limit reached")`; HTTP 403, `PERMISSION_DENIED` ou mensagem contendo `permission` ⇒ `LlmPermissionDeniedException("403 Permission denied - Invalid API key or permissions")`; qualquer outra falha ⇒ `LlmException` com mensagem genérica (código HTTP, sem corpo, sem chave). **As duas mensagens acima são literais** porque o webhook (012) decide o 429 `Limite de IA Excedido` procurando `429`, `quota` ou `rate limit` no texto (`whatsapp_handler.py:1559-1573`). *Origem: `gemini_service.py:44-75`.*
- **RN-010-25** **Texto e JSON.** O texto da resposta é a concatenação das partes de texto do 1º candidato (o SDK Python 0.5.4 exige parte única e levanta `ValueError` com mais de uma). Sem candidato, com `blockReason` ou sem texto ⇒ `LlmBlockedException("Falha na API: Resposta do Gemini bloqueada. <blockReason ou 'sem detalhes'>")`. `LlmJson.Parse` faz `Trim`, remove **todas** as ocorrências de <code>```json</code> e de <code>```</code>, e parseia JSON (como `text.strip().replace("```json","").replace("```","")` + `json.loads`); texto inválido lança `JsonException` (cada função da 012 decide; `categorize_transaction` captura e devolve o fallback "Outros", `gemini_service.py:153-159`). *Origem: `gemini_service.py:78-91` e o padrão repetido em cada função (ex.: `:112-114, 184`).*
- **RN-010-26** **Cache** (`cached_generate_content`). Só quando `Cache` é informado. Passos: valida `UserSpecific` sem `UserId` ⇒ `ArgumentException("user_specific cache requires usuario_id")`; Redis indisponível ⇒ chama direto (bypass); chave `gemini_cache:{Prefix}:{uid|global}:{hash12}` com `hash12` = 12 primeiros hex do **MD5 do prompt em UTF-8** (`MD5("abc")` = `900150983cd24fb0d6963f7d28e17f72` ⇒ `900150983cd2`) e `uid` só se `UserSpecific` e `UserId` > 0; HIT ⇒ devolve o `text` guardado; MISS ⇒ chama, grava `{"text","prompt_feedback","cached_at"}` com `SETEX` e o TTL do tipo, devolve. Erros de cache (leitura ou escrita) são engolidos e contados; erro do Gemini é relançado. Resposta bloqueada ou erro **não** é gravada. A chave **não inclui** o modelo nem a chave de API. *Origem: `gemini_cache_service.py:64-86, 88-121, 123-238`.*
- **RN-010-27** **TTL e quem usa cache.** Só 5 funções usam cache; as outras ~24 chamam direto:

  | Prefixo | TTL | Específico do usuário | Função (apêndice A) |
  |---|---|---|---|
  | `intent` | 604800 s (7 d) | não | `get_message_intent` |
  | `category` | 2592000 s (30 d) | **sim** | `categorize_transaction` |
  | `extract_trans` | 86400 s (24 h) | não | `extract_transaction_details` |
  | `period_query` | 604800 s | não | `extract_period_query` |
  | `chart_type` | 604800 s | não | `extract_chart_type` |
  | padrão (`default`) | 259200 s (3 d) | — | prefixo não listado |

  Os prefixos `calendar_query`, `fatura_query` e `saldo_query` existem na tabela de TTL e nas métricas do Python mas **nenhuma função os usa** (`extract_calendar_query`, `extract_fatura_query`, `extract_saldo_query` chamam direto). *Origem: `gemini_cache_service.py:11-21`; `gemini_service.py:140-149, 358-363, 405-414, 600-607, 661-668`.*
- **RN-010-28** **Métricas por instância** (`ILlmCacheAdmin.GetMetrics`): contadores `hits`, `misses`, `saves`, `errors` (total) e `hits`/`misses` por prefixo (`intent`, `category`, `extract_trans`, `period_query`, `calendar_query`, `chart_type`, `fatura_query`, `saldo_query`, `other`); `hit_rate` como texto `"{n:0.0}%"` com ponto decimal; mesma estrutura do dicionário do Python (`total_requests`, `cache_hits`, `cache_misses`, `cache_saves`, `cache_errors`, `hit_rate`, `breakdown_by_type`, `estimated_savings`). Os contadores são em memória (zeram no restart, como no Python), seguros para uso concorrente. *Origem: `gemini_cache_service.py:313-362`.*
- **RN-010-29** **Invalidação.** `InvalidateUserAsync(uid, pattern)` apaga as chaves `gemini_cache:{pattern}:{uid}:*` (ou `gemini_cache:*:{uid}:*` sem padrão); `InvalidatePatternAsync(pattern)` apaga `gemini_cache:{pattern}:*` (`*` apaga todo o cache). Devolvem a quantidade apagada; Redis fora ⇒ 0. *Origem: `gemini_cache_service.py:240-311`; `presentation/admin/cache_management.py:35-51`.*
- **RN-010-30** **Catálogo dos prompts** (apêndice A) é só documentação nesta feature. Três funções fora de `gemini_service.py` também chamam o modelo (`analytics_service.py:353`, `forecast_service.py:435`, `free_time_finder_service.py:535`), sempre com o modelo global do sistema, sem `usuario_id` e sem cache (P-010-11).
- **RN-010-31** **Logs** (desvio 008): metadados (prefixo do cache, HIT/MISS, milissegundos, tipo do erro, `user_id`). Nunca o prompt, o texto da resposta nem a chave. O Python imprime a resposta (dados financeiros) em cada função (ex.: `gemini_service.py:114`). *Origem: `gemini_cache_service.py:189-225`.*
- **RN-010-32** `AppSettings.GeminiApiKey` (obrigatória) é a chave do sistema. O app não valida a chave no boot.

### 6.4 O que muda de comportamento (010c)

- A chave do usuário nunca vaza para outras requisições (regressão do `genai.configure` global).
- Timeouts (45 s por tentativa, 90 s total) e 1 retry em 503/504/rede; o Python não define timeout próprio.
- Resposta com mais de uma parte de texto é concatenada (o Python 0.5.4 falharia com `ValueError`).
- Respostas e prompts deixam de ir ao log.
- Falhas fora de 429/403 chegam como `LlmException` de mensagem genérica (o Python repassava o texto do SDK).

### 6.5 Critérios de aceite (010c)

- **AC-010-40** A chamada é `POST` na URL de §6.2 com header `x-goog-api-key`, `Content-Type: application/json` e corpo **exatamente** `{"contents":[{"role":"user","parts":[{"text":"<prompt>"}]}]}` (sem outros campos); o prompt chega idêntico, inclusive com acentos, emoji e quebras de linha.
- **AC-010-41** Modelo padrão `gemini-2.5-flash-lite`; `GEMINI_MODEL` altera só o trecho `models/{modelo}` da URL.
- **AC-010-42** Duas chamadas **concorrentes** com chaves de usuários diferentes (e uma terceira do sistema) enviam, cada uma, a **sua** chave; nenhuma requisição leva a chave de outra (regressão do `genai.configure`).
- **AC-010-43** `UserId` nulo usa `GeminiApiKey` sem chamar `IApiKeyProvider`; `UserId` com `Resolved` usa `Key`; `Failed(Reason, Message)` lança `LlmKeyUnavailableException` com `Message` idêntico e **nenhuma requisição HTTP** é feita.
- **AC-010-44** Com cache habilitado, `IApiKeyProvider` é chamado **antes** da consulta ao cache, inclusive em HIT.
- **AC-010-45** Texto de uma parte e de duas partes (concatenado); resposta sem candidatos, com `blockReason: "SAFETY"` e sem texto ⇒ `LlmBlockedException` com a mensagem de RN-010-25.
- **AC-010-46** HTTP 429, e corpo de erro com `status: "RESOURCE_EXHAUSTED"` ou mensagem com `quota`, ⇒ `LlmQuotaExceededException` com a mensagem literal; **uma** requisição só.
- **AC-010-47** HTTP 403 e `PERMISSION_DENIED` ⇒ `LlmPermissionDeniedException` com a mensagem literal.
- **AC-010-48** HTTP 400 (ex.: chave inválida) ⇒ `LlmException`; a mensagem não contém corpo nem chave. HTTP 503 seguido de 200 ⇒ sucesso com 2 requisições; 503 duas vezes ⇒ `LlmException`; timeout de rede ⇒ 1 retry.
- **AC-010-49** Os timeouts configurados (45 s por tentativa, 90 s total) estão no pipeline do cliente (teste de configuração, sem esperar o tempo).
- **AC-010-50** Chave de cache: prompt `abc`, prefixo `intent`, global ⇒ `gemini_cache:intent:global:900150983cd2`; `category` específico do usuário 7 ⇒ `gemini_cache:category:7:<hash12>`; `UserSpecific` sem `UserId` ⇒ `ArgumentException("user_specific cache requires usuario_id")`.
- **AC-010-51** TTL por prefixo da tabela de RN-010-27 aplicado no `SETEX`; prefixo desconhecido usa 259200 s; o valor gravado é o objeto `{"text","prompt_feedback","cached_at"}` (`cached_at` no fuso de São Paulo).
- **AC-010-52** MISS: 1 requisição HTTP, valor gravado, `misses` e `saves` +1. HIT: 0 requisições, devolve o texto guardado, `hits` +1 (total e do prefixo; prefixo fora da lista conta em `other`). Redis fora: chama direto, sem erro.
- **AC-010-53** Resposta bloqueada ou erro do Gemini não é gravada no cache.
- **AC-010-54** Uma entrada gravada pelo Python (fixture com `ç` e `prompt_feedback` vazio) é lida como HIT.
- **AC-010-55** `Cache` nulo: nenhuma chamada ao `ICache`.
- **AC-010-56** Padrões de invalidação, como no Python: `InvalidateUserAsync(7, "category:*")` varre `gemini_cache:category:*:7:*` (o padrão informado é inserido entre `gemini_cache:` e `:{uid}:*`); `InvalidateUserAsync(7, null)` varre `gemini_cache:*:7:*`; `InvalidatePatternAsync("intent:*")` varre `gemini_cache:intent:*:*`; `InvalidatePatternAsync("*")` varre `gemini_cache:*:*`. Cada chamada devolve a quantidade de chaves apagadas; Redis fora ⇒ 0.
- **AC-010-57** `GetMetrics` tem todos os campos de RN-010-28; sem requisições `hit_rate` é `"0.0%"`; após 1 HIT e 1 MISS é `"50.0%"`.
- **AC-010-58** `LlmJson.Parse`: <code>```json\n{"a":1}\n```</code>, `{"a":1}` e `  {"a":1}  ` ⇒ `a = 1`; texto não-JSON ⇒ `JsonException`.
- **AC-010-61** Nenhum log de qualquer cenário contém o prompt, a resposta ou a chave.

---

## 7. 010d — Google (OAuth, Calendar e Drive)

### 7.1 Rotas (a única parte de 010 com HTTP de entrada)

| Método | Caminho | Auth | Resposta |
|---|---|---|---|
| GET | `/connect-calendar/<int:usuario_id>` (`?force=true`) | nenhuma (link enviado por WhatsApp) | 302 / HTML / texto / JSON, §7.4 |
| GET | `/oauth2callback` (`code`, `state`, `error`) | nenhuma (redirect do Google) | HTML / texto, §7.5 |
| POST | `/disconnect-calendar/<int:usuario_id>` | **`x-api-key`** (desvio 003; hoje nenhuma) | envelope **B**, §7.6 |

`<int:usuario_id>` só casa dígitos sem sinal (outro valor ⇒ 404 HTML do Flask; no .NET, constraint `regex(^\d+$)` e id acima de `int.MaxValue` também 404). Não há JWT. Estas três rotas **não** usam `Envelopes.A`: cada ramo devolve o formato listado abaixo. Texto e HTML saem como `text/html; charset=utf-8` (padrão do Flask para `str`).

### 7.2 Portas e componentes

- `IGoogleAccountService` (Application): `GetCredentialsAsync(userId)` → `GoogleCredentialResult` (conectado/não/precisa reconectar); `IsConnectedAsync`, `HasDriveScopeAsync`, `BuildAuthorizationUrl(userId)`, `ExchangeCodeAsync(code, state)` → `userId`, `RevokeAsync(userId)`.
- `IGoogleTokenRepository` (Application; Dapper em `googlecalendartokens`, tokens em texto puro, desvio 029).
- `ICalendarClient`: `ListSelectedCalendarsAsync`, `ListEventsAsync`, `GetEventsInRangeAsync` (helper multi-calendário), `GetEventAsync`, `InsertEventAsync`, `UpdateEventAsync`, `DeleteEventAsync`, `FindEventsByTitleAsync`.
- `IDriveClient`: `UploadFileAsync(userId, bytes, fileName, folderPath, mimeType)`; `DriveFilePolicy.Validate` (puro).
- `GoogleLinks.ConnectUrl(userId)` (puro): `GOOGLE_REDIRECT_URI` sem o último segmento + `/connect-calendar/{userId}` (RN-010-41), usada por 012 e 014 no lugar do `rsplit` repetido em 4 arquivos.
- Formatadores puros (`CalendarFormatter`), equivalentes a `google_calendar_service.py`.

### 7.3 Regras de negócio — OAuth e credenciais

- **RN-010-34** **Escopos e URL de autorização.** Escopos `https://www.googleapis.com/auth/calendar` e `https://www.googleapis.com/auth/drive.file`. URL: `https://accounts.google.com/o/oauth2/auth?` com `client_id`, `redirect_uri` (`GOOGLE_REDIRECT_URI`), `response_type=code`, `scope` (os dois, separados por espaço), `state`, `access_type=offline`, `include_granted_scopes=true`, `prompt=consent`. Sem `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET` ⇒ erro de configuração (§7.4, linha 6). **Não** se envia PKCE (`code_challenge`); ver pergunta 3. *Origem: `google_calendar_oauth_service.py:20-26, 28-73`.*
- **RN-010-35** **State assinado (desvio 021).** `state` = `base64url(payload) + "." + base64url(HMAC-SHA256(payload_b64))`, `payload` = JSON `{"p":"gcal-oauth","u":<usuario_id>,"e":<expiração em segundos Unix>}`, expiração = agora + 10 min, chave = bytes UTF-8 de `API_SECRET_KEY`. A verificação compara a assinatura em tempo constante, exige `p` correto e `e` no futuro. State no formato antigo (base64 do id), adulterado, expirado ou sem assinatura é **inválido** (§7.5, linha 4). O `state` é gerado no clique de `/connect-calendar`, então links já enviados continuam válidos; só fluxos de OAuth **em andamento no instante da virada** (menos de 10 min) falham e precisam reiniciar. A assinatura prova que o `state` foi emitido por nós, **não** que foi o mesmo navegador que o pediu (limite do desvio 021; ver P-010-1). *Origem: `google_calendar_oauth_service.py:63-69, 88`; desvio 021.*
- **RN-010-36** **Troca do código e gravação.** `POST https://oauth2.googleapis.com/token` (formulário): `grant_type=authorization_code`, `code`, `redirect_uri` (`GOOGLE_REDIRECT_URI`), `client_id`, `client_secret`. Resposta ⇒ `access_token`, `refresh_token` (pode faltar), `expires_in`, `scope` (lista separada por espaço). Grava em `googlecalendartokens` (upsert por `usuario_id`): `access_token`; `refresh_token` = o novo **ou o já existente se o novo vier nulo** (`COALESCE(EXCLUDED.refresh_token, refresh_token)`); `token_expiry` = agora UTC + `expires_in`; `scopes` = lista de escopos retornados como **array JSON de textos** (o Python lê com `json.loads`); `needs_reconnect = FALSE`; `updated_at = CURRENT_TIMESTAMP`. Usuário inexistente viola a FK ⇒ erro (§7.5, linha 5). *Origem: `google_calendar_oauth_service.py:75-147` (SQL `:122-134`).*
- **RN-010-37** **Leitura e renovação** (`get_credentials`). Sem linha ⇒ `null` ("não conectado"). Com linha: monta as credenciais; `token_expiry` nulo ⇒ **não expirado**. Expirado (a biblioteca Python considera a margem de 3 min 45 s; o SDK .NET, ~5 min; diferença sem efeito prático) e **com** `refresh_token` ⇒ `POST https://oauth2.googleapis.com/token` com `grant_type=refresh_token`, `refresh_token`, `client_id`, `client_secret`; sucesso ⇒ regrava via RN-010-36 (zera `needs_reconnect`) e devolve as credenciais novas. Falha com `invalid_grant` ⇒ `needs_reconnect = TRUE`, notificação (RN-010-38) e `null`. Falha de renovação com **outro** erro ⇒ `null`, **sem** marcar (pode ser temporário). Expirado **sem** `refresh_token` ⇒ marca (`needs_reconnect = TRUE`), notifica e `null`. A rotina **não** consulta `needs_reconnect` antes de tentar renovar (P-010-6). *Origem: `google_calendar_oauth_service.py:260-412`.*
- **RN-010-38** **`needs_reconnect` e notificação de token revogado.** Marca com `UPDATE googlecalendartokens SET needs_reconnect = TRUE, updated_at = CURRENT_TIMESTAMP WHERE usuario_id`. Depois, **sem propagar erro**: se `calendar_token_revoked_alert:{uid}` existe, não faz nada; senão busca `numero_whatsapp` do usuário (sem número ⇒ para); monta o texto (com o link de reconexão se `GOOGLE_REDIRECT_URI` estiver definido, senão a variante sem link) e envia por `IBotClient`; **só se o envio der certo** grava `calendar_token_revoked_alert:{uid}` (valor `"1"`) com TTL de 7 dias. Texto literal em `google_calendar_oauth_service.py:225-239`. *Origem: `:149-258`.*
- **RN-010-39** **Conectado e escopo do Drive.** `IsConnected` = `GetCredentials` não nulo (**pode renovar o token e gerar a notificação do RN-010-38**: é o comportamento do Flask). `HasDriveScope` = há linha com `scopes` e o array contém `https://www.googleapis.com/auth/drive.file`; `scopes` nulo, sem linha ou qualquer erro ⇒ `false`. *Origem: `:414-420, 474-508`.*
- **RN-010-40** **Revogação.** `RevokeAsync`: obtém as credenciais (pode renovar); se existirem, `POST https://oauth2.googleapis.com/revoke` com `token = access_token` (no .NET como corpo `application/x-www-form-urlencoded`, não na URL; o Python usa `params=`) e timeout de 10 s (o Python não tem timeout); falha do Google é registrada e ignorada. Em seguida `DELETE FROM googlecalendartokens WHERE usuario_id`, sempre. *Origem: `:422-449`.*
- **RN-010-41** **Link de conexão.** `base = GOOGLE_REDIRECT_URI` até antes da última `/`; `connect_url = base + "/connect-calendar/" + usuario_id`; `GOOGLE_REDIRECT_URI` vazio ⇒ sem link (cada consumidor tem a sua variante de texto). O host de `GOOGLE_REDIRECT_URI` precisa servir `/connect-calendar/{id}` e `/oauth2callback` (se hoje é o proxy do Render, `oauth_proxy/app.py` redireciona 302 para o servidor real; ver pergunta 2). *Origem: `calendar_alert_service.py:366-369`, `calendar_query_service.py:257`, `drive_intents.py:210-212, 233-235`.*
- **RN-010-42** **Rota `connect-calendar`:** as verificações seguem a ordem da tabela de §7.4 (banco, usuário, conectado + Drive + `force`, redirecionamento); `is_connected` roda antes de `has_drive_scope` e pode renovar o token (RN-010-39). *Origem: `calendar_handler.py:25-106`.*
- **RN-010-43** **Rota `oauth2callback`:** ordem `error` ⇒ `code`/`state` ⇒ troca ⇒ usuário ⇒ WhatsApp ⇒ HTML (tabela de §7.5); o WhatsApp só sai depois de os tokens estarem gravados. *Origem: `calendar_handler.py:108-317`.*
- **RN-010-44** **Rota `disconnect-calendar`:** `x-api-key` primeiro, depois revogação e `DELETE` (tabela de §7.6); o corpo da requisição é ignorado. *Origem: `calendar_handler.py:319-340`; desvio 003.*
- **RN-010-45** **Tokens em texto puro (desvio 029).** Lidos e gravados sem cifrar, como em produção. Todo acesso passa por `IGoogleTokenRepository`, de modo que cifrar com Fernet depois (leitura dupla, regra do desvio 017) não altere nenhum chamador.

### 7.4 `GET /connect-calendar/<int:usuario_id>`

| # | Condição (nesta ordem) | Resposta |
|---|---|---|
| 1 | falha ao testar o banco (`SELECT 1`) | **503** JSON `{"status":"erro","resposta":"Banco de dados temporariamente indisponivel"}` (note: chave `resposta`) |
| 2 | não existe `usuarios.id = <id>` (`SELECT nome FROM usuarios WHERE id`) | **404** texto `Erro: Usuario nao encontrado` |
| 3 | `is_connected` **e** `has_drive_scope` **e** `?force` diferente de `true` (comparação exata, minúsculas) | **200** HTML "Ja Conectado" (apêndice B.1) |
| 4 | caso contrário (não conectado, conectado sem escopo Drive, ou `force=true`) | **302** com `Location` = URL de autorização (RN-010-34, `state` novo). O corpo é o HTML padrão de redirecionamento do Flask (não é contratual) |
| 5 | usuário conectado **sem** Drive | cai na linha 4 (o Python só registra no log) |
| 6 | qualquer exceção (config OAuth ausente, banco, etc.) | **500** texto `Erro ao conectar` (desvio 008; o Flask devolvia `Erro ao conectar: {str(e)}`, `calendar_handler.py:104-106`) |

*Origem: `calendar_handler.py:25-106`.*

### 7.5 `GET /oauth2callback`

| # | Condição (nesta ordem) | Resposta |
|---|---|---|
| 1 | query `error` presente e não vazia (usuário negou) | **200** HTML "Autorizacao Negada" (B.2), mesmo sem `code` |
| 2 | `code` ou `state` ausente/vazio | **400** texto `Erro: codigo ou state faltando` |
| 3 | troca de código com sucesso | efeitos abaixo e **200** HTML "Conexao Autorizada" com o nome do usuário (B.3) |
| 4 | `state` inválido, adulterado ou expirado (desvio 021) | **500** HTML de erro (B.4) com o texto fixo `link de autorizacao invalido ou expirado`; nenhum token é gravado |
| 5 | qualquer outra exceção (Google recusou o código, banco, FK do usuário) | **500** HTML de erro (B.4) com o texto fixo `erro interno`; nenhum token gravado |

Efeitos da linha 3, nesta ordem: (1) valida o `state` (RN-010-35); (2) troca o código e grava os tokens (RN-010-36); (3) `SELECT nome, numero_whatsapp FROM usuarios WHERE id`; (4) se há usuário **e** número, envia por `IBotClient` o texto literal de B.5 (falha do envio é ignorada e não altera a resposta); (5) responde o HTML com `nome` (ou `Usuario` se o usuário não for encontrado). *Origem: `calendar_handler.py:108-317`.* O Flask coloca `str(e)` dentro de `<code>` na linha 5 (`:313`); no .NET o texto é fixo (desvio 008).

### 7.6 `POST /disconnect-calendar/<int:usuario_id>`

| # | Condição | Resposta |
|---|---|---|
| 1 | header `x-api-key` ausente ou diferente de `API_SECRET_KEY` (desvio 003) | **401** `{"status":"erro","mensagem":"Não autorizado"}` (envelope B, igual ao `require_api_key` do admin, `shared/decorators.py:32-37`); nada é lido nem gravado |
| 2 | sucesso (revoga no Google se houver credencial, apaga a linha; **sem** linha também é sucesso, e usuário inexistente também) | **200** `{"status":"sucesso","mensagem":"Google Calendar desconectado com sucesso"}` |
| 3 | exceção (banco) | **500** `{"status":"erro","mensagem":"Erro ao desconectar Google Calendar"}` (texto fixo, desvio 008; o Flask devolvia `str(e)`) |

O corpo da requisição é ignorado. *Origem: `calendar_handler.py:319-340`; desvio 003.* **Mapeamento de chamadores** (condição do desvio 003): procurei `disconnect-calendar` no Angular (`meusecretariofront/src`), no bot (`bot-appfinanceiro-whatsapp`) e no backend; **não há nenhum chamador**, apenas documentação (`docs/PHASE_B3_COMPLETE.md`). Aplicar a autenticação não quebra cliente conhecido; ver pergunta 5.

### 7.7 Regras de negócio — Calendar, Drive e formatadores

- **RN-010-46** **Erros de "não conectado".** `GetCalendarServiceAsync`/`GetDriveServiceAsync` lançam `GoogleNotConnectedException` com os textos do Python: Calendar sem credencial `Usuário não conectou Google Calendar`; Drive sem escopo `Usuário precisa reconectar a conta Google para autorizar o Google Drive. Acesse as configurações e clique em 'Conectar Google'.`; Drive sem credencial `Usuário não conectou conta Google`. *Origem: `google_calendar_oauth_service.py:451-472, 510-544`.*
- **RN-010-47** **Chamadas ao Calendar** (todas com as credenciais do usuário, fuso `America/Sao_Paulo`):

  | Operação | Chamada à API | Parâmetros / corpo |
  |---|---|---|
  | listar calendários | `calendarList.list` | sem parâmetros (1ª página, padrão 100); o filtro é no cliente: só `selected == true` (`id`, `summary` padrão `Sem nome`, `primary`). *`calendar_query_service.py:305-337`* |
  | listar eventos | `events.list` | `calendarId`, `timeMin`, `timeMax` (RFC 3339 com `-03:00`, ex.: `2026-10-02T00:00:00-03:00` a `2026-10-02T23:59:59.999999-03:00`), `singleEvents=true`, `orderBy=startTime`; busca por título acrescenta `q` e `maxResults` (10 por calendário, janela agora a +90 dias); padrões de comportamento usam `maxResults=500` (`user_schedule_pattern_service.py:75`). 1ª página apenas (padrão 250) |
  | `GetEventsInRange` (helper) | `calendarList.list` + `events.list` por calendário selecionado | reproduz `_get_events_for_date`/`_for_period`: falha ao listar calendários ⇒ usa só `primary` (`summary` `Primary`); erro em **um** calendário ⇒ pula esse calendário; resultado ordenado por `start` (texto) |
  | obter / atualizar / apagar | `events.get`, `events.update` (PUT do corpo inteiro), `events.delete` | `calendarId` padrão `primary` |
  | criar | `events.insert` | corpo abaixo |

  Evento formatado devolvido: `summary` (padrão `Sem título`), `start`/`end` (`dateTime` ou `date`), `location` (`''`), `description` (`''`), `all_day` (`date` presente em `start`), `calendar_name`; ordenado por `start`. *Origem: `calendar_query_service.py:340-421`; `google_calendar_oauth_service.py:561-669`.*
- **RN-010-48** **Corpo do evento criado** (`InsertEventAsync`): `summary`; `description` e `location` só se informados (não vazios); sem `hora_inicio` ⇒ dia inteiro: `start = {"date":"YYYY-MM-DD","timeZone":"America/Sao_Paulo"}`, `end = {"date":"<dia seguinte>","timeZone":"America/Sao_Paulo"}`; com `hora_inicio` ⇒ `start = {"dateTime":"YYYY-MM-DDTHH:mm:ss-03:00","timeZone":"America/Sao_Paulo"}`; `hora_fim` ausente ⇒ `hora_inicio` + 1 h; `end` análogo. O texto de `dateTime` é montado como o `isoformat()` do Python (segundos, sem fração, com `-03:00`), via `DateTimeRaw`. Retorna `id` (e `htmlLink`, ignorado). *Origem: `calendar_management_service.py:16-109`.* Problemas conhecidos em P-010-5.
- **RN-010-49** **Atualização e remoção.** Atualizar: `events.get`; altera `summary` (se truthy), `description` e `location` (se não nulos; `""` limpa); só altera `start`/`end` quando `hora_inicio` é informado (nova data = `data_evento` ou a do evento atual; `hora_fim` ausente = +1 h); `events.update` com o corpo inteiro. Remover: `events.get` para ler o título (falha ⇒ título `Evento`), depois `events.delete`. Mensagem de erro com `not found` ⇒ `Evento não encontrado ou já foi deletado.` (no .NET: HTTP 404 da API). *Origem: `calendar_management_service.py:111-250`.*
- **RN-010-50** **Drive** (`DriveFilePolicy` e `IDriveClient`). Validação, nesta ordem: arquivo vazio ⇒ `Arquivo vazio`; tamanho > 10 MiB (10 × 1024 × 1024 bytes) ⇒ `Arquivo muito grande. Máximo permitido: 10MB`; MIME fora da lista (comparação exata, sensível a maiúsculas) ⇒ `Tipo de arquivo não permitido. Aceitos: imagens (JPG, PNG, GIF), documentos (PDF, DOC, DOCX), planilhas (XLS, XLSX), texto (TXT, CSV), áudio (MP3, OGG)`. MIME aceitos (17): `image/jpeg`, `image/png`, `image/gif`, `image/webp`, `image/bmp`, `application/pdf`, `application/msword`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.ms-excel`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.ms-powerpoint`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `text/plain`, `text/csv`, `audio/mpeg`, `audio/ogg`, `audio/opus`. *Origem: `google_drive_service.py:37-100`.*
- **RN-010-51** **Pastas e upload.** Para cada segmento de `folder_path` (texto único vira lista de 1): `files.list` com `q = name = '<nome com ' escapado como \'>' and mimeType = 'application/vnd.google-apps.folder' and trashed = false` (+ ` and '<parentId>' in parents` a partir do 2º segmento), `spaces=drive`, `fields=files(id, name)`, `pageSize=1`; falha na busca ⇒ trata como "não achou"; senão `files.create` com `{"name": <nome>, "mimeType": "application/vnd.google-apps.folder"}` (+ `"parents": [<parentId>]`), `fields=id`. Upload: `files.create` com `{"name": <arquivo>, "parents": [<idDaPastaFinal>]}`, conteúdo `media` (upload retomável) com o MIME informado, `fields=id, name, webViewLink, webContentLink`. Resultado: `success`, `file_id`, `filename`, `folder_name` (último segmento), `folder_path`, `folder_path_display` (segmentos unidos por ` > `), `folder_id`, `web_view_link`, `web_content_link` (pode ser nulo). *Origem: `google_drive_service.py:102-326`.*
- **RN-010-52** **Mapeamento de erro do upload** (`success=false`): permissão (403 `insufficientPermissions`/`forbidden`) ⇒ `Sem permissão para acessar o Google Drive. Por favor, reconecte sua conta Google.`; cota (`storageQuotaExceeded` ou razão contendo `quota`) ⇒ `Limite de armazenamento do Google Drive atingido.`; credenciais (401, `invalid_grant`) ⇒ `Credenciais do Google expiraram. Por favor, reconecte sua conta.`; demais ⇒ `Erro ao fazer upload.` (texto fixo, desvio 008; o Python devolvia `Erro ao fazer upload: {str(e)}`). O Python decide por substring do texto da exceção (`google_drive_service.py:333-357`), que não existe no SDK .NET; o mapeamento acima é por código HTTP e razão da API. Ver pergunta 7.
- **RN-010-53** **Formatadores** (`CalendarFormatter`, puros, com `IClock` no lugar de `date.today()`): `StripHtml`, `FormatTime`, `FormatEventsForWhatsApp(events, date)`, `FormatPeriodEventsForWhatsApp(eventsByDate, start, end)` reproduzem `google_calendar_service.py:14-185` literalmente (emojis, `━` ×20, dias da semana `Segunda-feira`… e `Seg`…, plurais `compromisso(s)`/`dia(s)`). *Origem: `google_calendar_service.py:14-185`.*
- **RN-010-54** **Timeouts e retry do Google.** `HttpClient` do SDK com timeout de 60 s (o `googleapiclient` usa 60 s quando não há padrão de socket); retry apenas o padrão do SDK .NET para 503 com recuo exponencial (o Python faz `execute()` sem `num_retries`). Erros do SDK nunca vão ao log com URL ou token.

### 7.8 O que muda de comportamento (010d)

- `state` assinado e com validade de 10 min (desvio 021).
- `POST /disconnect-calendar` passa a exigir `x-api-key` (desvio 003).
- Erros 500 deixam de expor `str(e)` (rotas e upload do Drive).
- Revogação no Google com timeout de 10 s e token no corpo (não na URL).
- `GoogleLinks.ConnectUrl` único em vez de 4 cópias.

### 7.9 Critérios de aceite (010d)

**Rotas e OAuth**
- **AC-010-70** `connect-calendar` com falha de banco ⇒ 503 com o JSON exato de §7.4 linha 1; usuário inexistente ⇒ 404 `Erro: Usuario nao encontrado`.
- **AC-010-71** Usuário conectado com escopo Drive e sem `force` ⇒ 200 com o HTML B.1 **idêntico** ao do Python (comparação do corpo inteiro com a fixture).
- **AC-010-72** Usuário não conectado ⇒ 302; `Location` começa por `https://accounts.google.com/o/oauth2/auth?` e a query tem `client_id`, `redirect_uri`, `response_type=code`, `scope` (os dois escopos separados por espaço), `state`, `access_type=offline`, `include_granted_scopes=true`, `prompt=consent` e **não** tem `code_challenge`.
- **AC-010-73** Usuário conectado sem escopo Drive, ou com `?force=true`, ⇒ 302; `?force=TRUE` e `?force=1` **não** forçam (continuam em 200 HTML).
- **AC-010-74** Sem `GOOGLE_CLIENT_ID`/`GOOGLE_CLIENT_SECRET` ⇒ 500 `Erro ao conectar`, sem texto de exceção.
- **AC-010-75** `state`: round trip devolve o `usuario_id`; adulterado (1 caractere), assinado com outra chave, expirado (relógio falso +10 min e 1 s), formato antigo (base64 do id) e sem ponto ⇒ inválido; assinatura comparada em tempo constante.
- **AC-010-76** `oauth2callback?error=access_denied` ⇒ 200 HTML B.2; `error` presente vence a falta de `code`; sem `code` ou sem `state` ⇒ 400 `Erro: codigo ou state faltando`.
- **AC-010-77** Callback com sucesso: o fake do Google recebe `POST /token` com `grant_type=authorization_code`, `code`, `redirect_uri`, `client_id`, `client_secret` (formulário); a linha de `googlecalendartokens` fica com `access_token`, `refresh_token`, `token_expiry` (≈ agora + `expires_in`), `scopes` (array JSON), `needs_reconnect = false`; o fake do bot recebe o texto B.5 para o número do usuário; resposta 200 com HTML B.3 contendo o nome.
- **AC-010-78** Reconexão sem `refresh_token` na resposta mantém o `refresh_token` antigo; com um novo, substitui; `needs_reconnect` volta a `false`.
- **AC-010-79** Usuário sem número de WhatsApp ⇒ nenhuma chamada ao bot, resposta 200; falha do bot (500) ⇒ resposta continua 200.
- **AC-010-80** `state` inválido ou expirado ⇒ 500 HTML B.4 com `link de autorizacao invalido ou expirado`, **nenhuma** chamada ao Google e nenhuma linha gravada; código recusado pelo Google ⇒ 500 HTML B.4 com `erro interno`, sem gravar; nenhuma página contém texto de exceção, token ou segredo.
- **AC-010-81** `disconnect-calendar` sem `x-api-key` ou com chave errada ⇒ 401 `{"status":"erro","mensagem":"Não autorizado"}` e a linha **não** é apagada; com a chave: usuário conectado ⇒ fake do Google recebe `POST /revoke` com `token=<access_token>` no corpo, a linha é apagada, 200 com a mensagem literal; usuário sem linha ⇒ 200; falha do Google no revoke ⇒ linha apagada e 200; erro de banco ⇒ 500 `Erro ao desconectar Google Calendar`.
- **AC-010-82** Rotas: `/connect-calendar/abc`, `/connect-calendar/-1` e `/connect-calendar/1/` ⇒ 404 HTML do Flask; `POST /connect-calendar/1` ⇒ 405.

**Credenciais**
- **AC-010-83** Sem linha ⇒ não conectado, sem chamada ao Google. Token não expirado ⇒ credenciais sem chamada ao Google. `token_expiry` nulo ⇒ não expirado.
- **AC-010-84** Token expirado com `refresh_token`: o fake recebe `POST /token` com `grant_type=refresh_token`, `refresh_token`, `client_id`, `client_secret`; a linha é regravada (novo `access_token`, `token_expiry`, `needs_reconnect = false`).
- **AC-010-85** Renovação com `invalid_grant` ⇒ `null`, `needs_reconnect = true`, notificação enviada; renovação com 500 do Google ⇒ `null` **sem** marcar e sem notificar; expirado sem `refresh_token` ⇒ `null`, marcado, notificado.
- **AC-010-86** Notificação de token revogado: chave `calendar_token_revoked_alert:{uid}` existente ⇒ não envia; usuário sem número ⇒ não envia; envio ok ⇒ grava a chave (`"1"`, TTL 7 d); envio falhou ⇒ não grava; o texto tem a variante com link quando `GOOGLE_REDIRECT_URI` está definido e a variante sem link quando vazio; exceções internas não propagam.
- **AC-010-87** `HasDriveScope`: array com `drive.file` ⇒ `true`; só `calendar` ⇒ `false`; `scopes` nulo, sem linha ou JSON inválido ⇒ `false` sem lançar.
- **AC-010-88** `GoogleLinks.ConnectUrl(7)` com `GOOGLE_REDIRECT_URI = https://h.exemplo/oauth2callback` ⇒ `https://h.exemplo/connect-calendar/7`; `GOOGLE_REDIRECT_URI` vazio ⇒ sem link.
- **AC-010-89** Tokens e `client_secret` nunca aparecem em log nem em mensagem de exceção.

**Calendar, Drive e formatadores**
- **AC-010-90** `events.list`: o fake vê `timeMin`/`timeMax` RFC 3339 com `-03:00`, `singleEvents=true`, `orderBy=startTime`; com título, também `q` e `maxResults=10`.
- **AC-010-91** `GetEventsInRange`: falha em `calendarList` ⇒ consulta só `primary`; erro em 1 de 2 calendários ⇒ eventos do outro; só calendários `selected` são consultados; resultado ordenado por `start`; os campos do evento seguem RN-010-47 com os padrões `Sem título`, `''`, `all_day`.
- **AC-010-92** `InsertEventAsync` envia exatamente o corpo de RN-010-48 nos 3 casos (dia inteiro; com início; com início e fim), com `dateTime` no formato `2025-11-20T07:00:00-03:00`.
- **AC-010-93** Atualizar com título e sem horário não altera `start`/`end`; com `hora_inicio` e sem `hora_fim` termina 1 h depois; remover evento inexistente (404) ⇒ resultado `não encontrado`.
- **AC-010-94** Drive: arquivo vazio, de 10 MiB + 1 byte, e MIME `image/jpeg; charset=x` ⇒ as 3 mensagens de RN-010-50; os 17 MIME aceitos passam.
- **AC-010-95** Drive: busca `files.list` com o `q` exato de RN-010-51 (nome com `'` escapado), criação de pasta com o corpo exato, upload com `{"name","parents":[id]}` e `fields=id, name, webViewLink, webContentLink`; caminho `["IR","IR 2026"]` busca/cria a 1ª pasta e a 2ª com `parents`.
- **AC-010-96** Drive: 403 de permissão, cota, 401 e erro desconhecido ⇒ as 4 mensagens de RN-010-52; a do erro desconhecido não contém o texto da exceção.
- **AC-010-97** `CalendarFormatter`: saídas idênticas às do Python para (a) dia sem eventos hoje/amanhã/outra data, (b) dia com evento de horário, de dia inteiro, com local e com descrição HTML, (c) período com 2 dias; `StripHtml` e `FormatTime` com os casos `…Z`, `…+00:00`, `…-03:00`, sem `T`, vazio (fixtures geradas pelo Python).
- **AC-010-98** `GetCalendarServiceAsync`/`GetDriveServiceAsync` lançam `GoogleNotConnectedException` com os textos de RN-010-46.

---

## 8. 010e — Clima e rotas

### 8.1 Portas

- `IWeatherClient`: `GetWeatherAsync(city, state?, userId?)` → `WeatherInfo?`; `GetForecastAsync(city, state?, days, userId?)` → `ForecastInfo?`; `WeatherFormatter.FormatForBriefing(WeatherInfo)` (puro).
- `IRouteClient`: `GeocodeAsync(address, userId?)` → `GeocodeResult?` (`Lat`, `Lon`, `Label`); `CalculateTravelTimeAsync(origin, destination, userId?)` → `TravelResult?` (`DurationMinutes`, `DistanceKm`, `RouteSummary`).
- `ITravelRateLimiter`: `CheckAsync(userId)` → `(Allowed, Remaining, Used)`; `IncrementAsync(userId)` → `bool`.
- `IProviderKeyValidator`: `ValidateAsync(provider, key)` → `KeyValidation` (`Valid`, `Invalid`, `NotSupported`); indisponibilidade lança `ProviderUnavailableException` (a 007 responde 503).

`userId` nulo = chave do sistema (`AppSettings.WeatherApiKey`/`OpenRouteApiKey`). Hoje todos os chamadores chamam sem usuário (`daily_briefing_service.py:21`, `whatsapp_handler.py:209-226`, `user_address_service.py:48`), então só a chave do sistema é usada na prática; o parâmetro existe porque o Python tem o caminho com `usuario_id`.

### 8.2 Chamadas de saída

| # | Chamada | URL | Headers | Corpo | Timeout / retry |
|---|---|---|---|---|---|
| 1 | Clima atual | `GET https://api.weatherapi.com/v1/current.json?key={chave}&q={local}&lang=pt` (**HTTPS**, desvio 022) | — | — | 5 s, sem retry |
| 2 | Previsão | `GET https://api.weatherapi.com/v1/forecast.json?key={chave}&q={local}&days={min(dias,3)}&lang=pt` | — | — | 5 s, sem retry |
| 3 | Validar chave Gemini | `GET https://generativelanguage.googleapis.com/v1beta/models` | `x-goog-api-key: {chave}` | — | 5 s |
| 4 | Validar chave clima (desvio 019) | `GET https://api.weatherapi.com/v1/current.json?key={chave}&q=London` | — | — | 5 s |
| 5 | Validar chave rotas | `GET https://api.openrouteservice.org/v2/directions/driving-car` | `Authorization: {chave}` | — | 5 s |
| 6 | Geocode | `GET {OPENROUTE_BASE_URL}/geocode/search?api_key={chave}&text={endereço}&boundary.country=BR&size=1` | — | — | 15 s, 2 tentativas |
| 7 | Rota | `POST {OPENROUTE_BASE_URL}/v2/directions/driving-car` | `Authorization: {chave}`, `Content-Type: application/json` | `{"coordinates":[[lonOrigem,latOrigem],[lonDestino,latDestino]]}` | 15 s, 2 tentativas |

### 8.3 Regras de negócio

- **RN-010-55** **Clima atual** (`get_weather`). Sem chave ⇒ `null` sem requisição. `q` = `"{cidade},{estado}"` se há estado; sem estado e sem vírgula na cidade ⇒ `"{cidade},Brazil"`; sem estado e com vírgula ⇒ a cidade como veio. Status diferente de 200 ⇒ `null`. Dados: `condition.text` (padrão `Desconhecido`), `temp_c`, `feelslike_c` e `humidity` (padrão 0) **truncados** para inteiro (`int()` do Python trunca: 24,9 ⇒ 24; -0,5 ⇒ 0). `condicao, emoji` = tradução de `condition.text` pelo mapa literal de 22 entradas (`weather_service.py:18-54`; chaves **em inglês**: `Sunny`, `Clear`, `Partly cloudy`…), com padrão `(texto da API, U+1F321 U+FE0F)`. `descricao_completa` = `"{temp}°C, {condicao} {emoji}"`. `chance_chuva` = `null` sempre. Resultado: `temperatura`, `condicao`, `emoji`, `sensacao_termica`, `umidade`, `chance_chuva`, `descricao_completa`. Qualquer exceção (timeout, rede, JSON) ⇒ `null` e log sem a URL (contém a chave). **Efeito colateral conhecido:** como a requisição envia `lang=pt`, a API devolve o texto em português, que **nunca** casa com as chaves em inglês; na prática o emoji é sempre o termômetro (P-010-2). *Origem: `weather_service.py:83-173`.*
- **RN-010-56** **Previsão** (`get_forecast`). Sem chave ⇒ `null`. `q` = `"{cidade},{estado}"` ou `"{cidade},Brazil"`; `days = min(dias, 3)`. Status ≠ 200, `forecastday` vazio ou qualquer exceção ⇒ `null`. Resultado: `chance_chuva` (`day.daily_chance_of_rain`, inteiro), `temp_max` (`maxtemp_c`), `temp_min` (`mintemp_c`), truncados. **Nenhum chamador usa a previsão hoje.** *Origem: `weather_service.py:175-221`.*
- **RN-010-57** **Formatação para o resumo matinal.** `descricao` vazia/nula ⇒ `""`; senão `"<termômetro> Clima: {descricao_completa}"`; acrescenta ` (sensação de {sensacao}°C)` se `|temperatura - sensacao| >= 3`; se `chance_chuva` não é nula: `>= 60` ⇒ `\n` + `Alta chance de chuva ({n}%)` com os emojis do original, `>= 30` ⇒ `\n` + `Possível chuva ({n}%)`. Emojis e textos literais de `weather_service.py:236-248`.
- **RN-010-58** **Chave do clima e HTTPS.** `userId` nulo ⇒ `AppSettings.WeatherApiKey` (vazia ⇒ clima desativado com aviso no log). `userId` preenchido ⇒ `IApiKeyProvider.ResolveAsync(userId, "weather")`; `Failed` ⇒ trata como sem chave (`null`, log do motivo sem a chave; é o que o Python faz, `weather_service.py:74-76`). A chave vai na query (WeatherAPI não aceita header), por isso a URL **nunca** é registrada em log. Todas as chamadas ao WeatherAPI usam **HTTPS** (desvio 022). *Origem: `weather_service.py:56-81`.*
- **RN-010-59** **Validação de chave** (`IProviderKeyValidator`, usada por `POST /api/user/api-keys/validate` da 007). `gemini`: válida se status **200** (o Python põe a chave na URL; o .NET usa o header `x-goog-api-key`, equivalente). `weather` (**desvio 019**): válida se status **diferente de 401** (mesma regra que o Python aplicava ao OpenWeatherMap; ver pergunta 6). `openroute`: válida se status **diferente de 403**; a URL é fixa `https://api.openrouteservice.org` (não usa `OPENROUTE_BASE_URL`), como no Python. Provedor fora desses 3 ⇒ `NotSupported` (a 007 responde 400 `Provedor não suportado para validação`). Timeout de 5 s; exceção de rede/timeout ⇒ `ProviderUnavailableException` (a 007 responde 503 `Não foi possível validar (timeout ou erro de rede)`). *Origem: `api.py:2630-2672`; desvio 019.*
- **RN-010-60** **Geocode.** Chave: `userId` nulo ⇒ `AppSettings.OpenRouteApiKey`; preenchido ⇒ `IApiKeyProvider.ResolveAsync(userId, "openroute")` e `Failed` **propaga** como `ApiKeyUnavailableException` (o Python relança, `travel_time_service.py:37-39`); chave vazia ⇒ `null`. Resposta: sem `features` ⇒ `null` (sem retry); `coordinates = features[0].geometry.coordinates` como `[lon, lat]`; `label = properties.label` (padrão: o endereço pedido); coordenadas fora de `-33 <= lat <= 5` e `-74 <= lon <= -34` ⇒ `null`. Retorna `(lat, lon, label)`. *Origem: `travel_time_service.py:20-118`.*
- **RN-010-61** **Rota (directions).** Mesma regra de chave. Resposta: sem `routes` ⇒ `null` (sem retry); `summary.duration` (segundos) ⇒ `duration_minutes = int(duration / 60)` (**trunca**); `summary.distance` (metros) ⇒ `distance_km = round(distance / 1000, 1)` (arredondamento para o par mais próximo, como o `round` do Python); `route_summary` = `name` do 1º `step` do 1º `segment` (`''` se o passo não tem `name`), `null` se não há segmentos ou passos. *Origem: `travel_time_service.py:120-214`.*
- **RN-010-62** **Retry do OpenRoute** (geocode e rota): até **2 tentativas**, sem espera entre elas, para timeout, erro de conexão **e qualquer status não-2xx** (o Python usa `raise_for_status`, então 400/401/403/404/429/5xx também são repetidos; ver P-010-12); depois da 2ª falha ⇒ `null`. Exceção de outro tipo (JSON inválido, campo ausente) ⇒ `null` na hora, sem repetir. *Origem: `travel_time_service.py:73-118, 158-214`.*
- **RN-010-63** **Limite diário de rotas** (`ITravelRateLimiter`). Chave `travel_time_limit:{uid}:{yyyy-MM-dd}`; o valor é lido pelo `ICache` aceitando `3` e `"3"`; `Remaining = TRAVEL_TIME_DAILY_LIMIT - Used`; `Allowed = Remaining > 0`; qualquer erro ⇒ `(true, 10, 0)` (o `10` é fixo, não o limite configurado); sem Redis ⇒ `Used = 0` (permite). `Increment`: lê, soma 1 e grava com `SETEX` o texto JSON `"N"` e TTL **86400 s**; devolve se gravou. A data do Python vem de `datetime.now()` do container, **não** do fuso de São Paulo; o .NET usa `IClock.Today` (P-010-3). **Nenhum código do Flask chama `check_rate_limit` nem `increment_rate_limit`**: o limite não é aplicado hoje; o limitador é portado para quem decidir aplicá-lo (P-010-3). *Origem: `travel_time_service.py:471-533`.*
- **RN-010-64** **Não portados** (sem chamador e quebrados): `get_next_event_after`, `suggest_alternative_time` e `check_conflict` (`travel_time_service.py:216-469`); as duas primeiras chamam `CalendarManagementService.get_events_by_date_range`, **que não existe**, e por isso sempre caem no retorno vazio/fallback fixo (P-010-4).
- **RN-010-65** Logs sem chave, URL, endereço completo nem coordenadas do usuário (só o tipo do erro e o status HTTP).

### 8.4 O que muda de comportamento (010e)

- WeatherAPI por HTTPS (desvio 022); validação de chave `weather` contra WeatherAPI (desvio 019).
- Data do limite diário em São Paulo (se o limitador for usado).
- Logs sem URL, endereço, coordenadas ou chave.

### 8.5 Critérios de aceite (010e)

- **AC-010-110** Clima atual: o fake vê `GET https://api.weatherapi.com/v1/current.json` com `key`, `q` e `lang=pt` (esquema **https**), timeout de 5 s; `q` = `São Paulo,SP` com estado; `Campinas,Brazil` sem estado; `Foo, Bar` (vírgula, sem estado) permanece como veio.
- **AC-010-111** Resposta `{"current":{"condition":{"text":"Sunny"},"temp_c":24.9,"feelslike_c":-0.5,"humidity":65}}` ⇒ `temperatura = 24`, `sensacao_termica = 0`, `umidade = 65`, `condicao = "Ensolarado"`, `descricao_completa` = `24°C, Ensolarado <emoji do sol>`, `chance_chuva = null`; texto `Ensolarado` (já em português) ⇒ `condicao = "Ensolarado"` com o termômetro (documenta o desvio P-010-2); texto ausente ⇒ `Desconhecido` com o termômetro.
- **AC-010-112** Status 401/500, timeout e JSON inválido ⇒ `null` sem lançar; sem chave ⇒ `null` e **nenhuma** requisição; nenhum log contém a chave ou a URL.
- **AC-010-113** Previsão: `days = min(dias, 3)` (`5` ⇒ `3`); `q` com `,Brazil` quando sem estado; `forecastday` vazio ⇒ `null`; resultado com `chance_chuva`, `temp_max`, `temp_min` inteiros.
- **AC-010-114** `WeatherFormatter`: a sensação térmica aparece só com diferença >= 3 (`24` e `26` não; `24` e `27` sim); chance de chuva `29` ⇒ nada; `30` ⇒ linha `Possível chuva (30%)`; `59` ⇒ idem com `59`; `60` ⇒ linha `Alta chance de chuva (60%)`; `null` ⇒ nada; `descricao` vazia ⇒ `""`.
- **AC-010-115** Chave por usuário: `userId` com `Resolved` usa a chave devolvida na query; `Failed` ⇒ `null` sem requisição.
- **AC-010-116** Validação: gemini 200 ⇒ válida e 401/403/400 ⇒ inválida, com `x-goog-api-key` e **sem** `key=` na URL; weather: 200 e 403 ⇒ válida, 401 ⇒ inválida, URL em `api.weatherapi.com` com `q=London`; openroute: 200/401 ⇒ válida, 403 ⇒ inválida, com header `Authorization`; provedor `openai` ⇒ `NotSupported`; timeout/rede ⇒ `ProviderUnavailableException`.
- **AC-010-117** Geocode: `GET {base}/geocode/search` com `api_key`, `text`, `boundary.country=BR`, `size=1`; `features` vazio ⇒ `null` com **1** requisição; coordenada fora do Brasil ⇒ `null`; resposta válida ⇒ `(lat, lon, label)` com `[lon, lat]` lido na ordem certa; `label` ausente ⇒ o endereço.
- **AC-010-118** Rota: `POST {base}/v2/directions/driving-car`, header `Authorization: {chave}`, corpo **exatamente** `{"coordinates":[[-46.6,-23.5],[-46.7,-23.6]]}` (lon, lat); `duration 2759` ⇒ 45 minutos (trunca, não arredonda para 46); `distance 12549` ⇒ `12.5`; arredondamento de metade para o par mais próximo como o `round` do Python (`12450` ⇒ `12.4`, `12550` ⇒ `12.6`, valores exatos em float); `routes` vazio ⇒ `null` com **1** requisição; `route_summary` do 1º passo.
- **AC-010-119** Retry: 500 seguido de 200 ⇒ sucesso com 2 requisições; 500 duas vezes ⇒ `null`; 403 duas vezes ⇒ `null` com **2** requisições (paridade, P-010-12); timeout duas vezes ⇒ `null`; JSON inválido ⇒ `null` com 1 requisição; `userId` com `Failed` ⇒ `ApiKeyUnavailableException`, nenhuma requisição.
- **AC-010-120** `CheckAsync` com `Used` 9 e limite 10 ⇒ `(true, 1, 9)`; `Used` 10 ⇒ `(false, 0, 10)`; valor `"3"` e `3` ⇒ `Used = 3`; chave ausente ou Redis fora ⇒ `Used = 0` permitido; erro interno ⇒ `(true, 10, 0)`.
- **AC-010-121** `IncrementAsync` grava `"N"` (texto JSON) com TTL 86400 s na chave com a data de **São Paulo** do relógio falso (23:30 de São Paulo, que já é o dia seguinte em UTC, usa a data de São Paulo); devolve `false` sem Redis.
- **AC-010-122** Nenhum log de cenário algum contém chave, URL com `key=`/`api_key=`, endereço ou coordenadas.

---

## 9. Variáveis de ambiente

Nomes de `integrations.md` §2 e `AppSettings.cs`. **Todas as de integração já existem em `AppSettings`**; só uma é nova.

| Variável | Em `AppSettings` | Padrão no .NET | Diferença para o Flask | Usada por |
|---|---|---|---|---|
| `REDIS_URL` | `RedisUrl` (obrigatória) | — | Flask: padrão `redis://localhost:6379` | 010a |
| `BOT_WHATSAPP_URL` | `BotWhatsappUrl` | vazio | Flask: padrão `https://bot-appfinanceiro-whatsapp.onrender.com` (obsoleto); no .NET vazio ⇒ envio desligado (RN-010-17) | 010b |
| `BOT_SESSION_NAME` | `BotSessionName` | `sessao-principal` | igual | 010b |
| `BOT_SESSION_ID` | `BotSessionId` | vazio | igual | 010b |
| `BOT_API_KEY` | `BotApiKey` | vazio | igual | 010b |
| `API_SECRET_KEY` | `ApiSecretKey` | obrigatória | também assina o `state` do OAuth (010d) e é o `x-api-key` do fallback legado do bot | 010b, 010d |
| `GEMINI_API_KEY` | `GeminiApiKey` (obrigatória) | — | igual | 010c |
| **`GEMINI_MODEL`** | **falta** (`GeminiModel`) | `gemini-2.5-flash-lite` | novo, opcional (`tech-stack.md`: "modelo configurável") | 010c |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET` | `GoogleClientId`, `GoogleClientSecret` | vazio | igual | 010d |
| `GOOGLE_REDIRECT_URI` | `GoogleRedirectUri` | vazio | Flask: padrão `https://app-controle-financeiro-oh32.onrender.com/oauth2callback`; no .NET vazio ⇒ OAuth sem URI e links sem host (confirmar o valor real, pergunta 2) | 010d |
| `WEATHER_API_KEY` | `WeatherApiKey` | vazio | igual; vazio ⇒ clima desligado | 010e |
| `OPENROUTE_API_KEY` | `OpenRouteApiKey` | vazio | igual | 010e |
| `OPENROUTE_BASE_URL` | `OpenRouteBaseUrl` | `https://api.openrouteservice.org` | igual | 010e |
| `TRAVEL_TIME_DAILY_LIMIT` | `TravelTimeDailyLimit` | 10 | igual | 010e |

Constantes (sem variável de ambiente): URLs do Gemini, WeatherAPI e Google (OAuth, revoke); timeouts de §3.

---

## 10. Desvios aprovados e como se aplicam

| Desvio | Como se aplica nesta feature |
|---|---|
| **021** State do OAuth assinado | RN-010-35 e AC-010-75, 80. HMAC-SHA256 com `API_SECRET_KEY`, validade de 10 min, propósito `gcal-oauth` no payload. Limite: não amarra o navegador (P-010-1). |
| **022** WeatherAPI por HTTPS | RN-010-55, 56, 58 e AC-010-110: todas as URLs do WeatherAPI são `https://api.weatherapi.com/v1/...`. |
| **019** Validar chave `weather` contra WeatherAPI | RN-010-59 e AC-010-116: a validação do provedor `weather` chama o WeatherAPI, não o OpenWeatherMap. A regra de status está na pergunta 6. |
| **026** URL do bot no gráfico | RN-010-16: o intent de gráfico (012) usa `IBotClient`; não há mais `WHATSAPP_BOT_URL`. |
| **029** Tokens do Google em texto puro | RN-010-45 e RN-010-36/37: lê e grava texto puro; acesso encapsulado em `IGoogleTokenRepository` para cifrar depois sem tocar chamadores. A aceitação de leitura nos dois formatos não é implementada agora. |
| **008** Sem `str(e)` nem segredos em log | RN-010-09, 18, 31, 65 e §3: textos fixos nos 500 (`Erro ao conectar`, página B.4, `Erro ao desconectar Google Calendar`, `Erro ao fazer upload.`); logs só com `SafeError.Describe`, `user_id` e status HTTP. |
| **003** Rotas sem auth | RN-010-44, §7.6 e AC-010-81: `POST /disconnect-calendar/{id}` exige `x-api-key` (via `RequireApiKey` da 008-0, com o envelope B de 401 do admin). Condição de mapear chamadores: feito, nenhum chamador (pergunta 5). `/calendar-alerts/*` é da 014. |

Também valem o **024** (corpo sem `Content-Type`: as três rotas não leem corpo, nada a tratar) e o **005** (a whitelist é derivada das rotas registradas; `/connect-calendar`, `/oauth2callback` e `/disconnect-calendar` já estão na whitelist do Python, `security.py:87-89`).

---

## 11. Tabelas e dados tocados

| Tabela / recurso | Operação | Observação |
|---|---|---|
| `googlecalendartokens` | SELECT, upsert, UPDATE `needs_reconnect`, DELETE | `usuario_id` UNIQUE; `access_token` NOT NULL; `scopes` texto JSON; sem trigger de `updated_at`: o código grava `updated_at` explicitamente; FK com `ON DELETE CASCADE` |
| `usuarios` | SELECT `nome`, `numero_whatsapp` | só leitura (010d) |
| Redis | chaves de §4.3 RN-010-07 usadas por 010b, 010c, 010d, 010e | formatos de §4.2 |

**Migrations:** nenhuma. Os números 0040 a 0049 ficam reservados e **não** são usados; a tabela já existe em produção com todas as colunas necessárias (`schema-prod.sql`, `googlecalendartokens`).

## 12. Fora de escopo

- Os **textos dos prompts** (porte literal, com a 012) e as funções que os orquestram (`extract_*_params`, `extract_income_params`, `extract_expense_params`, que misturam LLM e banco): 012.
- `CalendarQueryService` (períodos, filtro por horário, mensagens de erro ao usuário), `CalendarAlertService` (janela de ±30 s, claim, mensagens) e `/calendar-alerts/*`: 014; `FreeTimeFinder`, padrões de agenda: 012.
- Filtro de segurança e chaves `security:*`: 009. Rate limit HTTP: 009.
- `oauth_proxy/` (só migra se o `GOOGLE_REDIRECT_URI` real ainda passar por ele, `api-contract.md` §6).
- Cifrar tokens do Google (desvio 029, depois da virada).
- Envio de imagem por arquivo (`enviar_imagem_whatsapp`), código de conflito de viagem (RN-010-64).
- E-mail (não existe).

## 13. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask. Não foi criado arquivo de desvio.

- **P-010-1 `/connect-calendar/{id}` abre para qualquer id (troca de conta Google).** Atual: sem autenticação nem assinatura no clique (`calendar_handler.py:25-102`). Qualquer pessoa pode abrir `/connect-calendar/<id de outro usuário>`, concluir o consentimento com a **própria** conta Google e o callback grava os tokens dela na linha da vítima: os alertas e o Drive da vítima passam a usar a conta do atacante (o arquivo que a vítima manda "salvar no drive" iria para o Drive do atacante). O desvio 021 (state assinado) **não resolve**, porque o atacante obtém um `state` válido chamando a própria rota. Também há enumeração de ids (404 × 302) e vazamento de "já conectado". Proposta: o link enviado por WhatsApp passa a levar `?t=<token>`, HMAC de `{usuario_id, expiração}` (7 dias) gerado por `GoogleLinks.ConnectUrl` (que todos os consumidores já usam); `/connect-calendar` sem token válido ⇒ 403 (HTML simples) e o `state` herda o `usuario_id` do token. Impacto: links já enviados (sem `t`) deixam de funcionar; o usuário pede outro pelo WhatsApp (o fluxo normal gera link novo). Recomendação: **aprovar** (a premissa do desvio 021, "links já enviados continuam funcionando", seria revogada); alternativa se o Rafael preferir manter os links antigos: aceitar sem `t` durante uma janela configurável (`GOOGLE_CONNECT_LEGACY_UNTIL`).
- **P-010-2 Clima: emoji sempre termômetro e chance de chuva nunca preenchida.** Atual: o mapa de tradução tem chaves em inglês (`weather_service.py:18-54`) mas a chamada usa `lang=pt` (`:119`), então nunca casa; `chance_chuva` é sempre `None` (`:147`) e `get_forecast` não tem chamador, logo o resumo matinal nunca mostra chuva (`daily_briefing_service.py:246-257`). Proposta: casar pelo texto em português da API (e pelo `condition.code`) e chamar `forecast.json` uma vez por cidade para preencher `chance_chuva`. Impacto: o resumo muda (emoji do tempo e linha de chuva) e há 1 requisição a mais por cidade (cota gratuita da WeatherAPI). Recomendação: aprovar a tradução; a previsão só se a cota permitir.
- **P-010-3 Limite diário de rotas não é aplicado e usa a data do container.** Atual: `check_rate_limit`/`increment_rate_limit` não têm chamador (`travel_time_service.py:471-533`); a data é `datetime.now()` (fuso do container) e o incremento lê-e-grava sem atomicidade. Proposta: aplicar o limite no intent de rota da 012 (só `calculate_travel_time`, que consome a cota do OpenRouteService), com data de São Paulo e incremento atômico (script Lua que lê `3` ou `"3"` e grava no mesmo formato). Impacto: usuários passam a ter no máximo `TRAVEL_TIME_DAILY_LIMIT` rotas por dia. Recomendação: aprovar a data de São Paulo; aplicar o limite é decisão de produto. Sem aprovação, o limitador é portado e fica sem uso.
- **P-010-4 Código morto e quebrado do `travel_time_service`.** Atual: `get_next_event_after` e `suggest_alternative_time` chamam `CalendarManagementService.get_events_by_date_range`, que **não existe** (`travel_time_service.py:245, 394`); `check_conflict` não tem chamador. Proposta: não portar (RN-010-64). Impacto: nenhum (nada os usa). Recomendação: aprovar.
- **P-010-5 Calendar: evento de 1 h na virada do dia e atualização que ignora data.** Atual: sem `hora_fim`, o fim é `hora_inicio + 1 h` calculado sobre `date.today()` e reduzido a hora (`calendar_management_service.py:74-76`): início 23:30 ⇒ fim 00:30 no **mesmo dia**, o que o Google rejeita (400). Em `update_event`, só mudar a data ou só o `hora_fim` é ignorado (o bloco só age com `hora_inicio`, `:202-226`), a duração original não é mantida (o comentário diz "manter duração original", o código soma 1 h) e a resposta diz que atualizou. Proposta: fim no dia seguinte quando passa de 24:00; atualizar data/horas independentemente e preservar a duração quando só o início mudar. Impacto: eventos que hoje falham passam a ser criados; remarcações só de data passam a valer. Recomendação: aprovar.
- **P-010-6 `get_credentials` re-tenta o refresh a cada uso mesmo com `needs_reconnect = TRUE`.** Atual: a rotina não consulta o flag (`google_calendar_oauth_service.py:260-412`); todo uso de um token já revogado chama o Google de novo, falha e re-marca (a notificação é limitada a 1 por semana, mas a chamada não). Proposta: com `needs_reconnect = TRUE` e `token_expiry` vencido, devolver "precisa reconectar" sem chamar o Google (o flag volta a `FALSE` na reconexão). Impacto: menos chamadas ao Google; um `invalid_grant` transitório só se recupera reconectando. Recomendação: aprovar.
- **P-010-7 Mensagens com mais de 4096 caracteres são recusadas pelo bot.** Atual: o bot responde 400 (`messageRoutes.js:46`) e o Flask conta como falha silenciosa. Proposta: `IBotClient.SendTextAsync` divide o texto em partes de até 4096 caracteres (em quebra de linha) e envia em sequência. Impacto: textos longos (resumos, relatórios) chegam em mais de uma mensagem em vez de se perderem; não sei se algum texto passa de 4096 hoje. Recomendação: aprovar.
- **P-010-8 Nome do usuário sem escape na página de sucesso.** Atual: `{nome_usuario}` entra cru no HTML (`calendar_handler.py:268`); o nome é digitado pelo próprio usuário no cadastro. Proposta: codificar para HTML (`&`, `<`, `>`, `"`). Impacto: nomes com esses caracteres mudam no HTML; o resto é idêntico. Recomendação: aprovar.
- **P-010-9 Busca de pasta no Drive escapa `'` mas não `\`.** Atual: `google_drive_service.py:120`; um nome com `\` quebra ou altera a consulta (nome vem do texto do usuário/LLM, só afeta o Drive dele). Proposta: escapar `\` antes de `'`. Impacto: nomes de pasta com `\` passam a funcionar. Recomendação: aprovar.
- **P-010-10 O prompt de criação de evento não recebe a data de hoje.** Atual: `extract_event_creation_details` (`gemini_service.py:850-873`) pede `data` em `YYYY-MM-DD` mas não informa a data atual; os exemplos fixam `2025-11-25`, então "dia 25/11" ou "sexta" dependem do chute do modelo (e do ano). Proposta (para a 012, ao portar o prompt): incluir "Hoje é {yyyy-MM-dd} ({dia da semana})" no texto. Impacto: o texto do prompt muda (e o cache `calendar` não existe). Recomendação: aprovar na 012.
- **P-010-11 Três chamadas ao Gemini ignoram o usuário.** Atual: `generate_ai_insights`, `generate_forecast_insights` e `suggest_best_slot_with_ai` usam o modelo global sem `usuario_id` (`analytics_service.py:353`, `forecast_service.py:435`, `free_time_finder_service.py:535`): não respeitam a preferência "chave própria × sistema", não contam uso nem limite do plano e, pelo `genai.configure` global, podem usar a chave do último usuário. Proposta: passar o `UserId` ao `ILlmClient` (a chave e os limites do 008 passam a valer). Impacto: o uso dessas três funções passa a contar no plano do usuário. Recomendação: aprovar (hoje nenhum usuário tem plano com limite).
- **P-010-12 OpenRoute repete a chamada em erro do cliente (4xx).** Atual: `raise_for_status` faz 400/401/403/404/429 virarem nova tentativa (`travel_time_service.py:75-76, 107-113`), gastando 2 requisições da cota diária por falha. Proposta: repetir só em timeout, erro de rede e 5xx. Impacto: chave inválida ou endereço malformado falham com 1 requisição. Recomendação: aprovar.

## 14. Perguntas em aberto

1. **Redis (010a): timeout de comando de 2 s.** O Python não tem timeout de comando. Mantenho 2 s por operação; confirme que não há operação legítima mais lenta (o `KEYS` do Python em Redis grande, por exemplo).
2. **`GOOGLE_REDIRECT_URI` real.** O padrão do Flask aponta para o Render (`app-controle-financeiro-oh32.onrender.com`), onde `oauth_proxy/app.py` redireciona 302 para `http://212.47.65.37:8000`. O .NET precisa responder `/connect-calendar/{id}` e `/oauth2callback` no host que estiver cadastrado no Google Cloud Console, e `GoogleLinks.ConnectUrl` usa esse host (RN-010-41). Qual é o valor em produção e o proxy ainda existe? (`infra-deploy.md` §7)
3. **PKCE.** `google-auth-oauthlib==1.2.0` (`requirements.txt:15`) pode gerar `code_challenge` automaticamente em `authorization_url`, mas o callback cria um `Flow` novo, sem `code_verifier`; nesse caso o Google rejeitaria a troca (`invalid_grant`, "Missing code verifier") e a conexão **já estaria quebrada** em produção. Não consegui confirmar (a biblioteca não está instalada no `.venv` local). O .NET não usa PKCE (cliente web com `client_secret` não exige). Alguém já conseguiu conectar o Google Calendar pelo fluxo atual? Só há 1 usuário real.
4. **Textos novos dos erros 500 (desvio 008).** Propostos: `Erro ao conectar` (§7.4), `Erro ao desconectar Google Calendar` (§7.6), `Erro ao fazer upload.` (RN-010-52), e, dentro da página B.4, `link de autorizacao invalido ou expirado` e `erro interno`. Aprovar ou trocar.
5. **`disconnect-calendar` com `x-api-key` (desvio 003).** Não achei nenhum chamador no Angular, no bot nem no backend. Há atalho do iPhone, script ou tela futura que o use? Se sim, ele precisa enviar o header.
6. **Regra de validade da chave `weather` (desvio 019).** O Python aceitava a chave do OpenWeatherMap se o status fosse diferente de 401. Para o WeatherAPI assumi o mesmo critério (401 ⇒ inválida; 403 de chave desativada ou cota estourada ⇒ "válida"). Alternativa: só 200 é válida. Qual prefere?
7. **Mapeamento de erros do Drive.** O Python casa por substring do texto da exceção; o .NET casa por HTTP e razão (RN-010-52). Em especial, 403 `storageQuotaExceeded` cai em "Limite de armazenamento" (antes de "Sem permissão"). Confirmar a prioridade.
8. **Métricas do cache do Gemini.** Mantive contadores em memória (como o Python). `integrations.md` §4 diz "Redis `INCR` opcional". Quer persistir no Redis? (afetaria as chaves compartilhadas)
9. **`BOT_API_KEY` vazia.** Hoje o Flask consulta `/sessions` com header vazio (o bot responde 401) e cai no legado em toda chamada. Mantive (RN-010-11). Prefere pular a consulta quando a chave estiver vazia?
10. **Janela de virada e `gemini_cache:*`.** O formato é compatível (§4.2); apagar o cache na virada (runbook) é opcional. Manter a instrução do runbook?

---

## Apêndice A — Catálogo dos prompts do Gemini

Nome, função, entrada, saída esperada, cache e origem. **Os textos não são portados aqui**: o porte literal para arquivos de texto (com teste de snapshot) acontece na feature que consome (coluna "Porte"). Toda função de `gemini_service.py` recebe `usuario_id` opcional e resolve a chave por `_obter_modelo_gemini_para_usuario` (RN-010-22). A saída é JSON (RN-010-25) salvo indicação contrária.

| # | Função (arquivo:linha def / prompt) | Função no produto | Entrada | Saída esperada | Cache | Porte |
|---|---|---|---|---|---|---|
| 1 | `extract_from_notification` (`gemini_service.py:94` / `:105`) | extrai dados de notificação de banco (Automate) | texto da notificação | `{valor_decimal, descricao_bruta, tipo_fluxo: "Renda"\|"Despesa"}` | não | 011 |
| 2 | `categorize_transaction` (`:119` / `:135`) | escolhe a subcategoria | lista JSON `[{id,...}]` de subcategorias, descrição, tipo, id de "Outros" | **só um número** (id); fora da lista ou não numérico ⇒ id de "Outros"; resposta bloqueada ⇒ id de "Outros" | `category`, 30 d, por usuário | 012 (e `transaction_categorizer_service`, 011) |
| 3 | `get_message_intent` (`:175` / `:189`) | classifica a mensagem do WhatsApp (36 nomes de intenção no prompt, `:189-338`: Renda, Despesa, Transferência, Pagamento Fatura, Consulta Período/Saldo/Valor Fatura/Categoria Específica/Potes/Contas Fixas/Todas Contas/Reserva/Detalhes Reserva, Listar Contas, Vencimentos Hoje/Amanhã/Essa Semana, Contas Atrasadas, Quitar Conta Fixa, Criar/Deletar Evento, Consultar Agenda, Horários Livres, Análise Inteligente, Comparação Mensal, Previsão de Gastos, Gráfico de Gastos, Configurar Notificações/Relatório Mensal/Localização/Endereço, Listar/Deletar Endereço, Menu de Ajuda, Upload Drive, Solicitar API Key) | texto da mensagem | `{"intent": "<nome>"}`; devolve só o nome | `intent`, 7 d, global | 012 |
| 4 | `extract_transaction_details` (`:376` / `:390`) | valor e descrição de renda/despesa | mensagem, intenção | `{valor_decimal: número\|null, descricao_bruta}` (as chaves `data`, `conta`, `categoria`, `parcelamento` que `extract_expense_params` lê **nunca vêm**) | `extract_trans`, 24 h, global | 012 |
| 5 | `extract_transfer_details` (`:422` / `:434`) | origem/destino/valor de transferência | mensagem, contas JSON | `{valor_decimal, conta_origem, conta_destino}` | não | 012 (via `extract_transfer_params` `:464`) |
| 6 | `extract_fatura_payment_details` (`:443` / `:455`) | pagamento de fatura | mensagem, contas JSON | `{valor_decimal, conta_origem, conta_cartao}` | não | 012 (via `extract_invoice_payment_params` `:500`) |
| 7 | `extract_category_query` (`:536` / `:547`) | categoria da consulta | pergunta | `{nome_categoria}` | não | 012 |
| 8 | `extract_period_query` (`:558` / `:578`) | período de consulta de gastos | pergunta | `{period_type: ontem\|hoje\|final_de_semana\|esta_semana\|semana_passada\|ultimos_7_dias\|este_mes\|mes_passado, categoria?}` | `period_query`, 7 d, global | 012 |
| 9 | `extract_chart_type` (`:615` / `:636`) | tipo e período do gráfico | mensagem | `{tipo_grafico: pizza\|barras\|linha, periodo_dias?, num_meses?}` | `chart_type`, 7 d, global | 012 |
| 10 | `extract_bill_payment` (`:676` / `:694`) | pagamento de conta fixa | mensagem | `{descricao, valor}` | não | **sem chamador** |
| 11 | `extract_payment_items` (`:713` / `:735`) | itens pagos (neutro) | mensagem | `{itens: [..], valor_total: número\|null, trigger_word}` | não | 012 |
| 12 | `extract_calendar_query` (`:779` / `:797`) | período da consulta de agenda | pergunta | `{period_type: hoje\|amanha\|final_de_semana\|esta_semana\|proxima_semana\|este_mes\|mes_passado\|proximo_mes}` | não | 012 |
| 13 | `extract_event_creation_details` (`:828` / `:850`) | dados do novo evento | mensagem | `{titulo, data: YYYY-MM-DD\|"hoje"\|"amanha", hora_inicio, hora_fim, descricao, localizacao}` (P-010-10) | não | 012 |
| 14 | `extract_event_deletion_query` (`:880` / `:895`) | evento a apagar | mensagem | `{titulo_busca, quando: "hoje"\|"amanha"\|null}` | não | 012 |
| 15 | `extract_time_filter_query` (`:916` / `:931`) | filtro de horário da agenda | pergunta | `{time_filter: manha\|tarde\|noite\|agora\|null}` | não | 012 |
| 16 | `extract_notification_config` (`:956` / `:976`) | configuração de notificações | mensagem | `{tipo: agenda_diaria\|contas_vencer, acao: ativar\|desativar\|configurar, hora, dias_antes}` | não | 012 |
| 17 | `extract_free_time_query` (`:1007` / `:1026`) | busca de horário livre | pergunta | `{period_type, duracao_minutos, contexto}` | não | 012 |
| 18 | `extract_fatura_query` (`:1056` / `:1074`) | cartão da consulta de fatura | pergunta, contas JSON | `{conta_cartao: nome\|null}` | não | 012 |
| 19 | `extract_saldo_query` (`:1096` / `:1114`) | conta da consulta de saldo | pergunta, contas JSON | `{nome_conta: nome\|null}` | não | 012 |
| 20 | `extract_parcelamento_info` (`:1137` / `:1156`) | detecta parcelamento | mensagem | `{parcelado, num_parcelas, descricao_limpa}` | não | **sem chamador** |
| 21 | `extract_monthly_report_config` (`:1190` / `:1209`) | configuração do relatório mensal | mensagem | `{acao: ativar\|desativar\|configurar\|consultar, momento_envio: INICIO_MES\|FIM_MES\|null, hora_envio}` | não | 012 |
| 22 | `generate_daily_briefing` (`:1256` / `:1434`) | resumo matinal humanizado | `briefing_data` (eventos, clima principal e adicionais, intervalos livres, alertas financeiros, data) formatado em texto pelo próprio código (`:1286-1431`) | **texto livre** para WhatsApp (até 20 linhas); pós-processamento por regex (asteriscos de lista, tags HTML) `:1478-1495`; falha ⇒ mensagem básica sem IA `:1497-1506` | não | 013 |
| 23 | `extract_location_config` (`:1509` / `:1527`) | cidade e estado | mensagem | `{cidade, estado: UF\|null}` | não | 012 |
| 24 | `extract_address_config` (`:1549` / `:1573`) | endereço favorito | mensagem | `{label: casa\|trabalho\|outro, endereco_completo}` | não | 012 |
| 25 | `validate_event_location_completeness` (`:1607` / `:1638`) | endereço do evento está completo? | texto do local | `{is_complete, missing_info: [..], suggested_question}`; texto vazio ⇒ resposta fixa sem chamar o modelo | não | **sem chamador** |
| 26 | `extract_address_label_from_deletion` (`:1685` / `:1702`) | endereço a apagar | mensagem | `{label}` | não | 012 |
| 27 | `generate_ai_insights` (`analytics_service.py:271` / `:297`) | análise inteligente de gastos | dados agregados do usuário | **texto livre** (até 15 linhas) + rodapé fixo | não | 012 |
| 28 | `generate_forecast_insights` (`forecast_service.py:301` / `:367`) | previsão de gastos | histórico e contas pendentes | **texto livre** (até 12 linhas) + rodapé | não | 012 |
| 29 | `suggest_best_slot_with_ai` (`free_time_finder_service.py:486` / `:515`) | melhor horário livre | até 5 horários, contexto, padrões | **texto livre** (2 linhas) | não | 012 |

Observações: as linhas 27 a 29 chamam o modelo global sem usuário (P-010-11). `extract_income_params` (`:1724`) e `extract_expense_params` (`:1794`) não têm prompt próprio: chamam o item 4 e consultam `agendamentos` (`_buscar_valor_lembrete_variavel`, `:1867`), portanto são da 012 com a 005. Nenhum prompt do Python recebe a data de hoje, exceto o resumo matinal (item 22, que recebe `DATA` formatada).

## Apêndice B — Páginas e textos das rotas de OAuth

Todas `text/html; charset=utf-8`. O HTML completo (com CSS) é a fonte; o plano manda copiá-lo **byte a byte** do Python para arquivos de recurso, preservando indentação e quebras (as páginas usam indentações diferentes), e os testes comparam o corpo inteiro.

- **B.1 "Ja Conectado"** — `calendar_handler.py:53-91`. `<title>Ja Conectado</title>`; `<h1>Voce ja esta conectado!</h1>`; parágrafos `Seu Google Calendar e Google Drive ja estao integrados.`, `Volte para o WhatsApp e use:`, `"Tenho compromisso hoje?"` (em negrito) e `"Salvar no drive pasta X"` (em negrito) seguido de ` (envie com uma imagem)`; ícone `OK`; fundo em gradiente `#667eea` a `#764ba2`.
- **B.2 "Autorizacao Negada"** — `:121-159`. `<title>Autorizacao Negada</title>`; ícone `X`; `<h1>Autorizacao Negada</h1>`; `Voce negou o acesso ao Google Calendar.`, `Para usar esta funcionalidade, voce precisa autorizar o acesso.`, `Tente novamente quando quiser!`; gradiente `#f093fb` a `#f5576c`.
- **B.3 "Conexao Autorizada"** — `:195-295` (f-string: `{{`/`}}` do CSS viram `{`/`}`). `<title>Conexao Autorizada</title>`; ícone `OK`; `<h1 class="success">Google Calendar + Drive Conectados!</h1>`; `Ola <strong>{nome_usuario}</strong>!`; `Sua conta Google foi conectada com sucesso.`; seções `Calendario:` (3 comandos) e `Google Drive:` (2 comandos); rodapé `Voce ja pode fechar esta janela`, `Volte para o WhatsApp!`, `Conexao segura via OAuth2 oficial do Google`. `nome_usuario` = nome do usuário ou `Usuario`.
- **B.4 "Erro"** — `:299-317`. `<title>Erro</title>`; `<h1 class="error">Erro</h1>`; `Ocorreu um erro ao processar a autorizacao:`; `<p><code>{texto}</code></p>` com `texto` fixo (§7.5: `link de autorizacao invalido ou expirado` ou `erro interno`, no lugar do `str(e)`); `Tente novamente ou contate o suporte.`; status **500**.
- **B.5 Mensagem de WhatsApp após conectar** — `:174-184`, texto literal em português: começa por `*Google Calendar + Drive Conectados!*`, depois `Ola {nome}! Sua conta Google foi conectada com sucesso.`, bloco `*Calendario:*` (`- Tenho compromisso hoje?`, `- Minha agenda amanha`), bloco `*Google Drive:*` (`- Envie uma imagem/documento com a legenda:` e `  _salvar no drive pasta Notas Fiscais_`) e `Seus dados estao seguros via OAuth2!`; parágrafos separados por linha em branco, `\n` como separador (copiar das linhas citadas).
