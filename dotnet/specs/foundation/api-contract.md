# Contrato HTTP

Inventário do Flask em produção (levantamento estático, 2026-10-02): **108 regras / 110 operações** (+4 no `oauth_proxy`). Este documento é o contrato que o .NET precisa honrar. Detalhes de payload por feature ficam nas specs; aqui ficam envelopes, autenticação e a lista de rotas.

Origem das rotas: `app/routes/api.py` (3011 linhas, 41 regras), `api_keys.py` (16), `auth.py` (3), `calendar_alerts.py` (4), `admin.py` + `presentation/admin/*` (~31), `webhooks/__init__.py` (10), `app/__init__.py` (`GET /`).

## 1. Envelopes de resposta

Cinco dialetos coexistem. **Cada rota usa o seu**; nunca unificar.

| ID | Onde | Sucesso | Erro |
|---|---|---|---|
| **A** | `/auth/*`, `/api/*` com JWT | `{"status":"success","message"?,"data"?}` | `{"status":"error","message":"..."}` |
| **B** | `/admin/*`, webhooks, `/calendar-alerts/*` | `{"status":"sucesso","mensagem":"...", ...extras na raiz}` | `{"status":"erro","mensagem":"..."}` |
| **C** | `/api-keys/*` | chaves soltas na raiz (`mensagem`, `id`, ...) | `{"erro":"<título>","mensagem":"<detalhe>"}` sem `status` |
| **D** | filtro de segurança | — | `{"error":"...","message":"..."}` |
| **E** | `/webhook-whatsapp` | `{"status":"sucesso","resposta":"<texto>"}` | `{"status":"erro","resposta":"..."}` (o 400 usa `mensagem`) |

**Campos na raiz no envelope A.** `/auth/register`, `/auth/login` e `/auth/verify` não usam `data`: devolvem `user_id`, `token`, `user` e `valid` direto na raiz (`{"status":"success","token":...,"user":{...}}`). As rotas `/api/*` usam `message` e `data`.

Exceções por rota (todas preservadas):
- `POST /api/transacao`: sucesso `{"status":"success","message",...}` (inglês); erro `{"status":"erro","mensagem"}`; JSON inválido `{"erro":"JSON invalido ou ausente"}`.
- Várias rotas `/admin/setup-*` devolvem texto puro ou HTML `<pre>`; as páginas OAuth são HTML.
- `GET /` devolve `{status:"online", service:"Meu Secretário API", database, timestamp}` e **sempre 200**, mesmo com o banco fora.

## 2. Status codes em uso
200, 201, 302, 400, 401, 403, 404, 409, 429, 500, 503. **Nunca** 204 nem 422. Validação sempre 400. Duplicidade: 409 no registro, 400 em contas.

## 3. Autenticação por rota

| Mecanismo | Como | Onde |
|---|---|---|
| JWT | `Authorization: Bearer <token>`; HS256, chave `API_SECRET_KEY`, claims `user_id` (int), `exp` (+24h), `iat`; sem iss/aud/sub; sem refresh | `/api/*` (exceto webhooks e rotas por api_key) |
| `x-api-key` global | header = `API_SECRET_KEY`, comparação em tempo constante | `/admin/*` (parte), `/webhook-whatsapp`, `/api-keys/*` |
| `user_api_key` | corpo JSON (ou query em `/api/agendamentos/reserva`); resolvida decifrando `api_key_automate` (Fernet) | `/api/transacao`, `/webhook-automate`, `/webhook-sms-payment`, `/api/agendamento/<id>/reserva`, `/api/agendamentos/reserva` |
| HMAC | `X-Webhook-Signature` = HMAC-SHA256 hex do corpo bruto, chave `WEBHOOK_SIGNATURE_KEY`; opcional, mas inválida ⇒ 401 | `/webhook-whatsapp` |
| Nenhuma | — | vários furos; ver desvios 002, 003 e 006 em `parity-deviations/` |

Mensagens 401 do `token_required` (exatas):
`"Token de autenticação não fornecido"`, `"Formato de autenticação inválido. Use: Bearer <token>"`, `"Token inválido ou expirado"`, `"Token não contém user_id"`. "Bearer" é comparado sem diferenciar maiúsculas e exige exatamente duas partes.

## 4. Convenções que quebram fácil

