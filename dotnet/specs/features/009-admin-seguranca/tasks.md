# Tasks 009 — Admin e segurança

Regra: cada tarefa termina com testes verdes e commit. Ordem importa. Teste primeiro (vermelho), código depois. Pré-requisito: spec aprovada e respostas às perguntas Q-009-1 a Q-009-5 (as demais não bloqueiam). **Só esta feature edita `Program.cs`** (T9).

## Divisão em entregas

| Entrega | Tarefas | Observação |
|---|---|---|
| **009-0** | T1–T10 | PR separado e **primeiro**: filtro, IP, fallback 404, headers, rate limit, `security.log`, edição de `Program.cs`. Sem rotas |
| **009a** | T11–T19 | 9 rotas com `x-api-key` e sem dependência futura |
| **009b** | T20–T23 | 13 rotas de setup no-op, `oauth-config-check`, `debug-calendar` |
| **009c** | T24–T30 | 8 rotas dependentes (contrato + portas + `Unavailable*`); as portas são implementadas pela 013/012 |
| Fechamento | T31 | desvios, specs, runbook, PR |

009a só precisa de T1–T4 (porta e armazenamento) de 009-0; 009b precisa de T11 (grupo `/admin` com `RequireApiKey`); 009c precisa de T11.

## 009-0 — filtro de segurança

