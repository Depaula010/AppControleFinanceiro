# Integrações, Redis e compatibilidade criptográfica

## 1. Compatibilidade criptográfica (obrigatória)

Sem estes quatro itens, usuários são deslogados, atalhos do iPhone param e o bot é rejeitado.

| Item | Especificação | Teste de aceite |
|---|---|---|
| **JWT** | HS256; chave `API_SECRET_KEY`; claims `user_id` (int), `exp` (UTC +24h), `iat`; sem `iss`/`aud`/`sub`/`jti`. No .NET: `MapInboundClaims=false`, `ValidateIssuer=false`, `ValidateAudience=false`, `ClockSkew=0`. | Token emitido pelo Flask é aceito pelo .NET e vice-versa (fixture gerada em Python). |
| **Hash de senha (Werkzeug)** | `pbkdf2:sha256:<iter>$<salt>$<hex>`: PBKDF2-HMAC-SHA256, saída 32 bytes em hex minúsculo, salt (16 chars alfanuméricos) usado como **bytes UTF-8** (não base64), iterações lidas da string (Werkzeug 3.1 usa 1.000.000). `scrypt:<N>:<r>:<p>$<salt>$<hex>` (padrão do Werkzeug 3 sem `method`, usado por `definir_senha_manual.py`): **é o formato real do único usuário em produção (`scrypt:32768:8:1`)**; scrypt com salt em bytes UTF-8, `dklen = 64` (padrão de `hashlib.scrypt`) saída em hex; a memória usada é `128·N·r` = 32 MiB. Cadastro novo no .NET: `pbkdf2:sha256` com as mesmas regras. Senha passa por `.strip()`. | Hashes `pbkdf2` e `scrypt` gerados pelo Python verificados no .NET. |
| **Fernet** | Chave `ENCRYPTION_KEY` (base64 url-safe, 32 bytes). Token: `0x80 ‖ timestamp(8) ‖ IV(16) ‖ AES-128-CBC(PKCS7) ‖ HMAC-SHA256`. Cifra `chave_api_criptografada` (3 linhas em produção) e possivelmente tokens do Google. **`api_key_automate` está em texto puro em produção** (coluna `varchar(100)` pequena demais; desvio 017). Leitura legada: valor em texto puro aceito se não começar com `gAAAAA` com ≥ 40 chars. | Vetores oficiais da especificação + token gerado pelo Python decifrado no .NET. |
| **HMAC do webhook** | `X-Webhook-Signature` = HMAC-SHA256 hex do **corpo bruto** com `WEBHOOK_SIGNATURE_KEY` (padrão `API_SECRET_KEY`). Opcional; se presente e inválido ⇒ 401. | Corpo bruto lido antes do model binding. |

`x-api-key`: comparar com `CryptographicOperations.FixedTimeEquals`.

## 2. Variáveis de ambiente (mesmos nomes dos secrets atuais)

| Variável | Obrigatória | Observação |
|---|---|---|
| `DATABASE_URL` | sim | formato `postgresql://user:pass@host:5432/db` (senha com URL-encoding); converter para connection string Npgsql; aceitar `postgres://` |
| `API_SECRET_KEY` | sim, ≥ 32 chars | JWT + `x-api-key` |
| `ENCRYPTION_KEY` | sim | Fernet |
| `GEMINI_API_KEY` | sim | chave do sistema |
| `REDIS_URL` | sim | `redis://redis:6379` |
| `WEBHOOK_SIGNATURE_KEY` | não | padrão `API_SECRET_KEY` |
| `BOT_WHATSAPP_URL`, `BOT_SESSION_NAME` (padrão `sessao-principal`), `BOT_SESSION_ID` (override), `BOT_API_KEY` | sim exceto SESSION_ID | |
| `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REDIRECT_URI` | sim | confirmar URI real no Google Cloud Console |
| `WEATHER_API_KEY`, `OPENROUTE_API_KEY`, `OPENROUTE_BASE_URL`, `TRAVEL_TIME_DAILY_LIMIT` (10) | sim/não | |
| `TZ` | — | irrelevante: o app fixa `America/Sao_Paulo` |

Variáveis novas do .NET (ex.: `ASPNETCORE_URLS=http://+:8000`) entram em três lugares: heredoc do `.env` no workflow, `environment` do compose e, se secreta, nos secrets do GitHub.

