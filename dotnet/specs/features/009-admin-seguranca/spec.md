# Spec 009 — Admin e segurança (`/admin/*`, filtro de segurança, 404 e rate limit)

- **Estado:** aprovada em 2026-10-04 (recomendações de foundation/decisoes-onda-2.md; onde a spec diverge, vale esse documento)
- **Onda:** 2
- **Origem (Python):** `app/routes/admin.py:31-81`; `app/presentation/admin/{cache_management,security,notification_config,database_setup,feature_migrations,testing,notification_triggers}.py`; `app/middleware/security.py:1-581`; `app/__init__.py:30-85` (logging, Limiter, Talisman, `before_request`); `app/security_config.py`; `app/shared/decorators.py:16-145`; `app/shared/responses/response_builder.py`; `app/services/redis_service.py:52-120`; `app/services/gemini_cache_service.py:240-352`; `app/services/notification_config_service.py:81-137, 333-378`; `app/services/finance/setup_service.py:17-36`; `app/services/monthly_report_processor_service.py:40-240`; `nginx/nginx.conf`; `docker-compose.yml:55-80`
- **Depende de:** 008-0 (`RequireApiKey`, `IApiKeyValidator`, `PythonDateFormat`), 010a (`ICache`), 010c (`ILlmCacheAdmin`), 010d (`IGoogleAccountService`, `ICalendarClient`), 010b (`IBotClient`), 001a (`IDbConnectionFactory`, `Envelopes`, `SafeError`, `FlaskNotFound`). As rotas da sub-entrega **009c** dependem ainda de 013 (jobs e motor) e 012 (relatórios, briefing, formatadores).
- **Consumida por:** todas as features (o filtro e o 404 valem para o processo inteiro); o bot Baileys (`POST /admin/run-motor-agendamentos`, diário às 8h, `x-api-key`); o Ofelia (`/admin/trigger-monthly-reports-*`).
- **Desvios aplicáveis (todos aprovados):** 002, 003, 004, 005 (refinado pelo 052), 006, 008, 013, 023 (com a condição "limites confirmados aqui", §8), 024, 028, 052. Propostas novas: §14 (`P-009-N`).

## Decisões aplicadas (já aprovadas; esta spec descreve o comportamento final)

| Desvio | Efeito nesta spec |
|---|---|
| 002 | Todas as `setup-*`, `populate-global-categories`, `cleanup-deprecated-notification-fields` viram rotas **no-op** com `x-api-key` obrigatório e texto "gerenciado por migrations" (§4.3). `debug-calendar` e `oauth-config-check` exigem `x-api-key` e não devolvem fragmento de segredo. |
| 003 | `x-api-key` obrigatório em `clear-bot-session`, `config-alertas-financeiros`, `get-notification-config`. Chamadores mapeados (§3.1): nenhum cliente legítimo precisa da rota aberta. |
| 004 | `gemini-cache-stats` funciona (hoje 500, o método se chama `get_metrics`). |
| 005 + 052 | A whitelist do filtro é **derivada das rotas registradas** (§5.3). `/api-keys/*` só existe com `API_KEYS_ROUTES_ENABLED=true`; desligada, o filtro trata como rota desconhecida. Padrões suspeitos só valem para caminhos **não** registrados. |
| 006 | `x-api-key` em tempo constante (reaproveita `RequireApiKey`). A separação JWT × admin fica depois da virada. |
| 008 | Nenhum 500 devolve `str(e)`; texto fixo por rota (§4, "500"). Logs e `security.log` sem chave, sem corpo. |
| 013 | `run-motor-agendamentos` com lock Redis e idempotência: responsabilidade da 013; esta spec só define a rota e a porta (§4.4). |
| 023 | `ForwardedHeaders` para a rede do nginx, rate limit por IP real, 429 em JSON envelope D (§5.1, §8). |
| 024 | Corpo ausente, vazio, inválido ou não-objeto = objeto sem campos; cai na validação "obrigatório" da rota (400). Não exige `Content-Type`. |
| 028 | `trigger-agenda-notifications` responde 200 "substituída pelo resumo matinal" (§4.5). |

## 1. Objetivo

(1) O operador (e o bot, e o Ofelia) executa tarefas administrativas pela API com `x-api-key`: desbloquear/bloquear IP, ver estatísticas de segurança, limpar e inspecionar o cache do Gemini, limpar a sessão do bot, consultar/alterar a configuração de alertas financeiros de um usuário, disparar os jobs de notificação e o motor de agendamentos. (2) O processo inteiro passa a ter o **filtro de segurança** do Flask (bloqueio de IP no Redis, whitelist, padrões suspeitos), o **fallback de 404** com o HTML do Flask e um **rate limit** por IP real.

## 2. Divisão (ver `tasks.md`)

| Entrega | Conteúdo | Pré-requisitos |
|---|---|---|
| **009-0** | Filtro de segurança, resolução de IP, whitelist por endpoint, fallback 404, rate limit, `security.log`, headers de segurança, edição de `Program.cs` | 010a, 008-0 |
| **009a** | 9 rotas com `x-api-key` sem dependência futura: `security-*` (4), `gemini-cache-*` (2), `clear-bot-session`, `get-notification-config`, `config-alertas-financeiros` | 009-0 (para o `ISecurityStore`), 010c |
| **009b** | 17 rotas de setup e diagnóstico (no-op, `oauth-config-check`, `debug-calendar`) | 009a (filtro `x-api-key` de admin) |
| **009c** | 7 rotas que dependem de 013/012: `run-motor-agendamentos`, 5 `trigger-*`, 3 `test-*`. Contrato e portas aqui; implementação das portas nas features 013/012 | 013, 012, 010b, 010d |

## 3. Rotas

Todas em `/admin/*`, **envelope B** (`{"status":"sucesso","mensagem":...}` / `{"status":"erro","mensagem":...}`), exceto onde a coluna "Resposta" diz texto ou HTML. O Flask tem **33 operações** (o cabeçalho de `admin.py` diz 31). Auth **KEY** = header `x-api-key` igual a `API_SECRET_KEY` (§4.0). A coluna "Hoje" é o Flask de produção; "Entrega" é a sub-entrega.

| # | Método | Caminho | Hoje | Auth no .NET | Resposta | Entrega |
|---|---|---|---|---|---|---|
| 1 | POST | `/admin/security-unblock-ip` | KEY | KEY | B | 009a |
| 2 | GET | `/admin/security-stats` | KEY | KEY | B | 009a |
| 3 | POST | `/admin/security-blacklist-add` | KEY | KEY | B | 009a |
| 4 | POST | `/admin/security-blacklist-remove` | KEY | KEY | B | 009a |
| 5 | POST | `/admin/gemini-cache-clear` | KEY | KEY | B | 009a |
| 6 | GET | `/admin/gemini-cache-stats` | KEY (500 hoje) | KEY | B | 009a |
| 7 | POST | `/admin/clear-bot-session` | **nenhuma** | KEY (003) | B | 009a |
| 8 | GET | `/admin/get-notification-config/<int:usuario_id>` | **nenhuma** | KEY (003) | B | 009a |
| 9 | POST | `/admin/config-alertas-financeiros` | **nenhuma** | KEY (003) | B | 009a |
| 10 | GET | `/admin/setup-database` | **nenhuma** (DROP+CREATE!) | KEY, no-op (002) | texto | 009b |
| 11 | GET | `/admin/populate-global-categories` | nenhuma | KEY, no-op | texto | 009b |
| 12 | GET | `/admin/setup-user-data` | nenhuma | KEY, no-op | B | 009b |
| 13 | GET | `/admin/setup-calendar-table` | nenhuma | KEY, no-op | texto | 009b |
| 14 | GET | `/admin/setup-monthly-reports-table` | nenhuma | KEY, no-op | B | 009b |
| 15 | POST | `/admin/setup-api-keys-tables` | KEY | KEY, no-op | B | 009b |
| 16 | GET | `/admin/setup-resumo-matinal` | nenhuma | KEY, no-op | HTML `<pre>` | 009b |
| 17 | GET | `/admin/setup-checkin-noturno` | nenhuma | KEY, no-op | HTML `<pre>` | 009b |
| 18 | GET | `/admin/setup-potes-alerts` | nenhuma | KEY, no-op | HTML `<pre>` | 009b |
| 19 | GET | `/admin/setup-alertas-financeiros` | nenhuma | KEY, no-op | HTML `<pre>` | 009b |
| 20 | GET | `/admin/setup-reserva-emergencia` | nenhuma (fora da whitelist hoje) | KEY, no-op | HTML `<pre>` | 009b |
| 21 | GET | `/admin/cleanup-deprecated-notification-fields` | nenhuma | KEY, no-op | HTML `<pre>` | 009b |
| 22 | GET | `/admin/setup-budget-violations` | nenhuma (fora da whitelist hoje) | KEY, no-op | HTML `<pre>` | 009b |
| 23 | GET | `/admin/oauth-config-check` | nenhuma (imprime prefixo do client_id) | KEY, sem fragmentos | B | 009b |
| 24 | GET | `/admin/debug-calendar` | nenhuma (imprime 20 chars do token) | KEY, sem token | HTML | 009b |
| 25 | POST | `/admin/run-motor-agendamentos` | KEY | KEY | B | 009c (013) |
| 26 | POST | `/admin/trigger-agenda-notifications` | KEY (quebra, 028) | KEY | B | 009c |
| 27 | POST | `/admin/trigger-bills-notifications` | KEY | KEY | B | 009c (013) |
| 28 | POST | `/admin/trigger-daily-briefing` | KEY | KEY | B | 009c (013) |
| 29 | POST | `/admin/trigger-monthly-reports-inicio` | KEY | KEY | B | 009c (013) |
| 30 | POST | `/admin/trigger-monthly-reports-fim` | KEY | KEY | B | 009c (013) |
| 31 | POST | `/admin/test-notification` | KEY | KEY | B | 009c (012, 014) |
| 32 | POST | `/admin/test-monthly-report/<int:usuario_id>` | KEY | KEY | B | 009c (013) |
| 33 | POST | `/admin/test-daily-briefing` | KEY | KEY | B | 009c (013) |

Convenções de roteamento (iguais às das outras features): sem barra final (404 do Flask, já feito pelo `StrictRoutingMiddleware`); `<int:usuario_id>` só casa dígitos sem sinal: `abc` e `-1` dão 404 HTML do Flask **antes** da autenticação, e ids acima de `int.MaxValue` também (o Flask daria erro do banco); método errado dá 405 (diferença de corpo aceita, decisão G-5).

### 3.1 Quem chama cada rota (condição do desvio 003; levantamento em 2026-10-04)