- [x] **T1 — Domain** (`SuspiciousRules`, `IpClassifier`, `AttemptWindow`, `SecurityKeys`, `PyTimedelta`, `SecurityLogText`): testes vermelhos ⇒ verdes. Um caminho exemplo por padrão (30) e por User-Agent (10), registrada × não registrada, janela de 10 min na borda, `H:MM:SS` (AC-009-06, 07, 08, 14, 17).
- [x] **T2 — Porta `ISecurityStore` + tipos** (`TrackResult`, `SecurityStats`, `Attempt`) em `Application/Security`. Casos de uso do filtro (função pura `Decide(...)`) com store fake: ordem blacklist ⇒ bloqueio ⇒ suspeito ⇒ 5ª tentativa (AC-009-01 a 04).
- [x] **T3 — `RedisSecurityStore`** (Infrastructure) sobre `ICache`: testes de integração com `RedisFixture`: codificação dupla (valor bruto com aspas externas), leitura da fixture do Python, TTLs 3600/600/31 536 000, JSON corrompido apaga a chave, `count` acumulado, `first_blocked` regravado, `SCAN`, sem Redis (AC-009-10 a 14). Gera `tests/Fixtures/security-redis-fixture.json`.
- [x] **T4 — `ClientIp`, `SecurityOptions`, `SecurityLog`** (`Api/Security`): resolução de IP (`Internal` quando origem confiável e sem `X-Forwarded-For`/`X-Original-For`), `MapToIPv4`, opções por `IConfiguration`, métodos `[LoggerMessage]` com os textos do spec §4 e §5 (AC-009-05, 06, 17).
- [x] **T5 — `SecurityFilterMiddleware`** + testes de integração com `WebApplicationFactory`, `RedisFixture` e um `IStartupFilter` de teste que fixa o `RemoteIpAddress`: IP via `X-Forwarded-For`, bypass interno, 5 tentativas, blacklist, bloqueio, rota registrada com trecho suspeito (`/admin/config-alertas-financeiros` na 009a; antes, uma rota de teste), `/api-keys` com e sem `API_KEYS_ROUTES_ENABLED`, 405 não conta, fail-open, `BOT_PROTECTION_ENABLED=false` (AC-009-01 a 09, 15, 20). **Inclui o teste de ordem do pipeline** (o filtro enxerga `GetEndpoint()`; `GET /` externo = 200).
- [ ] **T6 — Headers de segurança.** Capturar com `curl -I` os headers reais do Flask em homol (uma resposta 200 e um 404) e salvar em `tests/Fixtures/flask-security-headers.txt` (Q-009-2); `SecurityHeadersMiddleware` + teste de contrato com o conjunto capturado; `SECURITY_HEADERS_ENABLED=false` desliga (AC-009-19). Verificar se as páginas HTML da 010d usam `<script>` inline e registrar o resultado.
  - **Implementado em 2026-10-04 (parcial):** `SecurityHeadersMiddleware` e teste de contrato com o conjunto da spec §7 (`X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, HSTS, CSP com nonce); `SECURITY_HEADERS_ENABLED=false` desliga. **Pendente:** capturar os headers reais do Flask em homol (`tests/Fixtures/flask-security-headers.txt`; sem acesso a homol nesta sessão) e ajustar o conjunto/o teste. **Resultado da verificação da 010d:** `GooglePages.cs` não contém `<script>`; o `script-src 'self' 'nonce-...'` não bloqueia nada hoje.
- [x] **T7 — Rate limit** (`AddRateLimiter` + partição por IP real + isenções + 429 JSON envelope D + `Retry-After`), padrão **desligado**; testes (AC-009-18). Valores finais só depois da confirmação da Q-009-1.
- [x] **T8 — `UnmatchedRouteMiddleware`**: 404 HTML do Flask só quando `GetEndpoint()` é nulo; 405 preservado (`POST /connect-calendar/1` ⇒ 405), 404 de handler intacto, `abc`/`-1` de rota numérica intactos (AC-009-15, 16). Teste de que **não** foi usado `MapFallback`.
- [x] **T9 — Edição de `Program.cs`** exatamente como o `plan.md` §6 (Edições 1, 2 e 3) e `SecurityModule.AddServices` (store, opções, middlewares, `ForwardedHeadersOptions` com a rede privada confiável, rate limiter). Testes de integração do pipeline completo (T5, T7, T8 passam juntos) e o teste de `security.log` com diretório temporário e formato `[yyyy-MM-dd HH:mm:ss] WARNING: ...` (AC-009-17).
- [x] **T10 — Fechamento da 009-0:** testes de arquitetura (Application sem Serilog/Npgsql), suíte inteira verde, atualizar `infra-deploy.md` com as variáveis novas (§4 do plan) e o aviso do Ofelia (`localhost:5000`). **PR separado e primeiro.**

## 009a — rotas com `x-api-key`

- [x] **T11 — `AdminModule` (esqueleto) + `AdminUnauthorized.EnvelopeB`:** `MapGroup("/admin").RequireApiKey(...)`, `NumericIdFilter` antes da chave, `AdminMessages`, `AdminLog`, `AdminBody`. Teste de teoria por tabela de rotas (as 9 da 009a por enquanto; cresce nas outras): 401 sem corpo lido (AC-009-21). Com P-009-7 aprovada, teste de registro de tentativa inválida e de que chamada interna não conta.
- [x] **T12 — Casos de uso de segurança** (`UnblockIp`, `GetSecurityStats`, `BlacklistIp`, `RemoveBlacklistedIp`) com `ISecurityStore` fake: validação na ordem, textos exatos, 200 mesmo sem Redis no unblock, 500 de texto fixo nos outros (AC-009-22 a 24).
- [x] **T13 — Endpoints 1 a 4** + testes de integração com Redis real: chaves removidas, formato de `security-stats` (incluindo entrada corrompida e Redis fora), ordem por `ip`, TTL da blacklist (AC-009-22 a 24, 30).
- [x] **T14 — `CacheClearPlan` e `ClearLlmCache`/`GetLlmCacheStats`:** testes unitários dos três ramos e da diferença `category` × `category:*` (AC-009-25).
- [x] **T15 — Endpoints 5 e 6** com `LlmCacheAdmin` real e Redis: apaga só o previsto, corpo vazio × inválido (P-009-1), estatísticas no formato do §4.6 (AC-009-25, 26).
- [x] **T16 — `BotSessionRepository` + `ClearBotSession`** + endpoint 7: integração com `baileys_auth` semeada (3 + 2 linhas) (AC-009-27). Registrar o resultado da Q-009-3.
- [x] **T17 — `AdminNotificationConfigRepository`** (Dapper, SQL do plan §3): integração (criação da linha padrão, `ON CONFLICT DO NOTHING`, FK `23503`, `NULL` em `alertas_financeiros_ativos`).
- [x] **T18 — Casos de uso e endpoints 8 e 9** (`GetNotificationConfig`, `SetFinancialAlerts`): ordem das validações, `ativo` falso válido, 404 de usuário inexistente (P-009-4) ou 500 de texto fixo, id `abc`/`-1`/`> int.MaxValue` (AC-009-28, 29).
- [x] **T19 — `ErrorLeakTests`** das 9 rotas com falha injetada e mensagem sentinela (AC-009-30). **Fechamento da 009a.**
  - **Implementado em 2026-10-04 (009a):** desvios 077, 078, 079, 080 e 083 `implementado`. Divergências e decisões da implementação: (1) `AdminRouteIdFilter` próprio no grupo `/admin` (o `NumericIdFilter` da 002 lê o valor de rota `id`, não `usuarioId`), registrado antes do `RequireApiKey` do grupo; (2) com o desvio 078 aprovado o `ip` de `blacklist-add/remove` passa por `trim` + `IPAddress.TryParse` + forma canônica (o §4.3 diz "sem trim", texto escrito para o Flask); ordem: ip ausente ⇒ `IP inválido` ⇒ `reason`; (3) `pattern` não texto no `gemini-cache-clear` ⇒ 400 `Campo 'pattern' deve ser texto` (mensagem nova, não prevista na spec: sem ela `pattern: 5` limparia o cache inteiro); `usuario_id` inteiro positivo acima de `int.MaxValue` ⇒ 400 igual a não inteiro; (4) id de usuário fora de `int` (negativo ou grande) no corpo de `config-alertas-financeiros` ⇒ 404 `Usuário não encontrado` sem ir ao banco; (5) `GetOrCreate` são dois comandos (`INSERT ... ON CONFLICT DO NOTHING` e `SELECT`), não um só; (6) `AdminUnauthorized.EnvelopeB` devolve um `IResult` que conta a tentativa (`Invalid API key`) antes de escrever o 401. Pendências: Q-009-3 (`session_id` reais) e desvios 002/003/004/008 (fecham na T31).

## 009b — setup e diagnóstico

- [ ] **T20 — `SetupRoutes`:** tabela das 13 rotas e handler único sem acesso a banco; formatos texto/`<pre>`/envelope B; teste de arquitetura/spy de `IDbConnectionFactory` e ausência de `user_api_key_para_automate`, `tabelas_criadas`, `planos_inseridos` (AC-009-31).
- [ ] **T21 — `GetOAuthConfigCheck` + endpoint 23:** valores sentinela de client id/secret ausentes do corpo; `redirect_uri` presente (AC-009-32).
- [ ] **T22 — `RunCalendarDebug` + endpoint 24:** fakes de `IGoogleAccountService` e `ICalendarClient`; 5 etapas, `Token: [oculto]`, usuário desconectado com `<br>` sem `<pre>`, data de São Paulo, 500 de texto fixo (AC-009-33).
- [ ] **T23 — Fechamento da 009b:** `AdminAuthTests` cobre as 22 rotas até aqui; desvios 002 e 003 marcados como `implementado` neste escopo.

## 009c — rotas dependentes (contrato e portas)

- [ ] **T24 — Portas e `Unavailable*`:** `IScheduleMotor`, `IBillsNotificationJob`, `IDailyBriefingJob`, `IMonthlyReportJob`, tipos de resultado, `FeatureNotAvailableException`, registro `TryAdd`; 503 `Funcionalidade ainda não disponível` (AC-009-39). Avisar 013 e 012 das assinaturas.
- [ ] **T25 — `run-motor-agendamentos`** (endpoint 25) com porta fake: 200, `AlreadyRunning`, exceção ⇒ 500, 401 sem chamar a porta; teste de que um corpo `{}` e sem corpo funcionam (AC-009-34).
- [ ] **T26 — `trigger-*`** (endpoints 26 a 30) com portas fake: corpos exatos do §4.14, `trigger-agenda-notifications` sem porta (AC-009-35).
- [ ] **T27 — `AdminTestDataRepository` + `SendTestNotification`** (endpoint 31): usuário antes do `tipo`, `agenda` (fake de Google/Calendar), `contas` (7 agendamentos ⇒ 5 linhas; só o dia do mês), formatação de moeda pt-BR, bot falso ⇒ 500 (AC-009-36). Resolver a Q-009-9 em homol.
- [ ] **T28 — `SendTestMonthlyReport`** (endpoint 32): `momento` inválido, falha de negócio com `mensagem` genérica e `erro` com o motivo, sucesso com `dados` (AC-009-37).
- [ ] **T29 — `SendTestDailyBriefing`** (endpoint 33): 404/400/500/200 do §4.17 e `preview_mensagem` (AC-009-38).
- [ ] **T30 — Fechamento da 009c:** `AdminAuthTests` cobre as 33 rotas; documentar para 013/012 o que falta implementar (portas + substituição dos `Unavailable*`); desvios 004, 013 (rota) e 028 marcados no escopo.

## Fechamento

- [ ] **T31 — Contrato e desvios.** Revisar cada resposta contra o código Python lido (corpo exato dos testes); desvios 002, 003, 004, 005, 008, 023, 024, 028 marcados como `implementado` no escopo; criar os arquivos `parity-deviations/` dos `P-009-N` aprovados (sem aprovação o texto do spec §14 diz o que o .NET faz); atualizar `cutover-runbook.md` (Ofelia para a porta 8000, `redis-cli DEL` de emergência, restrição de `/admin/` no nginx, limpeza de `security:*` e das chaves do Limiter, alarme para `security-stats`); spec atualizada para `aprovada`/`implementada`; PR aberto.

Definição de pronto: `constitution.md` §10.