## 3. Bot WhatsApp

**Saída** (`notification_service.py`):
1. Resolver sessão: `GET {BOT_WHATSAPP_URL}/api/v1/sessions`, header `X-API-Key: BOT_API_KEY`; espera `{"data":[{"session_name","session_id"}]}`; cache Redis `bot_session_id:{BOT_SESSION_NAME}` 600s. `BOT_SESSION_ID`, se definido, ignora a resolução.
2. Texto: `POST {url}/api/v1/sessions/{session_id}/send-message` `{"numero","mensagem"}`, timeout 10s.
3. Imagem: `POST .../send-image` `{"numero","imagem":<base64>,"legenda"}`, timeout 30s.
4. Retry: em 404 invalida o cache e resolve de novo, uma vez. Sucesso = `status == 200`.
5. Fallback legado (sem sessão): `POST {url}/enviar-mensagem` / `/enviar-imagem` com `x-api-key: API_SECRET_KEY`.

**Entrada**: `POST /webhook-whatsapp` — headers `x-api-key` (obrigatório) e `X-Webhook-Signature` (opcional); corpo `{texto, numero_remetente, media_data?, media_type?, media_filename?}`; `numero_remetente` cortado no `@`. Resposta síncrona (envelope E); o Gemini roda dentro da requisição (timeout 120s no nginx). `client_max_body_size 10M` por causa de mídia em base64.

**Outras chamadas do bot ao backend**: `POST /admin/run-motor-agendamentos` (com `x-api-key`). O bot também lê/escreve `baileys_auth` no mesmo banco; `POST /admin/clear-bot-session` apaga `baileys_auth WHERE session_id = 'baileys_session'`.

Não identificado no repositório: a URL que o bot usa para chamar o backend (nome do container na `rede-global` ou IP). Confirmar no servidor antes da virada (ver `cutover-runbook.md`).

## 4. Gemini (`gemini_service.py`, 1888 linhas)

- Modelo `gemini-2.5-flash-lite`; ~30 funções de prompt (`get_message_intent` L175, `categorize_transaction` L119, vários `extract_*`, `generate_daily_briefing` L1256). **Os prompts são portados literalmente** para `ILlmClient` (arquivos de texto versionados em `Infrastructure/Llm/Prompts/`), pois são o contrato visível ao usuário.
- Chave por usuário via `gerenciador_chaves_api.py`; no .NET, passada por chamada.
- Cache `gemini_cache:{tipo}:{user|global}:{md5[:12]}` com TTL por tipo (24h a 30d; padrão 3d). Métricas passam a ser por instância (Redis `INCR` opcional).
- Erro com `429`, `quota` ou `rate limit` na mensagem ⇒ resposta 429 `{"status":"erro","resposta":"⚠️ *Limite de IA Excedido*..."}` no webhook.

## 5. Google (Calendar + Drive)

- Escopos `calendar` e `drive.file`; `access_type=offline`, `prompt=consent`.
- `state` = `usuario_id` em base64 url-safe, **sem assinatura** (desvio 021: assinar com HMAC e expirar).
- Tokens em `googlecalendartokens`; upsert com `COALESCE(EXCLUDED.refresh_token, refresh_token)`; `needs_reconnect` quando o refresh falha.
- Revogação: `POST https://oauth2.googleapis.com/revoke`.
- `GET /oauth2callback` devolve HTML e notifica por WhatsApp; `GET /connect-calendar/<id>` redireciona (302) ou devolve HTML "já conectado".
- Drive: upload por `MediaIoBaseUpload` acionado pelos intents de drive.

## 6. APIs de clima e rotas

- **WeatherAPI.com**: `GET http://api.weatherapi.com/v1/current.json` e `/forecast.json` (`key`, `q`, `lang=pt`), timeout 5s. Hoje é HTTP sem TLS; usar HTTPS (suportado) — desvio 022.
- **OpenRouteService**: `GET /geocode/search` (`api_key`, `text`, `boundary.country=BR`, `size=1`) e `POST /v2/directions/driving-car` (header `Authorization`, corpo `{"coordinates":[[lon,lat],[lon,lat]]}`), timeout 15s, 2 tentativas, limite diário `travel_time_limit:{uid}:{data}` (86400s, padrão 10).
- Validação de chave do usuário (`POST /api/user/api-keys/validate`): Generative Language `/v1beta/models`, OpenWeatherMap, ORS; 503 em timeout/rede.
- E-mail: nenhum envio no sistema.