| Rota | Chamador identificado | Fonte |
|---|---|---|
| `run-motor-agendamentos` | **Bot Baileys**, diário às 8h (`America/Sao_Paulo`), `POST` com corpo `{}`, header `x-api-key`, timeout 30 s (`src/index.js`) / 60 s (`index.js`) | `bot-appfinanceiro-whatsapp/src/index.js:265`, `index.js:482` |
| `trigger-monthly-reports-inicio`, `-fim` | **Ofelia** (`curl -X POST http://localhost:5000/...` com `x-api-key`, dentro do container; sem `X-Forwarded-For`) | `docker-compose.yml:66, 71` |
| `trigger-agenda-notifications`, `-bills-`, `-daily-briefing` | UptimeRobot/cron externo, "a cada hora" (só documentado nos docstrings; **não** está no repositório) | `notification_triggers.py:31-120` |
| `setup-*`, `get-notification-config`, `config-alertas-financeiros`, `clear-bot-session`, `debug-calendar`, `oauth-config-check`, `test-*`, `gemini-cache-*`, `security-*` | **Nenhum cliente no repositório** (Angular `meusecretariofront/src` não referencia nenhuma; bot só chama `run-motor`) | grep em Angular e bot |

Consequências: (a) `x-api-key` em 003 não quebra ninguém conhecido; (b) o Ofelia usa `localhost:5000` e o .NET escuta em `8000`: a mudança é do cutover (ver pergunta Q-009-4); (c) as chamadas internas (loopback, sem `X-Forwarded-For`) **precisam** continuar passando pelo filtro sem bloqueio (§5.1, RN-009-01).

## 4. Contrato das rotas `/admin/*`

Convenções:
- **401** (todas): `{"status":"erro","mensagem":"Não autorizado"}`. Header ausente, vazio ou diferente da `API_SECRET_KEY`; a autenticação roda **antes** de ler o corpo. (É o 401 do `require_api_key` e das duas rotas com checagem inline, `admin.py:45-48, 68-70`; não é o envelope C do 008.)
- **Corpo** (desvio 024): ausente, vazio, inválido ou que não seja objeto = objeto sem campos. Não exige `Content-Type` (o Flask devolvia 415 HTML ou 500).
- **`validate_required_fields`** (`decorators.py:86-101`): campo "faltando" = ausente, `null`, `""`, `0`, `false`, `[]`, `{}` (`not data.get(f)`). Mensagem `Campos obrigatórios faltando: <campos separados por ", ">`, status 400.
- **500** (desvio 008): `{"status":"erro","mensagem":"<texto fixo da rota>"}`; o Flask devolvia `str(e)`. Texto fixo adotado: `Erro ao <ação>. Tente novamente.` (tabela por rota abaixo). O log leva só `SafeError.Describe`.
- Chaves de sucesso na ordem escrita (o Flask as ordena alfabeticamente no fio; clientes não dependem; os testes comparam a ordem do .NET).
- Tipos do corpo (propostas P-009-3): onde a spec diz "texto", "inteiro" ou "booleano", outro tipo JSON cai na validação "obrigatório" da rota. O Flask aceitava qualquer tipo truthy.

### 4.0 Autenticação `x-api-key`

`RequireApiKey(AdminUnauthorized.EnvelopeB)` do 008-0: compara `x-api-key` (primeiro valor) com `API_SECRET_KEY` por hash SHA-256 + `FixedTimeEquals`. `AdminUnauthorized.EnvelopeB` mora em `Api/Admin/` (a classe `ApiKeyUnauthorized` do 008-0 não é alterada) e, **se a P-009-7 for aprovada**, também registra a tentativa inválida no `ISecurityStore` (§5.7). *Origem: `decorators.py:16-40`; `admin.py:45-48, 68-70`; integrations.md §1.*

### 4.1 `POST /admin/security-unblock-ip` (009a)

Entrada: `ip` (texto).

| # | Condição | Resposta |
|---|---|---|
| 1 | `ip` ausente, nulo, não texto ou vazio depois do `trim` (corpo inválido incluído) | 400 `{"status":"erro","mensagem":"Campo 'ip' obrigatório"}` |

Efeito (`security.py:284-301, 266-281`): `DEL security:blocked:{ip}`, `DEL security:attempts:{ip}` e `DEL security:blacklist:{ip}` (com `ip` já `trim`ado). **Remove também da blacklist permanente**, apesar do docstring falar só de bloqueio temporário; reproduzido. O resultado das remoções é ignorado: **sem Redis ou com IP que nunca esteve bloqueado, a resposta continua 200**.
Sucesso **200** `{"status":"sucesso","mensagem":"IP <ip> desbloqueado"}`.
Logs (`security.log`, WARNING): `[SECURITY-UNBLOCK] Bloqueio temporário removido manualmente: <ip>` (só com Redis) e `[SECURITY-BLACKLIST] IP removido da blacklist: <ip>` (idem).
500: `Erro ao desbloquear IP. Tente novamente.` *Origem: `admin.py:59-81`.*

### 4.2 `GET /admin/security-stats` (009a)

Sem entrada. Lê com `ICache.KeysByPatternAsync` (`SCAN`) as chaves `security:blacklist:*`, `security:blocked:*`, `security:attempts:*`; cada valor é **JSON duplamente codificado** (RN-009-09): `GetJsonAsync<string>` devolve o JSON interno, que é então interpretado. Entradas com JSON corrompido ou sem os campos obrigatórios são **ignoradas** em silêncio.

**200** (Redis disponível):
```json
{"status":"sucesso","mensagem":"Estatísticas de segurança obtidas",
 "blacklisted_ips":[{"ip":"1.2.3.4","reason":"Manual block via API","blacklisted_at":"2026-10-04T10:00:00.123456","permanent":true}],
 "blocked_ips":[{"ip":"5.6.7.8","attempts":1,"blocked_until":"2026-10-04T11:00:00.123456","time_remaining":"0:59:12"}],
 "suspicious_activity":[{"ip":"9.9.9.9","recent_attempts":2,"last_attempt":"2026-10-04T10:05:00.123456","reasons":["Unknown endpoint: /x","Suspicious URL pattern: /\\.env"]}],
 "total_blacklisted":1,"total_blocked":1,"total_suspicious":1,"redis_connected":true}
```
- `ip` = sufixo da chave depois do prefixo (remoção do **prefixo**, não `replace` de todas as ocorrências como o Python).
- blacklist: `reason` padrão `"Unknown"`, `blacklisted_at` padrão `"Unknown"`, `permanent` padrão `true`.
- blocked: só se agora < `blocked_until`; `attempts` = `count` (padrão 1); `blocked_until` reformatado (`isoformat`); `time_remaining` = `str(blocked_until - agora)` sem os microssegundos (`H:MM:SS`, hora sem zero à esquerda).
- suspicious: só listas não vazias; `recent_attempts` = tamanho da lista (inclui tentativas já fora da janela de 10 min, porque o TTL é o único filtro aqui); `last_attempt` = `timestamp` do último; `reasons` = `reason` das **últimas 3**.
- Ordem dos itens: o Flask devolve a ordem do `KEYS` (indefinida); o .NET ordena por `ip` crescente (determinístico).

**200** (Redis indisponível): mesmas chaves, mais `"error":"Redis não disponível"` logo depois de `mensagem`, listas vazias, totais 0 e **sem** `redis_connected`. `"Estatísticas de segurança obtidas"` continua a `mensagem`.
500: `Erro ao obter estatísticas de segurança. Tente novamente.` *Origem: `security.py:263-283`; `security.py` (middleware) `:495-581`.*

### 4.3 `POST /admin/security-blacklist-add` (009a)

Entrada: `ip` (texto, obrigatório), `reason` (texto, opcional).

| # | Condição | Resposta |
|---|---|---|
| 1 | `ip` falso (ausente, nulo, vazio; corpo inválido incluído) ou não texto (P-009-3) | 400 `{"status":"erro","mensagem":"Campos obrigatórios faltando: ip"}` |
| 2 | `reason` presente, não nulo e não texto (P-009-3) | 400 `{"status":"erro","mensagem":"Campo 'reason' deve ser texto"}` (mensagem **nova**, ver P-009-3) |
| 3 | Redis indisponível (`ICache.IsAvailableAsync` falso, ou a gravação não confirmou) | 500 `{"status":"erro","mensagem":"Falha ao adicionar IP à blacklist (Redis indisponível)"}` |

`reason` ausente = `"Manual block via API"`; `reason: null` explícito é gravado e devolvido como `null` (o Flask também). `ip` **não** é validado nem normalizado (P-009-2) e **não** sofre `trim` (o `unblock` faz `trim`; reproduzido).
Efeito: `SETEX security:blacklist:{ip} 31536000 <JSON duplamente codificado de {"ip":ip,"reason":reason,"blacklisted_at":<agora naive isoformat>,"permanent":true}>`.
Sucesso **200** `{"status":"sucesso","mensagem":"IP <ip> adicionado à blacklist permanente","ip":"<ip>","reason":"<reason>"}`.
Log (WARNING): `[SECURITY-BLACKLIST] IP adicionado à blacklist permanente: <ip> | Razão: <reason>`; sem Redis (ERROR): `[SECURITY-BLACKLIST] Redis indisponível, não foi possível adicionar IP à blacklist: <ip>`.
500 (outras falhas): `Erro ao adicionar IP à blacklist. Tente novamente.` *Origem: `security.py:286-323`; `security.py` (middleware) `:234-264`.*

### 4.4 `POST /admin/security-blacklist-remove` (009a)

Entrada: `ip` (texto, obrigatório). Validação 1 igual à de 4.3. Efeito: `DEL security:blacklist:{ip}` (sem `trim`).
Redis indisponível ⇒ 500 `{"status":"erro","mensagem":"Falha ao remover IP da blacklist (Redis indisponível ou IP não estava na blacklist)"}`. Com Redis, **200 mesmo que o IP não estivesse** (o `delete` do Redis service devolve `True` sempre que conectado; o texto do 500 sugere o contrário, reproduzido).
Sucesso **200** `{"status":"sucesso","mensagem":"IP <ip> removido da blacklist permanente","ip":"<ip>"}`. Log (WARNING): `[SECURITY-BLACKLIST] IP removido da blacklist: <ip>`.
500: `Erro ao remover IP da blacklist. Tente novamente.` *Origem: `security.py:326-360`.*

### 4.5 `POST /admin/gemini-cache-clear` (009a)

Entrada (todos opcionais): `pattern` (texto), `usuario_id` (inteiro).