- **Sem barra final**: `/api/bills/` dá 404 (HTML do Flask). No .NET, `/api/bills/` também deve dar 404.
- **`<int:id>`**: valor não numérico ou negativo ⇒ 404, não 400.
- **Corpo sem `Content-Type: application/json`** ⇒ erro 415 do Flask, que vira 500 com mensagem genérica dentro dos `try/except`. Em `/api/user/*` vira `{}`. (Candidato a desvio.)
- **Chaves desconhecidas no corpo** são ignoradas.
- **`validate_required_fields`** trata `0` e `false` como ausentes (`not data.get(f)`). `valor: 0` em `/api/transacao` ⇒ "faltando".
- **Zero vira nulo**: `limite_credito: 0` e `valor_previsto: 0` (em alerts) saem `null`; `daysBeforeDue: 0` vira 3.
- **Dinheiro**: `NUMERIC` ⇒ `float`. Arredondar a 2 casas **apenas** no resumo do dashboard e na meta de reserva; `/api/bills/summary` sai sem arredondar.
- **Sinal**: o banco guarda despesa negativa; a API recebe positivo e devolve com sinal em `POST/PUT /api/transactions`; relatórios aplicam `abs()`.
- **Datas**: entrada `YYYY-MM-DD`; saída ISO; timestamps sem `Z`. Exceções: `vencimento` `dd/MM/yyyy` (reprocessar fatura), `horario_processamento` `yyyy-MM-dd HH:mm:ss`, horas `HH:mm`.
- **`id`** é string em `/api/dashboard/alerts` e inteiro no resto.
- **Nome do campo de data**: `data_transacao` na listagem paginada; `data` nas recentes e em POST/PUT.
- **Rotas estáticas ao lado de `/<id>`**: `/api/bills/summary` e `/api/transactions/recent`; no ASP.NET, a constraint `:int` mantém a precedência.
- **HEAD e OPTIONS**: o Flask responde automaticamente.

## 5. Inventário de rotas

Legenda de auth: **JWT**, **KEY** (`x-api-key`), **UAK** (`user_api_key`), **—** (nenhuma). Implementação: nº da feature em `roadmap.md`.

### Raiz e health
| Método | Caminho | Auth | Env. | Feat. |
|---|---|---|---|---|
| GET | `/` | — | próprio | 009 |
| GET | `/api/health` | — | A | 009 |
| GET | `/api-keys/health` | — | `{status:"ok",servico,versao}` | 008 |

### Auth (A) — feature 001
| Método | Caminho | Auth |
|---|---|---|
| POST | `/auth/register` | — |
| POST | `/auth/login` | — |
| POST | `/auth/verify` | — |

### Dashboard (A, JWT) — feature 006
| Método | Caminho (aliases) |
|---|---|
| GET | `/api/dashboard/summary`, `/api/dashboard/resumo`, `/api/dashboard/stats` |
| GET | `/api/dashboard/charts` (`?meses=1..12`, padrão 3) |
| GET | `/api/dashboard/recent`, `/api/transactions/recent`, `/api/transacoes/recentes` |
| GET | `/api/dashboard/alerts` |

### Contas (A, JWT) — feature 002
| Método | Caminho (alias) |
|---|---|
| GET, POST | `/api/accounts`, `/api/contas` |
| PUT, DELETE | `/api/accounts/<int:id>`, `/api/contas/<int:id>` |

`tipo_conta` aceito: `Conta Corrente`, `Conta Poupança`, `Investimento`, `Cartão de Crédito`, `Dinheiro`, `Outro`. GET devolve `{id,nome,banco,tipo,saldo}`; POST/PUT usam `nome_conta`, `tipo_conta`, `saldo_inicial`. DELETE é soft delete (`ativa=false`) e bloqueia se houver transações.

### Transações (A, JWT) — feature 004
| Método | Caminho |
|---|---|
| GET, POST | `/api/transactions` (GET: `limit` 1..100 padrão 20, `offset`, `tipo` Receita/Despesa, `data_inicio`, `data_fim`) |
| PUT, DELETE | `/api/transactions/<int:id>` |

### Categorias (A, JWT) — feature 003
| GET | `/api/categories`, `/api/categorias` (`?tipo=Receita|Despesa`) |

### Contas mensais / agendamentos (A, JWT) — feature 005
| Método | Caminho (alias) |
|---|---|
| GET, POST | `/api/bills`, `/api/contas-mensais` |
| PUT, DELETE | `/api/bills/<int:id>`, `/api/contas-mensais/<int:id>` |
| GET | `/api/bills/summary` (sem alias) |

### Configurações do usuário (A, JWT) — feature 007
| Método | Caminho |
|---|---|
| GET, PUT | `/api/user/profile` |
| GET, PUT | `/api/user/api-keys` (PUT usa camelCase: `type`, `useOwnKey`, `key`) |
| POST | `/api/user/api-keys/validate` |
| GET, PUT | `/api/user/notifications` (camelCase) |
| GET, POST | `/api/user/addresses` |
| DELETE | `/api/user/addresses/<string:label>` |
| GET | `/api/user/settings` |