## 7. Chaves Redis (mesmo Redis, mesmas chaves)

Formato: `redis_service` grava valores com `json.dumps` (`SETEX`, `SET NX EX`); `set_if_not_exists` grava cru; o módulo de segurança **codifica JSON duas vezes** (`security.py` + `redis_service.py:52`).

| Chave | Finalidade | TTL |
|---|---|---|
| `pending_tx:{whatsapp}:{tx_id}`, `last_pending:{whatsapp}` | transação aguardando confirmação | 300s |
| `pending_event:{whatsapp}:{event_id}` | evento de agenda aguardando confirmação | 300s |
| `nightly_checkin:{whatsapp}:{id}`, `nightly_checkin_active:{whatsapp}` | sessão do check-in | 3600s |
| `security:blocked:{ip}` | bloqueio temporário `{blocked_until,count,first_blocked}` | 3600s |
| `security:attempts:{ip}` | tentativas `[{timestamp,reason}]` | 600s |
| `security:blacklist:{ip}` | bloqueio permanente `{ip,reason,blacklisted_at,permanent}` | 1 ano |
| `gemini_cache:{tipo}:{user\|global}:{md5}` | cache do LLM | 24h–30d |
| `invoice_closed:{fatura_id}`, `invoice_due_warning:{fatura_id}:3d` | dedupe de notificação | 7d |
| `calendar_disconnected_alert:{uid}`, `calendar_token_revoked_alert:{uid}` | dedupe | 7d |
| `calendar_alert:{hash}` (de uid, event_id, start) | claim de alerta de tarefa | 7200s (job) / 2h (serviço) |
| `job_lock:{NOME}` | lock distribuído de job | 300s |
| `travel_time_limit:{uid}:{data}` | limite diário de rotas | 86400s |
| `bot_session_id:{BOT_SESSION_NAME}` | cache da sessão do bot | 600s |
| chaves do Flask-Limiter | rate limit | janela fixa |

Comportamento fail-open: sem Redis, locks são ignorados e o filtro de segurança não bloqueia. O .NET mantém o mesmo comportamento e registra aviso.

**Virada**: apagar `pending_*`, `nightly_checkin*`, `security:*`, `gemini_cache:*` e as chaves do Limiter (formatos específicos do Python); **manter** `invoice_*`, `calendar_*_alert:*`, `calendar_alert:*`, `travel_time_limit:*`, que o .NET só testa por existência. Sem as chaves de dedupe, notificações seriam reenviadas.

## 8. Filtro de segurança (`app/middleware/security.py`)

- IP: `remote_addr`; se privado/loopback e houver `X-Forwarded-For`, usa o primeiro IP; **sem o header, a requisição passa sem filtro** (cron e health internos).
- Ordem: blacklist ⇒ 403 `{error:"Access permanently denied",message:"Your IP has been permanently blocked"}`; bloqueio temporário ⇒ 403 `{error:"Access denied",message:"Your IP has been temporarily blocked due to suspicious activity"}`; requisição suspeita ⇒ registra tentativa e 404 `{error:"Not found"}`; na 5ª ⇒ 403 `{error:"Access denied",message:"Too many suspicious requests. IP blocked."}`.
- "Suspeito": caminho casa algum `SUSPICIOUS_PATTERNS` (linhas 30–62, `re.search` ignorando caixa) **ou** User-Agent casa `SUSPICIOUS_USER_AGENTS` (65–76) **ou** caminho fora da whitelist. Método HTTP não conta.
- Limites: 5 tentativas em 10 min ⇒ bloqueio de 60 min.
- Rate limit do Flask: 100/hora por `remote_addr` (balde único por causa do nginx sem ProxyFix; desvio 023).
- No .NET a whitelist vem do `EndpointDataSource`; os padrões suspeitos e user-agents são copiados para configuração. Cuidado: o padrão `/config` (L42) hoje bloqueia `/admin/config-alertas-financeiros` e `/calendar-alerts/config/<id>` apesar de estarem na whitelist (desvio 005).