Corpo **vazio** (nenhum byte) = `{}` = limpar tudo (comportamento descrito no docstring, que o Flask **não** cumpre: sem `Content-Type: application/json` ele responde 415 e o `handle_errors` o transforma em 500; P-009-1). Corpo **não vazio** e inválido/não-objeto: 400 `{"status":"erro","mensagem":"Corpo da requisição deve ser um objeto JSON"}` (mensagem **nova**, P-009-1). `usuario_id` presente, truthy e não inteiro positivo: 400 `{"status":"erro","mensagem":"Campo 'usuario_id' deve ser um número inteiro"}` (**nova**, P-009-3).

Decisão de ramo (`cache_management.py:190-205`), nesta ordem:

| Condição | Chamada | `mensagem` |
|---|---|---|
| `usuario_id` truthy (≠ 0, ≠ ausente/nulo) | `ILlmCacheAdmin.InvalidateUserAsync(usuario_id, pattern)` ⇒ apaga `gemini_cache:{pattern ou *}:{usuario_id}:*` | `<n> chaves deletadas para usuário <usuario_id>` |
| senão, `pattern` truthy | `InvalidatePatternAsync(pattern)` ⇒ `gemini_cache:{pattern}:*` | `<n> chaves deletadas (pattern: <pattern>)` |
| senão | `InvalidatePatternAsync("*")` ⇒ `gemini_cache:*:*` | `Cache completo limpo: <n> chaves deletadas` |

**Atenção ao padrão com usuário.** O formato da chave é `gemini_cache:{tipo}:{uid|global}:{hash}`. Para limpar **um tipo de um usuário** o `pattern` deve ser o **tipo puro** (`"category"`), que resulta em `gemini_cache:category:123:*`. Passar `"category:*"` (como o docstring do Flask sugere) gera `gemini_cache:category:*:123:*`, que **não casa** com nenhuma chave real e apaga 0. Já no ramo **sem usuário** o `pattern` aceita as duas formas (`"intent"` ⇒ `gemini_cache:intent:*`; `"intent:*"` ⇒ `gemini_cache:intent:*:*`). A spec reproduz o formato do Flask; não "corrige" `category:*`; o teste cobre os dois casos (AC-009-25).
Redis indisponível: `ILlmCacheAdmin` devolve 0 e a rota responde 200 com `0 chaves deletadas ...` (igual ao Flask).
Sucesso **200** `{"status":"sucesso","mensagem":"<acima>","keys_deleted":<n>}`. 500: `Erro ao limpar o cache do Gemini. Tente novamente.` *Origem: `cache_management.py:168-207`; `gemini_cache_service.py:240-311`; `ILlmCacheAdmin`.*

### 4.6 `GET /admin/gemini-cache-stats` (009a)

Sem entrada. `ILlmCacheAdmin.GetMetrics()` (métricas **da instância**, em memória, zeradas a cada reinício; desvio 004).
**200**
```json
{"status":"sucesso","mensagem":"Estatísticas do cache obtidas com sucesso",
 "total_requests":0,"cache_hits":0,"cache_misses":0,"cache_saves":0,"cache_errors":0,"hit_rate":"0.0%",
 "breakdown_by_type":{"intent":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"}, "...":"category, extract_trans, period_query, calendar_query, chart_type, fatura_query, saldo_query, other (nesta ordem)"},
 "estimated_savings":{"calls_saved":0,"quota_saved_pct":"0.0%"}}
```
`hit_rate` é texto `"<%.1f>%"`; `total_requests` = hits + misses. 500: `Erro ao obter estatísticas do cache. Tente novamente.` *Origem: `cache_management.py:210-248`; `gemini_cache_service.py:313-351`.*

### 4.7 `POST /admin/clear-bot-session` (009a)

Sem entrada. Efeito: `DELETE FROM baileys_auth WHERE session_id = 'baileys_session'` (literal; ver Q-009-3) e conta as linhas.
**200** `{"status":"sucesso","mensagem":"Sessão do bot ('baileys_auth') limpa com sucesso. <n> linhas deletadas.","linhas_deletadas":<n>}`. 500: `Erro ao limpar a sessão do bot. Tente novamente.` Log: `[ADMIN-FIX] <mensagem>`. *Origem: `database_setup.py:23-39`; `setup_service.py:17-36`; integrations.md §3.*

### 4.8 `GET /admin/get-notification-config/<usuario_id>` (009a)

Efeito: lê `notificationconfigs` do usuário; **se não existir, insere a linha com os padrões do banco** (efeito colateral do GET, `notification_config_service.py:81-137`) e devolve a nova.
**200** `{"status":"sucesso","mensagem":"Configurações de notificação obtidas com sucesso","usuario_id":<id>,"configuracoes":{"resumo_matinal_ativo":<bool>,"resumo_matinal_hora":"HH:mm","alertas_financeiros_ativos":<bool|null>}}`. `resumo_matinal_hora` = `strftime('%H:%M')`; `alertas_financeiros_ativos` é anulável no banco (pode sair `null`). Padrões do banco para linha nova: `true`, `07:00`, `true`.
Usuário inexistente: o `INSERT` viola a FK ⇒ o Flask devolve **500 com `str(e)`**. .NET: 404 `{"status":"erro","mensagem":"Usuário não encontrado"}` (P-009-4; sem aprovação, 500 de texto fixo).
500: `Erro ao obter as configurações de notificação. Tente novamente.` *Origem: `notification_config.py:375-399`.*

### 4.9 `POST /admin/config-alertas-financeiros` (009a)

Entrada: `usuario_id` (inteiro), `ativo` (booleano).

| # | Condição | Resposta |
|---|---|---|
| 1 | `usuario_id` falso (ausente, nulo, 0, `""`, `false`; corpo inválido incluído) ou não inteiro (P-009-3) | 400 `{"status":"erro","mensagem":"Campos obrigatórios faltando: usuario_id"}` |
| 2 | `ativo` ausente ou nulo (`false` é válido) ou não booleano (P-009-3) | 400 `{"status":"erro","mensagem":"Campo 'ativo' é obrigatório (true ou false)"}` |

Efeito: garante a linha (como 4.8) e `UPDATE notificationconfigs SET alertas_financeiros_ativos = @ativo, updated_at = now() WHERE usuario_id = @id RETURNING alertas_financeiros_ativos`.
**200** `{"status":"sucesso","mensagem":"Alertas financeiros ativados com sucesso"|"Alertas financeiros desativados com sucesso","configuracao":{"alertas_financeiros_ativos":<bool>}}`.
Usuário inexistente: Flask 500 com `str(e)`; .NET 404 igual a 4.8 (P-009-4). Falha do `UPDATE`: o serviço devolve `(False, "Erro ao atualizar configuração: <str(e)>")` e a rota responde 500 com essa mensagem (vazava `str(e)`); .NET: 500 `Erro ao atualizar a configuração de alertas financeiros. Tente novamente.`
*Origem: `notification_config.py:402-436`; `notification_config_service.py:333-378`.*

### 4.10 Rotas de setup (009b): 10 a 22

Contrato único. `x-api-key` obrigatório (401 B); **nenhum DDL, nenhuma leitura ou escrita no banco**. Resposta 200 com a mensagem fixa

`Rota mantida por compatibilidade: o esquema do banco é gerenciado por migrations. Nenhuma alteração foi feita.`

no formato original de cada rota:
- **texto** (10, 11, 13): corpo = a mensagem, `Content-Type: text/html; charset=utf-8` (o Flask devolve `str` como HTML).
- **HTML `<pre>`** (16 a 22): `<pre>` + mensagem + `</pre>`, `text/html; charset=utf-8`.
- **envelope B** (12, 14, 15): `{"status":"sucesso","mensagem":"<mensagem>"}`. Chaves extras do Flask **não** voltam: `user_api_key_para_automate` (12; é a `api_key_automate` de um usuário, segredo), `tabelas_criadas` e `planos_inseridos` (15; afirmariam uma criação que não ocorreu).

Origem e efeito antigo, só para registro: 10 recriava o schema inteiro (`setup_service.py:38`); 11 semeava categorias (hoje migration `0020`); 12 criava/atualizava o usuário do WhatsApp `553194001072` com contas e devolvia a chave de automação; 13/14/15 criavam `googlecalendartokens`, `monthlyreportconfigs` e as 7 tabelas de chaves; 16 a 22 acrescentavam colunas e faziam `UPDATE`/`INSERT` de configuração (a 21 removia `agenda_diaria_*`). Tudo isso existe no `0000_baseline.sql`. 500 não existe (a rota não faz I/O).
*Origem: `database_setup.py:42-157`; `feature_migrations.py:23-704`.*

### 4.11 `GET /admin/oauth-config-check` (009b)

`x-api-key` obrigatório. **200** `{"status":"sucesso","mensagem":"Configuracao OAuth verificada","client_id_configured":<bool>,"client_secret_configured":<bool>,"redirect_uri":"<GOOGLE_REDIRECT_URI>"}` (sem acento em "Configuracao", como o Flask). `*_configured` = valor não vazio em `AppSettings`. **Fora**: `client_id_prefix` (20 primeiros caracteres do client id; desvio 002). `redirect_uri` é uma URL pública de callback, não segredo, e permanece. 500: `Erro ao verificar a configuração do OAuth. Tente novamente.` *Origem: `feature_migrations.py:707-726`.*

### 4.12 `GET /admin/debug-calendar` (009b)

`x-api-key` obrigatório. Diagnóstico do Google Calendar do usuário **1** (fixo no Flask; ver Q-009-6) em HTML. Mesmas 5 etapas, com `IGoogleAccountService` e `ICalendarClient` (010d):

1. `IsConnectedAsync(1)`: linha `✅ Usuário conectado? <True|False>`. Se falso, a resposta é `<br>`-juntada **sem `<pre>`** e termina com `<br>❌ Usuário não conectado!` (como o Flask, `testing.py:57-58`).
2. `GetCredentialsAsync(1)`: `✅ Credenciais obtidas` e **`   - Token: [oculto]`** no lugar dos 20 primeiros caracteres; a linha `Expiry` sai (a porta não expõe a expiração).
3. `✅ Serviço criado` (etapa mantida só pela forma; no .NET não há objeto de serviço).
4. `ListSelectedCalendarsAsync(1)`: `✅ <n> calendários encontrados` (**selecionados**: o Flask conta todos os da `calendarList`; diferença documentada).
5. `GetEventsInRangeAsync(1, hoje, hoje)` (data de São Paulo, `IClock`): `   Data: <yyyy-MM-dd>`, `✅ API retornou <n> eventos` e, se houver, até 3 linhas `   <i>. <título>`. Termina com `✅ TODOS OS TESTES PASSARAM!`.