### Webhooks, automação, OAuth — features 010, 011, 012
| Método | Caminho | Auth | Env. |
|---|---|---|---|
| POST | `/webhook-whatsapp` | KEY (+HMAC opcional) | E |
| POST | `/webhook-automate` | UAK | B |
| POST | `/api/transacao` | UAK | exceção (§1) |
| POST | `/webhook-sms-payment` | UAK | B |
| GET | `/connect-calendar/<int:id>` (`?force=true`) | — | 302/HTML |
| GET | `/oauth2callback` | — | HTML |
| POST | `/disconnect-calendar/<int:id>` | — | B |
| PATCH | `/api/agendamento/<int:id>/reserva` | UAK (corpo) | B |
| GET | `/api/agendamentos/reserva` | UAK (query) | B |
| POST | `/api/fatura/<int:id>/reprocessar` | — | B |

### API keys e LGPD (C, KEY) — feature 008
| Método | Caminho |
|---|---|
| POST, GET | `/api-keys/usuario/<int:usuario_id>` |
| PUT, DELETE | `/api-keys/chave/<int:chave_id>` |
| POST, GET | `/api-keys/preferencias/<int:usuario_id>` |
| DELETE | `/api-keys/preferencias/<int:usuario_id>/<provedor>` |
| GET | `/api-keys/uso/<int:usuario_id>` |
| GET | `/api-keys/logs/<int:usuario_id>` |
| POST | `/api-keys/lgpd/consentimento` |
| GET | `/api-keys/lgpd/consentimentos/<int:usuario_id>` |
| GET | `/api-keys/lgpd/consentimentos-iniciais/<int:usuario_id>` |
| GET | `/api-keys/lgpd/exportar/<int:usuario_id>` |
| DELETE | `/api-keys/lgpd/deletar-conta/<int:usuario_id>` (corpo `{confirmacao:"CONFIRMO_DELECAO"}`) |
| GET | `/api-keys/lgpd/verificar-consentimento/<int:usuario_id>/<tipo_consentimento>` |

### Alertas de calendário (B, sem auth) — feature 014
`GET|POST /calendar-alerts/config/<int:id>`, `POST /calendar-alerts/ativar/<int:id>`, `POST /calendar-alerts/desativar/<int:id>`. **Hoje retornam 500** (método inexistente); ver desvio 004.

### Admin (B) — feature 009
Com `x-api-key`: `POST /admin/run-motor-agendamentos`, `POST /admin/security-unblock-ip`, `GET /admin/security-stats`, `POST /admin/security-blacklist-add`, `POST /admin/security-blacklist-remove`, `POST /admin/gemini-cache-clear`, `GET /admin/gemini-cache-stats` (hoje 500), `POST /admin/setup-api-keys-tables`, `POST /admin/trigger-agenda-notifications`, `POST /admin/trigger-bills-notifications`, `POST /admin/trigger-daily-briefing`, `POST /admin/trigger-monthly-reports-inicio`, `POST /admin/trigger-monthly-reports-fim`, `POST /admin/test-notification`, `POST /admin/test-monthly-report/<int:id>`, `POST /admin/test-daily-briefing`.

Sem autenticação hoje (ver desvios): `GET /admin/get-notification-config/<int:id>`, `POST /admin/config-alertas-financeiros`, `POST /admin/clear-bot-session`, `GET /admin/setup-database`, `/populate-global-categories`, `/setup-user-data`, `/setup-calendar-table`, `/setup-monthly-reports-table`, `/setup-resumo-matinal`, `/setup-checkin-noturno`, `/setup-potes-alerts`, `/setup-alertas-financeiros`, `/setup-reserva-emergencia`, `/cleanup-deprecated-notification-fields`, `/setup-budget-violations`, `/oauth-config-check`, `/debug-calendar`.

## 6. O que NÃO migra como rota
Código morto: `app/presentation/routes/example_with_di.py` (`/api/example`, 7 rotas, não registrado). `oauth_proxy` (4 rotas) só entra se o `GOOGLE_REDIRECT_URI` real ainda apontar para ele (ver `infra-deploy.md`).

## 7. Whitelist de segurança
O Flask devolve 404 + conta tentativa para rotas **registradas** que não estão em `VALID_ENDPOINTS` (ex.: `/api-keys/usuario/*`, `/api-keys/lgpd/*`, `/calendar-alerts/ativar/*`, `/api/agendamento*/reserva`) quando chamadas por IP público. Hoje essas rotas são inalcançáveis de fora. Decisão em `parity-deviations/005`.