Saída `<pre>` + linhas separadas por `\n` + `</pre>`, `text/html; charset=utf-8`. Cabeçalho com `=` ×60 e `🧪 TESTE DE DEBUG - GOOGLE CALENDAR` como no Flask. Nenhuma linha contém token, `refresh_token`, e-mail ou id de evento.
Falha em qualquer etapa (exceção): 500 `{"status":"erro","mensagem":"Erro ao executar o diagnóstico do Google Calendar. Tente novamente."}` (o Flask devolvia `str(e)`, que pode conter trechos de resposta do Google). *Origem: `testing.py:31-130`.*

### 4.13 `POST /admin/run-motor-agendamentos` (009c; implementação do motor: 013)

Entrada: nenhuma (o bot manda `{}`). Auth KEY (o 401 usa o mesmo envelope B).
Porta: `IScheduleMotor.RunAsync(CancellationToken)` ⇒ `MotorRunResult { Executed | AlreadyRunning }` (definida pela 009c em `Application/Admin/`, **implementada pela 013** com lock `job_lock:motor-agendamentos` e idempotência por (`agendamento_id`, data), desvio 013).
**200** `{"status":"sucesso","mensagem":"Agendamentos processados."}` (o Flask devolve 200 mesmo quando o processamento falha internamente, porque `processar_agendamentos` engole os erros; o .NET só devolve 200 quando a porta termina sem exceção). `AlreadyRunning` ⇒ 200 `{"status":"sucesso","mensagem":"Agendamentos já estão em processamento."}` (**nova**; Q-009-8). Exceção da porta ⇒ 500 `{"status":"erro","mensagem":"Erro ao processar agendamentos. Tente novamente."}` (o Flask: `str(e)`).
O tempo de resposta precisa caber nos 30 s do bot (`src/index.js`): a 013 decide se o motor roda inline ou em segundo plano (Q-009-8). Logs `[MOTOR]`. *Origem: `admin.py:31-56`; `schedule_processor.py:23-`.*

### 4.14 `POST /admin/trigger-*` (009c; implementação: 013)

Todas: auth KEY, sem entrada, cada uma chama uma porta e devolve `{"status":"sucesso","mensagem":"<texto>", ...campos do resultado}` (os campos do resultado entram na raiz, depois de `mensagem`).

| Rota | Porta (Application/Admin) | `mensagem` | Campos do resultado | 500 |
|---|---|---|---|---|
| `trigger-agenda-notifications` | nenhuma (desvio 028) | `Agenda diária foi substituída pelo resumo matinal. Nada a processar.` | `usuarios_processados:0`, `notificacoes_enviadas:0`, `erros:0` | não existe |
| `trigger-bills-notifications` | `IBillsNotificationJob.RunAsync` | `Processamento de contas a vencer concluído` | `usuarios_processados`, `notificacoes_enviadas`, `erros`, `hora_processamento` (ISO `isoformat` de São Paulo) | `Erro ao processar notificações de contas a vencer. Tente novamente.` |
| `trigger-daily-briefing` | `IDailyBriefingJob.RunForCurrentMinuteAsync` | sem usuário: `Nenhum usuario configurado para <HH:mm>`; senão `Processamento de resumos matinais concluído` | sem usuário: `usuarios_processados:0`, `enviados_sucesso:0`, `horario`; senão `usuarios_processados`, `enviados_sucesso`, `erros`, `horario` (`HH:mm`) | `Erro ao processar os resumos matinais. Tente novamente.` |
| `trigger-monthly-reports-inicio` | `IMonthlyReportJob.RunAsync(InicioMes)` | `Processamento de relatórios mensais (INICIO_MES) concluído` | `momento_envio`, `horario_processamento` (`yyyy-MM-dd HH:mm:ss`), `total_usuarios`, `enviados_sucesso`, `enviados_erro`, `usuarios_processados[]`, `erros[]` | `Erro ao processar os relatórios mensais. Tente novamente.` |
| `trigger-monthly-reports-fim` | `IMonthlyReportJob.RunAsync(FimMes)` | `... (FIM_MES) concluído` | idem | idem |

Janela de 5 minutos dos relatórios (`janela_minutos=5`), seleção de usuários, texto dos relatórios e do briefing são da 013. O `trigger-daily-briefing` usa `datetime.now()` do container no Flask; o .NET usa a hora de São Paulo (`IClock`, regra 8). *Origem: `notification_triggers.py:31-228`; `notification_processor_service.py:143-148, 356-361`; `monthly_report_processor_service.py:46-53`; desvio 028.*

### 4.15 `POST /admin/test-notification` (009c)

Entrada: `tipo` (texto, padrão `"agenda"`), `usuario_id` (inteiro, padrão 1).
Ordem: (1) usuário em `usuarios`: sem linha ⇒ 404 `{"status":"erro","mensagem":"Usuário não encontrado"}` (**antes** de validar `tipo`); (2) `tipo`:
- `agenda`: `IGoogleAccountService.IsConnectedAsync` falso ⇒ 400 `{"status":"erro","mensagem":"Usuário não conectou Google Calendar"}`; senão eventos de hoje (`ICalendarClient.GetEventsInRangeAsync(hoje, hoje)`) e mensagem `🧪 *TESTE - Agenda de Hoje*\n\n📅 <n> evento(s) encontrado(s)\n\n` + até 5 linhas `<i>. <título>\n` + (se > 5) `\n... e mais <n-5>`.
- `contas`: agendamentos **ativos** do usuário com `dia_execucao` = dia do mês de **amanhã** (São Paulo), `LIMIT 5`, mensagem `🧪 *TESTE - Contas de Amanhã*\n\n💰 <n> conta(s) encontrada(s)\n\n` + linhas `<i>. <descrição>: <moeda pt-BR>\n` (`valor_previsto` nulo = 0). O filtro olha só o dia do mês (ignora mês, periodicidade e parcelas), como o Flask.
- outro ⇒ 400 `{"status":"erro","mensagem":"Tipo inválido"}`.
(3) `IBotClient.SendTextAsync(numero_whatsapp, mensagem)`: verdadeiro ⇒ **200** `{"status":"sucesso","mensagem":"Notificação de teste enviada para <nome>","tipo":"<tipo>","usuario_id":<id>,"numero_whatsapp":"<número>"}`; falso ⇒ 500 `{"status":"erro","mensagem":"Falha ao enviar notificação"}`.
Corpo ausente/inválido (024): usa os padrões `agenda` e `1`. 500 (exceção): `Erro ao enviar a notificação de teste. Tente novamente.`
Divergência com o desvio 028: ele diz que `test-notification tipo=agenda` quebra; **o código lido não usa `agenda_diaria_*`** (`testing.py:130-175` só usa Calendar e o bot), então a rota funciona hoje e continua funcionando (Q-009-9). *Origem: `testing.py:133-253`.*

### 4.16 `POST /admin/test-monthly-report/<usuario_id>` (009c)

Query `momento` (padrão `INICIO_MES`). `momento` fora de `INICIO_MES`/`FIM_MES` ⇒ 400 `{"status":"erro","mensagem":"Parâmetro 'momento' deve ser 'INICIO_MES' ou 'FIM_MES'"}`.
Porta `IMonthlyReportJob.SendManualAsync(usuarioId, momento)` ⇒ `ManualReportResult`:
- sucesso ⇒ **200** `{"status":"sucesso","mensagem":"Relatório mensal enviado com sucesso","sucesso":true,"usuario_id":<id>,"nome":"<nome>","mensagem_enviada":<bool>,"grafico_enviado":<bool>,"dados":{...dados do relatório da 013}}`;
- falha de negócio (usuário inexistente, sem transações no período, falha no envio do texto) ⇒ **400** `{"status":"erro","mensagem":"Erro ao enviar relatório","sucesso":false,"erro":"<motivo>"[,"dados":{...}]}` com `erro` em `Usuário não encontrado` | `Nenhuma transação encontrada no período` (com `dados`) | `Falha ao enviar mensagem de texto`. Note que a **`mensagem` é sempre o texto genérico**; o motivo vai em `erro` (o `resultado.get('mensagem', ...)` do Flask nunca encontra a chave).
Exceção ⇒ 500 `Erro ao enviar o relatório mensal. Tente novamente.` Id não numérico/negativo: 404 HTML. *Origem: `testing.py:256-302`; `monthly_report_processor_service.py:176-240`.*

### 4.17 `POST /admin/test-daily-briefing` (009c)

Entrada: `usuario_id` (inteiro, padrão 1; corpo ausente/inválido = padrão). Ordem: usuário inexistente ⇒ 404 `{"status":"erro","mensagem":"Usuário <id> não encontrado"}`; `numero_whatsapp` vazio ⇒ 400 `{"status":"erro","mensagem":"Usuário <id> não tem número de WhatsApp cadastrado"}`; porta `IDailyBriefingJob.SendTestAsync(usuarioId)`:
- dados do briefing indisponíveis ⇒ 500 `{"status":"erro","mensagem":"Erro ao preparar dados do briefing"}`; mensagem vazia ⇒ 500 `{"status":"erro","mensagem":"Falha ao gerar mensagem"}`; envio falhou ⇒ 500 `{"status":"erro","mensagem":"Falha ao enviar WhatsApp"}`;
- sucesso ⇒ **200** `{"status":"sucesso","mensagem":"Resumo matinal enviado com sucesso","usuario_id":<id>,"numero_whatsapp":"<número>","total_eventos":<n>,"preview_mensagem":"<primeiros 200 caracteres>..."}` (`...` só se a mensagem tiver mais de 200).
Exceção ⇒ 500 `Erro ao enviar o resumo matinal de teste. Tente novamente.` *Origem: `testing.py:305-353`.*

## 5. Filtro de segurança

Substitui `security_filter` (`security.py:430-493`), registrado como `before_request` quando `BOT_PROTECTION_ENABLED` (padrão `true`) está ligado.

### 5.1 Resolução do IP (RN-009-01)

Cadeia real: cliente → nginx nativo da VPS → nginx do compose → API. O nginx do compose manda `X-Forwarded-For: $proxy_add_x_forwarded_for` e `X-Real-IP` (`nginx.conf:109-112`). O Flask usava `remote_addr` e, se ele fosse "confiável" (loopback ou privado) **e** houvesse `X-Forwarded-For`, o **primeiro** IP da lista (o mais à esquerda, que o cliente controla). Sem o header, a requisição **passa direto** (Ofelia, health interno, bot).

.NET (desvio 023 + P-009-6): `UseForwardedHeaders` com `ForwardedHeaders = XForwardedFor | XForwardedProto`, `KnownIPNetworks` = `127.0.0.0/8`, `::1/128`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `fc00::/7`, `fe80::/10`, `KnownProxies` limpo, `ForwardLimit = null`. O middleware lê a lista **da direita para a esquerda** e para no primeiro endereço não confiável; esse é o IP do cliente (um `X-Forwarded-For` forjado pelo cliente fica à esquerda e é ignorado). `RemoteIpAddress` IPv4 mapeado em IPv6 (`::ffff:a.b.c.d`) é convertido com `MapToIPv4()`; o texto do IP é `IPAddress.ToString()`.
Passa **sem filtro** (RN-009-02, passo 0) quando o IP original da conexão é confiável e **o header `X-Forwarded-For` nunca existiu** (nem `X-Original-For`, que o middleware cria ao processá-lo). Se o header existiu, o filtro roda para o IP resolvido, mesmo que ele seja privado (igual ao Flask).

### 5.2 Ordem das verificações (RN-009-02)

0. Bypass interno (acima).
1. `security:blacklist:{ip}` existe ⇒ 403 `{"error":"Access permanently denied","message":"Your IP has been permanently blocked"}` (envelope D). Log WARNING `[SECURITY-BLACKLISTED] IP na blacklist tentou acessar | IP: <ip> | Path: <path>`.
2. `security:blocked:{ip}` existe e agora < `blocked_until` ⇒ 403 `{"error":"Access denied","message":"Your IP has been temporarily blocked due to suspicious activity"}`. Log `[SECURITY-BLOCKED] IP bloqueado tentou acessar | IP: <ip> | Path: <path>`. JSON corrompido ou `blocked_until` inválido ⇒ apaga a chave e segue (não bloqueado). Chave presente mas **expirada** ⇒ segue.
3. Requisição suspeita (§5.4) ⇒ registra tentativa (§5.5). Se essa foi a 5ª ⇒ 403 `{"error":"Access denied","message":"Too many suspicious requests. IP blocked."}`; senão 404 `{"error":"Not found"}` (JSON; não revela a detecção). O método HTTP não conta; **`OPTIONS` e `HEAD` também passam pelo filtro**.
4. Caso contrário, segue para o roteamento.

### 5.3 Whitelist derivada das rotas (RN-009-03, desvios 005 e 052)

Em vez de `VALID_ENDPOINTS` + `dynamic_endpoints` (listas fixas que já estavam erradas, desvio 005), "rota registrada" = o roteamento do ASP.NET encontrou um endpoint para o caminho (`HttpContext.GetEndpoint() is not null`), **qualquer método** (um caminho existente com método errado é registrado e responde 405, como o Flask: o filtro dele não olha o método). Isso exige que o filtro rode **depois** do `UseRouting` implícito do `WebApplication` e **antes** de `StrictRoutingMiddleware` (§6, edição do `Program.cs`).
- O endpoint do fallback não existe (§6 usa middleware, não `MapFallback`), então "sem endpoint" = caminho desconhecido.
- Restrições de rota fazem parte da derivação: `/api/accounts/abc` casa com `{id}` sem restrição e vai ao `NumericIdFilter` da 002 (404 HTML, **sem** contar tentativa); uma rota com `{id:int}` ou `regex(^\d+$)` que não casa com `abc` fica **sem endpoint** e conta como desconhecida. Isto é mais estrito que o prefixo do Flask (`path.startswith(prefix)`), de propósito.
- `/api-keys/*` só tem endpoint com `API_KEYS_ROUTES_ENABLED=true`: desligada, o filtro vê caminho desconhecido (404 JSON e contagem), igual ao IP público hoje (desvio 052).
- Barra final (`/api/bills/`): o roteamento a casa; o `StrictRoutingMiddleware` devolve 404 HTML **sem** contar tentativa (o Flask só contava fora dos prefixos dinâmicos; diferença irrelevante).
- Trocar a lista fixa por derivação **abre** caminhos antes bloqueados pelo filtro (todas as rotas de `setup-*`, `/calendar-alerts/*`, `/api/agendamento*`); eles ficam protegidos por autenticação (desvios 002/003), não pelo 404 (já aprovado em 005).

### 5.4 O que é "suspeito" (RN-009-04)

Avaliado nesta ordem, na primeira que casar:
1. **User-Agent** (qualquer rota, registrada ou não): `Regex.IsMatch(ua, padrão, IgnoreCase | CultureInvariant)` para `sqlmap`, `nikto`, `nmap`, `masscan`, `acunetix`, `nessus`, `openvas`, `metasploit`, `havij`, `zgrab` ⇒ razão `Suspicious User-Agent: <padrão>`. UA ausente = vazio (não casa).
2. Caminho **não registrado** (§5.3): primeiro padrão de `SUSPICIOUS_PATTERNS` que casar (`re.search`, ignora caixa, sobre o caminho **decodificado**) ⇒ razão `Suspicious URL pattern: <padrão>`; se nenhum casar ⇒ razão `Unknown endpoint: <path>`. **Os dois resultados são "suspeito"**: para caminho desconhecido a diferença é só o texto da razão (aparece em `security-stats` e no log).
3. Caminho **registrado**: os padrões de URL **não são avaliados** (desvio 005). Hoje o padrão `/config` bloqueia `/admin/config-alertas-financeiros` e `/calendar-alerts/config/<id>`, e `/db` bloquearia `/api/user/addresses/db`; isso acaba.

Padrões de URL (copiados de `security.py:30-62`, na ordem; as strings abaixo são as expressões regulares literais):
`/\.env`, `/\.git`, `/phpMyAdmin`, `/phpmyadmin`, `/wp-admin`, `/wp-login`, `/xmlrpc\.php`, `/\.php`, `/cgi-bin`, `/shell`, `/config`, `/backup`, `/sql`, `/db`, `/database`, `/\_`, `/\.\.`, `/%2e%2e`, `/nice%20ports`, `/Tri%6Eity\.txt`, `/devicedesc\.xml`, `/dana-`, `/CFIDE`, `/geoserver`, `/fog/`, `/magento`, `/webui`, `/\.bak`, `/\.old`, `/\.backup`. (`/admin` foi removido no Flask e continua fora.) Ficam em `Domain/Security/SuspiciousRules` como constantes, não em configuração.

### 5.5 Tentativas e bloqueio (RN-009-05, RN-009-06)

`track_failed_attempt` (`security.py:368-428`):
1. Sem Redis ⇒ só loga `[SECURITY-ATTEMPT] IP: <ip> | Razão: <razão> (Redis indisponível)` (WARNING) e devolve "não bloqueou" ⇒ 404 JSON.
2. Lê `security:attempts:{ip}` (lista JSON de `{"timestamp","reason"}`), descarta as de mais de 10 min (JSON/campo inválido ⇒ lista vazia), acrescenta `{"timestamp":<agora naive isoformat>,"reason":<razão>}`, grava com TTL 600 s.
3. Se a lista tem **≥ 5**: `block_ip(ip, 60)` e apaga `security:attempts:{ip}`; devolve "bloqueou" ⇒ 403 "Too many suspicious requests. IP blocked.". Logs `[SECURITY-BLOCK] IP bloqueado: <ip> | Tentativas: <count> | Bloqueado até: <yyyy-MM-dd HH:mm:ss>` e `[SECURITY-AUTO-BLOCK] IP <ip> bloqueado automaticamente | Razão: <n> tentativas suspeitas`.
4. Senão loga `[SECURITY-ATTEMPT] IP: <ip> | Tentativa: <n>/5 | Razão: <razão>`.

`block_ip`: `blocked_until` = agora + 60 min; `count` = `count` anterior + 1 (ou 1); `first_blocked` = **agora** (o Flask sobrescreve a cada bloqueio, apesar do nome); `SETEX security:blocked:{ip} 3600`. Sem Redis: loga `[SECURITY-BLOCK] Redis indisponível, bloqueio não persistido: <ip>` e não bloqueia.
Constantes fixas (5, 10 min, 60 min): `AUTO_BLOCK_ENABLED`, `BLOCK_DURATION_MINUTES` e `MAX_SUSPICIOUS_ATTEMPTS` existem em `security_config.py:61-64` mas **nenhum código as lê**; o .NET também as ignora. Só `BOT_PROTECTION_ENABLED` é honrada.
Concorrência: leitura-modifica-grava sem atomicidade (como no Flask): rajadas simultâneas do mesmo IP podem contar menos tentativas; o nginx (20 r/s) e o bloqueio da 5ª limitam o efeito.

### 5.6 Fail-open (RN-009-08)

Sem Redis (`ICache.IsAvailableAsync` falso ou erro): blacklist e bloqueio **não** são consultados (ninguém é bloqueado), tentativas não são contadas, a resposta ao suspeito continua 404 JSON. O `ICache` nunca lança. Cada falta de Redis é logada no máximo uma vez por minuto (o Flask logava por requisição).

### 5.7 Formato das chaves Redis (RN-009-09)

Idêntico ao Flask, lido e escrito por `ICache` (`SetJsonAsync<string>(chave, jsonInternoSerializado, ttl)` e `GetJsonAsync<string>`), o que produz a **codificação dupla** (`redis_service.set_with_ttl` faz `json.dumps` em cima do `json.dumps` do módulo de segurança; AC-010-12). Exemplo do valor guardado no Redis: `"{\"blocked_until\": \"2026-10-04T11:00:00.123456\", \"count\": 1, \"first_blocked\": \"2026-10-04T10:00:00.123456\"}"` (com aspas externas).

| Chave | Valor interno | TTL |
|---|---|---|
| `security:blocked:{ip}` | `{"blocked_until","count","first_blocked"}` | 3600 s |
| `security:attempts:{ip}` | `[{"timestamp","reason"}, ...]` | 600 s |
| `security:blacklist:{ip}` | `{"ip","reason","blacklisted_at","permanent":true}` | 31 536 000 s |

Datas **naive** `isoformat` com microssegundos (`PythonDateFormat.IsoLocal`), no fuso de São Paulo via `IClock` (o Flask usa o relógio do container, UTC na prática; regra 8 manda fixar). As chaves `security:*` são apagadas na virada (integrations.md §7), então não há leitura cruzada de datas entre Flask e .NET. O .NET escreve o JSON interno compacto; ao ler aceita o formato do Python (`", "` e `": "`). `blocked_until` só decide bloqueio dentro do TTL do Redis; a comparação usa o mesmo relógio que escreveu.

### 5.8 Registro de tentativas de `x-api-key` inválida (proposta P-009-7)

Hoje um `x-api-key` errado em `/admin/*` responde 401 e **não** conta como tentativa: a chave pode ser adivinhada sem bloqueio (só o nginx limita a 5 r/s com burst 5). **Se aprovada**, o 401 de `AdminUnauthorized.EnvelopeB` chama `ISecurityStore.TrackAttemptAsync(ip, "Invalid API key")` (mesma regra 5 em 10 min ⇒ bloqueio de 60 min) quando o IP **não** é do bypass interno (§5.1). O 401 da requisição corrente não muda. Sem aprovação, nada disso acontece.

## 6. Fallback de 404 e erros sem endpoint (RN-009-13)

Problema: rota inexistente (`/foo`, `/api/x/y/z`, `/admin/inexistente`), segmento extra e id não numérico em rota sem restrição respondem hoje **404 de corpo vazio** do ASP.NET; o Flask devolve o HTML padrão (`FlaskNotFound`, já em `Api/Http/StrictRoutingMiddleware.cs`). Para IP externo o filtro do Flask devolvia o JSON `{"error":"Not found"}` (§5.2); o HTML aparece só para chamadas internas sem `X-Forwarded-For` e para caminhos registrados cujo handler decide 404.

Comportamento: depois do `StrictRoutingMiddleware`, o `UnmatchedRouteMiddleware` (`Api/Security/`) faz `if (context.GetEndpoint() is null) return FlaskNotFound.WriteAsync(context);` e **não** chama o próximo. Escolhas:
- **Middleware, não `MapFallback`:** o fallback `{*path}` casaria com qualquer método e **roubaria** o 405 do roteamento (`POST /connect-calendar/1` ⇒ 405 é requisito da 010); com middleware, o 405 é um endpoint "método não suportado" (endpoint não nulo) e segue intacto.
- 404 de handler (`Results.NotFound`, `FlaskNotFound` do `NumericIdFilter`) não passa por aqui: o endpoint existe.
- Corpo: o HTML do Flask, `Content-Type: text/html; charset=utf-8`, igual a `FlaskNotFound.Html`.
- A tentativa de caminho desconhecido por IP externo já foi contada e respondida em 404 JSON pelo filtro (§5.2) **antes**; este middleware só vê o que o filtro deixou passar (interno, `BOT_PROTECTION_ENABLED=false`).

Edição de `Program.cs` (**única** feature autorizada, ver `plan.md` §6): registrar `UseForwardedHeaders`, rate limiter, `SecurityFilterMiddleware`, e `UnmatchedRouteMiddleware` na ordem do `plan.md`, mais o sub-logger do `security.log`.

## 7. Headers de segurança (RN-009-11)

O Flask liga o Talisman (`SECURITY_HEADERS_ENABLED`, padrão `true`) em **todas** as respostas, inclusive 404/403 do filtro: `force_https=False`; HSTS `max-age=31536000` (com `includeSubDomains`, padrão do Talisman); `X-Frame-Options: DENY`; `Referrer-Policy: strict-origin-when-cross-origin`; `X-Content-Type-Options: nosniff`; CSP `default-src 'self'; script-src 'self' 'nonce-<aleatório>'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'`. O conjunto **exato** que o Talisman emite (`Permissions-Policy`, `X-XSS-Protection`, `Document-Policy`...) depende da versão instalada e **não** foi confirmado pela leitura estática: tarefa 009-0 T6 captura os headers reais (`curl -I`) e fixa o conjunto em um teste de contrato (Q-009-2). `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, HSTS e CSP são implementados por um middleware `SecurityHeadersMiddleware` (`Api/Security/`), desligável com `SECURITY_HEADERS_ENABLED=false` (mesmo nome do Flask). O nonce do CSP só serviria a HTML com `<script>` inline (páginas do OAuth da 010d); a 010d precisa ser revisada se usar script inline (Q-009-2).
CORS: `CORS_ENABLED` (padrão `false`) e `CORS_ORIGINS` não existem no .NET nesta feature: produção usa o proxy do Angular (`/api/*`) e o CORS está desligado hoje (Q-009-7).

## 8. Rate limit (RN-009-12, desvio 023)

Hoje: `Limiter(default_limits=["100 per hour"], key_func=get_remote_address, storage=Redis, strategy=fixed-window)` (`app/__init__.py:51-57`, `security_config.py:7-20`). Os limites por tipo (`api`, `webhooks`, `admin`, `RATELIMIT_SPECIFIC`) estão **declarados e nunca aplicados**: nenhum `@limiter.limit`. O `default_limits` aplica-se, em tese, a todas as rotas; com `remote_addr` igual ao IP do nginx todos os clientes dividem **um** balde de 100/hora, e o 429 do Flask sai em HTML. (Se isso de fato ocorresse em produção o painel travaria em minutos; Q-009-1 pede confirmação nos logs.)

.NET: `AddRateLimiter` com `PartitionedRateLimiter` por IP resolvido (§5.1), janela fixa. **Proposta de limites (a confirmar, condição do desvio 023):**

| Escopo | Limite proposto | Observação |
|---|---|---|
| Geral (qualquer rota, por IP real) | 300 / hora | o Flask declara 100/h; o painel Angular faz ~8 chamadas por tela |
| `/admin/*` | 60 / hora | o Flask declara `5 per minute; 50 per hour` (morto); nginx já limita 5 r/s |
| Isentos | `GET /`, `GET /api/health`, chamadas internas (IP confiável sem `X-Forwarded-For`: bot, Ofelia, health do compose) | |

- **Padrão: desligado** (`RATE_LIMIT_ENABLED=false`) até o Rafael confirmar os números (P-009-5); com a flag ligada, os valores vêm de `RATE_LIMIT_PER_HOUR` (geral) e `RATE_LIMIT_ADMIN_PER_HOUR`.
- 429 em JSON, envelope D: `{"error":"Too many requests","message":"Rate limit exceeded. Try again later."}` com `Retry-After` (segundos até o fim da janela). Textos **novos** (o Flask devolvia o HTML do Flask-Limiter).
- Armazenamento em memória do processo (uma instância): zera a cada reinício. As chaves do Flask-Limiter no Redis são apagadas na virada e não são lidas.
- Posição no pipeline: **antes** do filtro de segurança (no Flask o `Limiter(app)` registra o seu `before_request` antes do `security_filter`).

## 9. Logs e `security.log` (RN-009-14)

- Arquivo `security.log` em `LogDirectory` (padrão `logs/`; `SECURITY_LOG_FILE` do Flask, padrão `logs/security.log`, vira `SECURITY_LOG_FILE` também), nível mínimo `SECURITY_LOG_LEVEL` (padrão `WARNING`), formato `[yyyy-MM-dd HH:mm:ss] LEVEL: mensagem`, e também no console (o Flask adiciona `StreamHandler`). Alimentado por um sub-logger do Serilog filtrado pela categoria `MeuSecretario.Security` (edição do `Program.cs`).
- Mensagens: exatamente as dos §4.1 a §4.4 e §5.2 a §5.5 (prefixos `[SECURITY-*]`); o `fail2ban` e o `monitor-security.sh` do host leem o log do **nginx**, não este, mas os prefixos ficam estáveis para quem faz `grep`.
- **Sanitização** (nova, só no log): `ip`, `path`, `razão` e `reason` têm CR, LF e demais caracteres de controle trocados por `?` antes de gravar (o Flask permitia forjar linhas de log com `\n` no caminho). Não se registra corpo, `x-api-key`, `Authorization`, cookie nem User-Agent completo (a razão cita só o **padrão** casado).
- Erros 500 das rotas admin: log `SafeError.Describe` + nome da rota; nunca a exceção completa nem a mensagem.

## 10. Regras de negócio

- **RN-009-01** IP do cliente resolvido por `ForwardedHeaders` (direita para esquerda, rede privada confiável); sem `X-Forwarded-For` e origem confiável ⇒ bypass interno. *Origem: `security.py:166-180, 430-451`; desvio 023.*
- **RN-009-02** Ordem: bypass ⇒ blacklist (403) ⇒ bloqueio temporário (403) ⇒ suspeito (tentativa; 404 ou 403 na 5ª) ⇒ segue. *Origem: `security.py:453-493`.*
- **RN-009-03** Whitelist = existência de endpoint; padrões de URL só para caminho sem endpoint; User-Agent vale sempre. *Origem: `security.py:182-222`; desvios 005, 052.*
- **RN-009-04** Listas de padrões e User-Agents copiadas literalmente (§5.4). *Origem: `security.py:30-76`.*
- **RN-009-05** 5 tentativas em 10 min ⇒ bloqueio; TTL das tentativas 600 s. *Origem: `security.py:368-428`.*
- **RN-009-06** Bloqueio de 60 min, `count` acumulado, `first_blocked` regravado. *Origem: `security.py:326-366`.*
- **RN-009-07** Respostas do filtro no envelope D com os textos exatos (§5.2). *Origem: `security.py:459-490`.*
- **RN-009-08** Fail-open sem Redis; `ICache` nunca lança. *Origem: `security.py:224-232, 303-312, 372-377`; `integrations.md` §7.*
- **RN-009-09** Chaves `security:{blocked,attempts,blacklist}:{ip}` com JSON duplamente codificado e os TTLs da tabela. *Origem: `security.py:19-27`; `redis_service.py:52-62`; AC-010-12.*
- **RN-009-10** Autenticação de admin por `x-api-key` em tempo constante; 401 envelope B; antes de ler o corpo. *Origem: `decorators.py:16-40`.*
- **RN-009-11** Headers de segurança do Talisman (§7). *Origem: `app/__init__.py:65-80`.*
- **RN-009-12** Rate limit por IP real, JSON 429, padrão desligado (§8). *Origem: `app/__init__.py:51-58`; desvio 023.*
- **RN-009-13** Sem endpoint ⇒ 404 HTML do Flask; 405 preservado (§6). *Origem: Flask; `StrictRoutingMiddleware.cs`.*
- **RN-009-14** `security.log` com os prefixos do Flask, sem segredo, com sanitização (§9). *Origem: `app/__init__.py:30-47`.*
- **RN-009-15** `security-unblock-ip` limpa bloqueio temporário, tentativas **e** blacklist; resposta 200 sempre. *Origem: `admin.py:78-81`.*
- **RN-009-16** `gemini-cache-clear`: ramo usuário > padrão > tudo; `pattern` com usuário é o tipo puro. *Origem: `cache_management.py:190-205`.*
- **RN-009-17** `get-notification-config` cria a linha se não existir. *Origem: `notification_config_service.py:115-137`.*
- **RN-009-18** Setup: no-op autenticado, sem I/O, sem segredo na resposta. *Origem: desvio 002.*
- **RN-009-19** `trigger-agenda-notifications`: 200 de substituição (desvio 028). *Origem: `notification_triggers.py:31-56`.*
- **RN-009-20** Datas de negócio das rotas de teste/trigger em São Paulo (`IClock`), nunca o relógio do container. *Origem: `testing.py:200`; constituição regra 8.*

## 11. Critérios de aceite

### Filtro (009-0)

- **AC-009-01** — **Dado** IP `203.0.113.7` (via `X-Forwarded-For`) sem bloqueio **Quando** `GET /rota-que-nao-existe` **Então** 404 `{"error":"Not found"}` JSON e `security:attempts:203.0.113.7` tem 1 item.
- **AC-009-02** — **Dado** 4 tentativas anteriores no mesmo IP **Quando** a 5ª requisição suspeita chega **Então** 403 `{"error":"Access denied","message":"Too many suspicious requests. IP blocked."}`, `security:blocked:<ip>` existe com `count` 1 e `security:attempts:<ip>` foi apagada.
- **AC-009-03** — **Dado** IP bloqueado **Quando** qualquer rota (inclusive válida) é chamada **Então** 403 `{"error":"Access denied","message":"Your IP has been temporarily blocked due to suspicious activity"}`.
- **AC-009-04** — **Dado** IP na blacklist **Quando** `GET /` **Então** 403 `{"error":"Access permanently denied","message":"Your IP has been permanently blocked"}` (a blacklist vem antes do bloqueio temporário).
- **AC-009-05** — **Dado** `Remote` loopback e **sem** `X-Forwarded-For` **Quando** `GET /rota-que-nao-existe` **Então** 404 **HTML** do Flask e nenhuma chave em `security:*` (bypass interno).
- **AC-009-06** — **Dado** `Remote` privado com `X-Forwarded-For: 198.51.100.9` **Quando** `GET /rota-que-nao-existe` **Então** o filtro roda para `198.51.100.9`. Com `X-Forwarded-For: 1.1.1.1, 198.51.100.9` e peer privado, o IP é `198.51.100.9` (o mais à direita não confiável; `1.1.1.1` forjado é ignorado; P-009-6).
- **AC-009-07** — **Dado** User-Agent `sqlmap/1.7` **Quando** `GET /` (rota válida) **Então** 404 JSON e tentativa registrada com razão `Suspicious User-Agent: sqlmap`.
- **AC-009-08** — **Dado** rota registrada com trecho suspeito (`GET /admin/config-alertas-financeiros`, `GET /api/user/addresses/db`) **Quando** chamada por IP externo com credencial válida **Então** chega ao handler; nenhuma tentativa contada (desvio 005).
- **AC-009-09** — **Dado** `/api-keys/health` e `API_KEYS_ROUTES_ENABLED=false` **Quando** `GET` por IP externo **Então** 404 JSON e tentativa contada; com a flag ligada, 200.
- **AC-009-10** — **Dado** Redis fora do ar **Quando** chega requisição suspeita **Então** 404 JSON, nada gravado, nenhum bloqueio, nenhuma exceção.
- **AC-009-11** — **Dado** valor `security:blocked:<ip>` com JSON inválido **Quando** o IP faz uma requisição **Então** a chave é apagada e a requisição segue.
- **AC-009-12** — **Dado** bloqueio gravado por `block_ip` **Quando** o valor bruto é lido do Redis **Então** é um texto JSON (aspas externas) cujo conteúdo, depois de decodificado, é outro JSON com `blocked_until`, `count`, `first_blocked`, e o TTL ≈ 3600 s (codificação dupla do Python).
- **AC-009-13** — **Dado** um valor `security:blacklist:<ip>` escrito pelo Python (fixture) **Quando** o filtro consulta **Então** reconhece o IP como blacklistado (compatibilidade de leitura).
- **AC-009-14** — **Dado** tentativas com timestamp de 11 min atrás **Quando** nova tentativa chega **Então** só a nova conta (`len` = 1).
- **AC-009-15** — **Dado** `POST /connect-calendar/1` (rota existe só para GET) **Quando** chamada **Então** 405 (não 404 HTML) e nenhuma tentativa contada.
- **AC-009-16** — **Dado** `GET /api/accounts/` (barra final) **Quando** chamada por IP interno **Então** 404 HTML do Flask (já da 001); **Dado** `GET /nao-existe/a/b/c` interno **Então** 404 HTML do Flask, `Content-Type: text/html` (o fallback).
- **AC-009-17** — **Dado** caminho com `\n` ou `\r` **Quando** a tentativa é logada **Então** a linha de `security.log` não contém o caractere de controle.
- **AC-009-18** — **Dado** `RATE_LIMIT_ENABLED=true` e limite geral 3 **Quando** o 4º pedido do mesmo IP real chega **Então** 429 JSON envelope D com `Retry-After`; um segundo IP real (outro `X-Forwarded-For`) ainda passa; chamada interna sem header e `GET /api/health` nunca recebem 429.
- **AC-009-19** — **Dado** resposta qualquer (inclusive 404 do filtro) **Quando** `SECURITY_HEADERS_ENABLED` está ligado **Então** leva `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, HSTS e CSP (conjunto final fixado na T6).
- **AC-009-20** — **Dado** `BOT_PROTECTION_ENABLED=false` **Quando** caminho suspeito **Então** o filtro não age e o fallback devolve 404 HTML.

### Admin (009a)

- **AC-009-21** — **Dado** `x-api-key` ausente, vazio ou errado **Quando** qualquer rota `/admin/*` (as 33) **Então** 401 `{"status":"erro","mensagem":"Não autorizado"}` e o corpo nem é lido.
- **AC-009-22** — **Dado** `POST /admin/security-unblock-ip` com `{"ip":"  1.2.3.4 "}` **Quando** o IP tem bloqueio, tentativas e blacklist **Então** as três chaves somem e a resposta é 200 `{"status":"sucesso","mensagem":"IP 1.2.3.4 desbloqueado"}`; sem Redis ou sem chaves, 200 igual; `ip` ausente/em branco/número ⇒ 400 `Campo 'ip' obrigatório`.
- **AC-009-23** — **Dado** bloqueios, blacklist e tentativas gravados **Quando** `GET /admin/security-stats` **Então** 200 com as três listas nos formatos do §4.2, `time_remaining` `H:MM:SS`, ordem por `ip`, `redis_connected:true`; entrada corrompida ignorada; sem Redis: `error:"Redis não disponível"` e sem `redis_connected`.
- **AC-009-24** — **Dado** `POST /admin/security-blacklist-add` `{"ip":"9.9.9.9"}` **Quando** Redis disponível **Então** 200 com `reason:"Manual block via API"`, chave com TTL ≈ 31 536 000 s, e o IP passa a receber 403 permanente; `{"ip":""}` ⇒ 400 `Campos obrigatórios faltando: ip`; sem Redis ⇒ 500 com o texto `Falha ao adicionar IP à blacklist (Redis indisponível)`. `remove` ⇒ 200 mesmo sem a chave; sem Redis ⇒ 500 do §4.4.
- **AC-009-25** — **Dado** chaves `gemini_cache:category:7:aaa`, `gemini_cache:intent:global:bbb`, `gemini_cache:intent:7:ccc` **Quando** `gemini-cache-clear` com `{"usuario_id":7,"pattern":"category"}` **Então** apaga só a primeira (`1 chaves deletadas para usuário 7`, `keys_deleted:1`); com `{"usuario_id":7,"pattern":"category:*"}` apaga 0; com `{"pattern":"intent:*"}` apaga as duas de `intent`; corpo vazio apaga tudo (`Cache completo limpo: 3 chaves deletadas`); corpo `[1]` ⇒ 400.
- **AC-009-26** — **Dado** métricas registradas pela 010c **Quando** `GET /admin/gemini-cache-stats` **Então** 200 com o formato do §4.6 (nove tipos na ordem, `hit_rate` `"66.7%"`).
- **AC-009-27** — **Dado** `baileys_auth` com 3 linhas `baileys_session` e 2 de outra sessão **Quando** `POST /admin/clear-bot-session` **Então** 200 com `linhas_deletadas:3` e a mensagem do §4.7; as outras 2 permanecem.
- **AC-009-28** — **Dado** usuário sem `notificationconfigs` **Quando** `GET /admin/get-notification-config/<id>` **Então** 200 com `07:00`, `true`, `true` e a linha criada; usuário inexistente ⇒ 404 (P-009-4) ou 500 de texto fixo sem ela; `abc`/`-1` ⇒ 404 HTML antes da auth.
- **AC-009-29** — **Dado** `POST /admin/config-alertas-financeiros` **Quando** `{"usuario_id":1,"ativo":false}` **Então** 200 `Alertas financeiros desativados com sucesso` e `configuracao.alertas_financeiros_ativos=false` no banco; `{}` ⇒ 400 `Campos obrigatórios faltando: usuario_id`; `{"usuario_id":1}` e `{"usuario_id":1,"ativo":null}` ⇒ 400 `Campo 'ativo' é obrigatório (true ou false)`.
- **AC-009-30** — **Dado** erro inesperado (banco indisponível, falha injetada) em qualquer rota **Quando** chamada **Então** 500 com o texto fixo da rota; o corpo e o log não contêm a mensagem da exceção.

### Setup e diagnóstico (009b)

- **AC-009-31** — **Dado** `x-api-key` válida **Quando** cada uma das 13 rotas de setup (10 a 22) **Então** 200 com a mensagem "gerenciado por migrations" no formato da rota (texto/HTML `<pre>`/envelope B), **nenhuma** consulta ao banco e nenhum `user_api_key_para_automate`, `tabelas_criadas` ou `planos_inseridos` na resposta.
- **AC-009-32** — **Dado** `GET /admin/oauth-config-check` **Quando** `GOOGLE_CLIENT_ID` e `GOOGLE_CLIENT_SECRET` configurados **Então** 200 com `client_id_configured:true`, `client_secret_configured:true`, `redirect_uri` e **sem** `client_id_prefix`; o corpo não contém o client id nem o secret (teste com valores sentinela).
- **AC-009-33** — **Dado** usuário 1 conectado (fake de `IGoogleAccountService`/`ICalendarClient`) **Quando** `GET /admin/debug-calendar` **Então** 200 HTML `<pre>` com as 5 etapas e `Token: [oculto]`; o corpo não contém o token sentinela; usuário desconectado ⇒ linhas com `<br>` sem `<pre>`.

### Dependentes (009c)

- **AC-009-34** — **Dado** porta `IScheduleMotor` fake **Quando** `POST /admin/run-motor-agendamentos` **Então** 200 `Agendamentos processados.`; `AlreadyRunning` ⇒ 200 com a mensagem do §4.13; exceção ⇒ 500 de texto fixo; sem `x-api-key` ⇒ 401 e a porta não é chamada.
- **AC-009-35** — **Dado** portas fake **Quando** cada `trigger-*` **Então** o corpo do §4.14 (chaves e `mensagem` exatas); `trigger-agenda-notifications` ⇒ 200 de substituição sem chamar porta alguma.
- **AC-009-36** — **Dado** `POST /admin/test-notification` **Quando** usuário inexistente com `tipo` inválido **Então** 404 (a existência vem antes do `tipo`); usuário existente e `tipo:"x"` ⇒ 400 `Tipo inválido`; `agenda` sem Google ⇒ 400 `Usuário não conectou Google Calendar`; `contas` com 7 agendamentos no dia de amanhã ⇒ mensagem com 5 linhas; bot responde falso ⇒ 500 `Falha ao enviar notificação`.
- **AC-009-37** — **Dado** `POST /admin/test-monthly-report/<id>?momento=XX` **Então** 400 do §4.16; porta com falha de negócio ⇒ 400 com `mensagem:"Erro ao enviar relatório"` e `erro:<motivo>`; sucesso ⇒ 200 com as chaves do §4.16.
- **AC-009-38** — **Dado** `POST /admin/test-daily-briefing` **Quando** `{"usuario_id":99}` inexistente **Então** 404 `Usuário 99 não encontrado`; sem WhatsApp ⇒ 400; sucesso ⇒ 200 com `preview_mensagem` de até 200 caracteres + `...`.
- **AC-009-39** — **Dado** rotas da 009c com as portas ainda **não** implementadas (013/012 pendentes) **Quando** chamadas com `x-api-key` válida **Então** 503 `{"status":"erro","mensagem":"Funcionalidade ainda não disponível"}` (implementação padrão `Unavailable*`, `TryAdd`; a 013 a substitui).

## 12. Tabelas, Redis e dados tocados

| Recurso | Uso | Observação |
|---|---|---|
| Redis `security:blocked:*`, `security:attempts:*`, `security:blacklist:*` | leitura/escrita/`SCAN` | §5.7; o `SCAN` evita `KEYS` bloqueante |
| Redis `gemini_cache:*` | apaga (via `ILlmCacheAdmin`) | 010c |
| Redis `job_lock:motor-agendamentos` | só a 013 | fora desta spec |
| `baileys_auth` (`session_id`, `data_key`, `data_value`; PK composta) | `DELETE` por `session_id = 'baileys_session'` | tabela compartilhada com o bot Node |
| `notificationconfigs` (`usuario_id` UNIQUE, FK `ON DELETE CASCADE`) | leitura, `INSERT` de linha padrão, `UPDATE alertas_financeiros_ativos` | **não** tem `agenda_diaria_*` (desvio 028); sem CHECK de horário no banco de produção |
| `usuarios`, `agendamentos` | leitura nas rotas `test-*` | 009c |
| nenhuma outra | | as rotas de setup não tocam o banco |

Tenant: não há usuário autenticado em `/admin/*`; o `usuario_id` do caminho/corpo vale para qualquer usuário (credencial administrativa; o `x-api-key` é o mesmo segredo do JWT, desvio 006). **Migrations: nenhuma** (faixa 0080–0089 reservada e não usada).

## 13. Desvios de paridade

Aplicados (aprovados): ver a tabela de abertura. Não aplicáveis: 001, 007, 009 a 012, 014 a 022, 025 a 027, 029 a 051, 053 a 064. O desvio 028 contém uma imprecisão sobre `test-notification` (§4.15).

## 14. Desvios propostos (precisam de aprovação)

Sem aprovação, o .NET reproduz o Flask no que é determinável; onde o Flask só "funciona" por acidente, o texto diz o que acontece.

| Id | Atual (arquivo:linha) | Proposta | Impacto | Recomendação |
|---|---|---|---|---|
| **P-009-1** | `gemini-cache-clear` usa `request.get_json()` estrito (`cache_management.py:190`): sem `Content-Type: application/json` ou com corpo vazio vira 415/400 e o `handle_errors` o devolve como 500 com `str(e)`; o docstring promete "sem body limpa TUDO" (`:181`) | corpo **vazio** = limpar tudo (200); corpo não vazio inválido/não-objeto = 400 `Corpo da requisição deve ser um objeto JSON` (mensagem nova) | quem chamava sem corpo recebia 500; passa a limpar o cache (operação de baixo risco, só custo de quota) | Aprovar |
| **P-009-2** | `ip` de `blacklist-add/remove` e `unblock` aceito sem validação nem normalização (`security.py:307-309, 346-347`; `admin.py:73-76`); `add` não faz `trim`, `unblock` faz; qualquer texto vira chave Redis e linha de log; IPv6 em formas diferentes (`::1` × `0:0:0:0:0:0:0:1`) não casam com o IP que o filtro resolve | validar com `IPAddress.TryParse` (depois do `trim`), normalizar com `ToString()` e responder 400 `IP inválido` (mensagem nova) em `add`/`remove`/`unblock`; o filtro usa a mesma normalização | rejeita lixo e IPs com forma diferente do que o filtro compara; ninguém conhecido usa | Aprovar |
| **P-009-3** | corpo com tipos inesperados: `ip` número/lista/`true` aceitos e gravados como chave (`security.py:308`); `reason` de qualquer tipo; `usuario_id` texto numérico aceito por coerção do banco (`notification_config.py:422`); `ativo` truthy qualquer (`"false"` ⇒ mensagem "ativados" e gravação `false`, `:425, notification_config_service.py:366`) | tipos estritos como 008-048: `ip`/`reason` texto, `usuario_id` inteiro JSON, `ativo` booleano; erro cai na validação "obrigatório" da rota; mensagens novas: `Campo 'reason' deve ser texto`, `Campo 'usuario_id' deve ser um número inteiro` (cache) | clientes que mandavam `"5"` ou `"false"` recebem 400 | Aprovar |
| **P-009-4** | `get-notification-config` e `config-alertas-financeiros` para usuário inexistente: o `INSERT` viola a FK e a rota devolve 500 com `str(e)` (`notification_config_service.py:127-129`; `decorators.py:129-143`) | 404 `{"status":"erro","mensagem":"Usuário não encontrado"}` (como o desvio 043 da 008) | só muda 500 por 404 | Aprovar |
| **P-009-5** | rate limit declarado e nunca aplicado de verdade (§8); desvio 023 exige confirmar números | limites do §8, **ligado só depois** da sua confirmação (`RATE_LIMIT_ENABLED` padrão false) | sem a flag nada muda; com ela, 429 JSON | Aprovar os valores ou indicar outros (Q-009-1) |
| **P-009-6** | IP do cliente = **primeiro** da lista `X-Forwarded-For` (`security.py:447`): o cliente forja o primeiro elemento, escapa do bloqueio (rotaciona) ou incrimina outro IP | `ForwardedHeaders` da direita para a esquerda sobre a rede privada confiável (§5.1) | muda só quando o cliente envia o próprio `X-Forwarded-For`; o fluxo normal (nginx) resolve o mesmo IP | Aprovar |
| **P-009-7** | `x-api-key` errado em `/admin/*` responde 401 sem contar tentativa (`decorators.py:33-38`): adivinhação sem bloqueio além do nginx (5 r/s). O plan 008 §7 deixou "limite de tentativas inválidas" para a 009 | contar a tentativa (razão `Invalid API key`) no `ISecurityStore` quando o IP não é interno; 5 em 10 min ⇒ bloqueio de 60 min (§5.8) | um operador que erre a chave 5 vezes em 10 min fica bloqueado 1 h (chamadas internas não contam; `security-unblock-ip` por um IP ainda não bloqueado resolve) | Aprovar |
| **P-009-8** | `clear-bot-session` usa o literal `'baileys_session'` (`setup_service.py:22`), mas o bot atual identifica sessões por `BOT_SESSION_NAME=sessao-principal` (integrations.md §3); se o bot usa outro `session_id` em `baileys_auth`, a rota apaga 0 linhas e o operador acha que limpou | manter o literal (paridade) **e** registrar no log o número de linhas; decidir após a resposta da Q-009-3 se a rota deve aceitar `session_id` opcional no corpo (padrão `baileys_session`) | nenhum se mantido | Manter (rejeitar a mudança) até Q-009-3 |

## 15. Fora de escopo

- Implementação do motor de agendamentos, dos jobs e dos relatórios (013) e dos textos do briefing (012): aqui só contrato e portas.
- Rotas `/calendar-alerts/*`, `/disconnect-calendar`, `/api/fatura/*/reprocessar`: desvio 003 também as cobre, mas pertencem a 014 e 004/011.
- CORS (Q-009-7), HTML 500 do Flask para exceção não tratada e HTML do 405 (decisão G-5): não reproduzidos.
- Separar a chave do JWT da chave de admin (desvio 006, pós-virada).
- Atualizar o Ofelia, o bot e o UptimeRobot para a nova porta/URL (cutover).

## 16. Perguntas em aberto

- **Q-009-1** O rate limit do Flask está de fato aplicando 100/h num balde único em produção? Conferir `docker logs meu-secretario-api | grep 429` (e `grep "limit"` no nginx). Confirmar os limites do §8 e se `/webhook-whatsapp` (bot interno, hoje sem bypass no Flask) deve ficar isento.
- **Q-009-2** Capturar os headers reais do Flask (`curl -I http://<prod>/` e um 404) para fixar o conjunto do Talisman (§7); as páginas HTML da 010d (OAuth) usam `<script>` inline que o CSP `script-src 'self'` bloquearia?
- **Q-009-3** Quais `session_id` existem hoje em `baileys_auth` em produção? (`SELECT session_id, count(*) FROM baileys_auth GROUP BY 1`.) O literal `baileys_session` ainda é o usado pelo bot?
- **Q-009-4** Cutover: o Ofelia chama `http://localhost:5000` e o .NET escuta em `8000`; atualizar os labels do compose (`docker-compose.yml:66, 71`) ou publicar o .NET também em 5000. Quem chama os `trigger-*` de hora em hora (UptimeRobot?) e para qual host?
- **Q-009-5** Mantemos `Erro ao <ação>. Tente novamente.` como padrão de 500 do admin (decisão análoga à G-7)?
- **Q-009-6** `debug-calendar` fixa o usuário 1; aceitar `?usuario_id=` opcional (padrão 1)? Preferi não acrescentar parâmetro sem pedido.
- **Q-009-7** `CORS_ENABLED`/`CORS_ORIGINS` estão ligados no `.env` de produção? Se sim, abrir uma entrega 009d.
- **Q-009-8** `run-motor-agendamentos`: o motor roda inline (o bot espera até 30/60 s) ou a porta devolve rápido e processa em segundo plano? `AlreadyRunning` responde 200 ou 409? Decisão da 013, mas o contrato HTTP é desta spec.
- **Q-009-9** O desvio 028 diz que `test-notification tipo=agenda` quebra por `agenda_diaria_*`; o código lido não usa essas colunas (§4.15). Confirmar com um teste manual em homol antes de implementar a 009c; se a rota realmente quebrar por outro motivo, atualizar o desvio.
- **Q-009-10** Os 31 do cabeçalho de `admin.py` × 33 contados: confirmar com `flask routes` em homol que não há duas rotas a menos (alguma registrada condicionalmente).
