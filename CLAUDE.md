# CLAUDE.md — Meu Secretário

Backend **.NET 10** do Meu Secretário (finanças pessoais + agenda por WhatsApp). Em produção desde **2026-10-12**, substituindo o backend Flask/Python, que foi removido deste repositório (o código antigo está na tag `flask-final`). Este arquivo é o ponto de partida para manutenção: leia-o inteiro antes de mexer.

## 1. Regras inegociáveis

1. **Nenhuma rota muda.** Método, caminho, nomes de campos JSON, status codes e envelope de resposta são contrato com o Angular (`E:/Projetos/Projetos/meusecretariofront`), o bot Baileys e o atalho do iPhone.
2. **Spec antes de código.** Feature nova ou mudança de comportamento: `dotnet/specs/features/NNN-nome/{spec,plan,tasks}.md` primeiro (fluxo em `dotnet/specs/README.md`).
3. **Banco = verdade.** Tabelas minúsculas, sem aspas (`usuarios`, `contas`). Esquema em `dotnet/specs/foundation/data-model.md`. Migrations só aditivas e idempotentes, em `dotnet/migrations/NNNN_*.sql` (ADR 0001; rodam no deploy com `--migrate`, nunca no boot).
4. **Mudança de comportamento só como desvio aprovado** (`dotnet/specs/foundation/parity-deviations/`, um arquivo por desvio; estados proposto/aprovado/rejeitado/implementado). O Rafael aprova.
5. **Teste junto com o código.** Regra financeira sem teste não entra.
6. **Segredos nunca em arquivo versionado, log, mensagem de erro nem no chat.** Não logar PII (telefone, texto de mensagem). `SafeError` descreve só o tipo da exceção.
7. **Idioma:** código em inglês; mensagens ao usuário, erros da API e WhatsApp em português, idênticos aos contratos.
8. **Fuso `America/Sao_Paulo` e cultura `pt-BR` explícitos**; use `IClock`/`TimeProvider`, nunca `DateTime.Now`.
9. **Nunca dar push em `main` sem intenção de deployar**: cada push em `main` derruba e recria os containers (~3 min de indisponibilidade). Trabalhe em branch.

## 2. Comandos

```bash
dotnet build dotnet/MeuSecretario.slnx            # 0 warnings (warnings são erro no CI)
dotnet test  dotnet/MeuSecretario.slnx            # ~2.9k unit + ~2.1k integração; precisa do Docker Desktop ligado
dotnet run --project dotnet/src/MeuSecretario.Api -- --migrate   # aplica migrations pendentes (usa DATABASE_URL)
```

Variáveis obrigatórias: `DATABASE_URL, API_SECRET_KEY, ENCRYPTION_KEY, GEMINI_API_KEY, REDIS_URL`. Opcionais: `WEBHOOK_SIGNATURE_KEY, BOT_WHATSAPP_URL, BOT_SESSION_NAME, BOT_SESSION_ID, BOT_API_KEY, GOOGLE_CLIENT_ID/SECRET/REDIRECT_URI, WEATHER_API_KEY, OPENROUTE_API_KEY, JOBS_ENABLED, LOG_TO_FILE, LOG_DIR, RATE_LIMIT_ENABLED, API_KEYS_ROUTES_ENABLED`. Modelo em `.env.example`.

## 3. Arquitetura (`dotnet/`)

Monólito modular em camadas: `Domain` (regras puras) → `Application` (casos de uso, portas) → `Infrastructure` (Dapper/Npgsql, Redis, clientes HTTP, Quartz) → `Api` (minimal APIs). Cada feature é um `IFeatureModule` descoberto por reflexão (`Api/<Feature>/`); não edite `Program.cs` para registrar rotas. Regras de dependência verificadas por `ArchitectureTests`.

| Feature | Rotas / papel |
|---|---|
| 001 Auth | `/auth/register, /auth/login, /auth/verify` (JWT HS256, hash Werkzeug pbkdf2/scrypt) |
| 002 Contas · 003 Categorias | `/api/accounts` (+`/api/contas`), `/api/categories` |
| 004 Transações | `/api/transactions` (+ portas `ITransactionPosting`, `IInvoiceService`) |
| 005 Contas mensais | `/api/bills` (+`/api/contas-mensais`), `/api/bills/summary`, `IEmergencyReserveService` |
| 006 Dashboard | `/api/dashboard/*`, `/api/transactions/recent` |
| 007 Configurações | `/api/user/*` (perfil, notificações, endereços, api-keys) |
| 008 API keys/LGPD | `/api-keys/*` (só com `API_KEYS_ROUTES_ENABLED=true`) |
| 009 Admin/segurança | `/admin/*` (`x-api-key` = `API_SECRET_KEY`), filtro de segurança (whitelist = rotas registradas, bloqueio de IP no Redis `security:*`) |
| 010 Integrações | Redis, bot, Gemini, Google (OAuth/Calendar/Drive), clima, OpenRoute |
| 011 Automação | `/webhook-automate, /api/transacao, /webhook-sms-payment, /api/agendamento/{id}/reserva, /api/agendamentos/reserva, /api/fatura/{id}/reprocessar` |
| 012 WhatsApp | `POST /webhook-whatsapp` (HMAC `X-Webhook-Signature` opcional + `x-api-key`), intenções por Gemini, estado `pending_*` no Redis |
| 013 Jobs | Quartz no processo (`JOBS_ENABLED=true`): resumo matinal (minuto a minuto), check-in noturno 18–23h, alertas de tarefas (1 min), faturas 00:01, motor de agendamentos 00:05, relatórios mensais (dia 1 às 08:00 e 23:00) |
| 014 Alertas de calendário | `/calendar-alerts/*` (JWT; o `<id>` precisa ser o do token) |

Envelopes A–E reproduzem os formatos do Flask (`Api/Contracts/Envelopes.cs`). Compatibilidade criptográfica: JWT com claim `user_id`, Fernet (`ENCRYPTION_KEY`), HMAC-SHA256 do webhook.

## 4. Produção

- **Servidor:** VPS Contabo (`212.47.65.37`), pasta `/opt/meu-secretario`. Containers (`docker compose`): `meu-secretario-api` (.NET, porta 8000 interna), `meu-secretario-nginx` (127.0.0.1:8080/8443), `meu-secretario-redis`. Rede externa `rede-global`. Postgres: container `postgres-central` (bancos `bot_whatsapp_prod`, `bot_whatsapp_homol`, `bot_whatsapp_refatorado`). **Nomes de container, porta 8000 e rede não podem mudar** (nginx e bot dependem).
- **nginx do host** (porta 80) → `127.0.0.1:8080` → container nginx → `meu-secretario-api:8000`. O proxy HTTPS do **Render** (`meu-financeiro-proxy.onrender.com`, repositório `Depaula010/meu-financeiro-proxy`, `proxy_server.py`) repassa qualquer rota (incluindo a query string) para `http://212.47.65.37`; é por ele que o Google devolve o OAuth (`GOOGLE_REDIRECT_URI=https://meu-financeiro-proxy.onrender.com/oauth2callback`).
- **Deploy:** push em `main` (exceto mudanças só de `*.md`, `docs/`, `dotnet/specs/`, `.claude/`, que são ignoradas pelo `paths-ignore`; `[skip ci]` na mensagem também evita) → `.github/workflows/deploy.yml` → SSH → `git reset --hard origin/main`, reescreve `.env` dos secrets, `docker compose down`, `build`, `up -d redis`, **`--migrate`**, limpa chaves Flask do Redis, `up -d`, health check (`127.0.0.1:8080/` = 200). Secrets do GitHub: `CONTABO_SSH_KEY/HOST/USER, DATABASE_URL, API_SECRET_KEY, ENCRYPTION_KEY, WEBHOOK_SIGNATURE_KEY, GEMINI_API_KEY, BOT_WHATSAPP_URL, BOT_SESSION_NAME, BOT_API_KEY, GOOGLE_*, WEATHER_API_KEY, OPENROUTE_API_KEY`.
- **Cuidado no workflow:** comandos `docker compose run/exec` dentro do `ssh bash << heredoc` consomem o stdin e engolem o resto do script. Sempre `-T` e `< /dev/null` (falhou assim em 2026-10-12).
- **Logs:** só console (`LOG_TO_FILE=false`): `docker logs -f meu-secretario-api`. Webhook do WhatsApp registra `recebido`/`processado`/`sem x-api-key válida`/`respondeu 503`/chaves do corpo no 400, sem PII.
- **Health:** `GET /` e `GET /api/health` sem login.
- **Rollback:** `git revert` do merge (ou `git checkout flask-final`) e novo deploy (o compose do Flask volta, com Ofelia). O .NET só faz migrations aditivas, então o Flask roda no mesmo schema. Backup de dados: `pg_dump` (ver pendências: ainda não feito antes da virada).

## 5. Bot de WhatsApp (Baileys)

- **Bot em uso:** `whatsapp-saas-bot` (código em `E:/Projetos/Projetos/AgendaInteligente/bot`, pasta do servidor `/root/whatsapp-saas`, porta host 3001 → container 3000, multi-tenant, `docker.compose.yml`). Sessão `sessao-principal` no número do Meu Secretário (**55 31 …2385**, celular do Rafael com o aparelho vinculado). O bot antigo `whatsapp-bot` (`bot-appfinanceiro-whatsapp`) foi **removido em 2026-10-12** (container, imagens e `/root/bot-whatsapp`; cópia da pasta em `/root/whatsapp-bot-antigo-20261012.tgz`, com `.env`, apagar depois de alguns dias): a sessão dele perdeu as chaves Signal (`PreKeyError`) e deixou de receber. O banco `bot_whatsapp_refatorado` e o usuário dele continuam no `postgres-central` (podem ser removidos após `pg_dump`). Há também sessões de outros clientes (disparador) no SaaS: **não reiniciar o container sem checar `SELECT session_name,status FROM sessions`**.
- **Como o bot chama o backend:** `sessions.webhook_url` (banco do SaaS) = `http://meu-secretario-api:8000/webhook-whatsapp`, `x-api-key` = `sessions.metadata.webhook_auth_token` (valor do `API_SECRET_KEY` do backend; o container SaaS **não** tem `API_SECRET_KEY` no ambiente de propósito), HMAC com `sessions.webhook_signature_key` (= `WEBHOOK_SIGNATURE_KEY`).
- **Como o backend envia:** `BOT_WHATSAPP_URL=http://whatsapp-saas-bot:3000`, `BOT_SESSION_NAME=sessao-principal`, `BOT_API_KEY` = chave do tenant `meu-secretario` criada em `POST /api/v1/admin/api-keys` (header `X-Admin-Key`). `IBotClient` resolve o `session_id` pelo nome.
- **Criar sessão nova no SaaS:** a API recusa `webhook_url` com nome de container (anti-SSRF). Crie com o IP público e troque por `UPDATE sessions SET webhook_url=...` no banco do SaaS; grave `webhook_auth_token` no `metadata` (jsonb); reinicie a sessão para recarregar o metadata; QR code aparece no log do container (`docker logs -f whatsapp-saas-bot`).
- **Se o bot parar de responder:** (1) `docker exec meu-secretario-api` + curl assinado em `127.0.0.1:8080/webhook-whatsapp` (prova o backend); (2) `docker logs` do bot (nível `warn`; para `info` recrie com `LOG_LEVEL=info`); (3) procure `Invalid PreKey ID` ou `device_removed` ⇒ re-parear (apagar `baileys_auth` da sessão, fazer backup antes, subir e escanear QR).
- **Front Angular:** a tela `whatsapp-sessions` chama o bot por `/bot-api`. Em desenvolvimento, `proxy.conf.js` (front) encaminha para `212.47.65.37:3001` e injeta `X-API-Key` a partir da variável de ambiente `BOT_API_KEY` (a chave do tenant `meu-secretario`, em `/root/ms-bot.key` no servidor); a chave não fica mais no código. Se o front for publicado, o proxy reverso do servidor precisa injetar esse cabeçalho.

## 6. Dados de produção (conferidos em 2026-10-09/12)

Um único usuário (o Rafael). Cartão Inter fecha dia 13 e vence dia 20. As faturas de cartão foram **ajustadas em 2026-10-12** com `dotnet/scripts/cutover/ajuste-faturas-cartao.sql` (12 → 10 faturas, vencimento no mês do fechamento; regra do desvio 065). `meses_reserva_emergencia = 10`. Collation `en_US.utf8` sobre Alpine (ordem por bytes), timezone UTC, `api_key_automate` em texto puro (o .NET lê as duas formas).

## 7. Ambientes de teste

- **Homol local em Docker** (banco vazio por `--migrate`, Redis novo, Gemini falso): `dotnet/scripts/homol/local/README.md` e `smoke.py` (56 verificações). Não exercita WhatsApp/IA.
- **Dados reais locais:** o export `migrations/schema.sql` (gitignored, com dados de produção; **nunca commitar**) pode alimentar um banco de ensaio (`dotnet/scripts/homol/`).
- **Homol no servidor:** `bot_whatsapp_homol` existe com estrutura e categorias, sem dados; o `docker-compose.homol.yml` não foi criado.
- **Fixtures Python:** os JSON de `dotnet/tests/Fixtures` foram gerados rodando o código Flask real. Para regenerar, recupere o código antigo sem commitar: `git checkout flask-final -- app` (e `git rm -r --cached app` / apague depois), e use os `generate_*.py` (há `.venv` com rapidfuzz/Werkzeug/PyJWT, ver cabeçalhos).

## 8. Desvios de paridade

Todos os aprovados foram implementados (001–088, 090–101, 120–122, 130–139, 150–153, 155–156, 158–159, 161, 170, 190–192, 210, 230–233, 250). **Rejeitado:** 089 (tipo `Renda` nas recentes). **Adiados para depois da virada:** 102 (chave do atalho), 154 (extração sem data/conta/parcelamento), 157 (Pagamento Fatura inalcançável), 160 (Quitar Conta Fixa sem ramo). Índice em `dotnet/specs/foundation/parity-deviations/README.md`; decisões em `foundation/decisoes-onda-{1,2}.md` e `decisoes-ondas-3-e-4.md`.

## 9. Pendências conhecidas

1. **Backup:** nenhum `pg_dump` foi feito antes da virada; fazer e agendar (`postgres-central` tem `backup-postgres.sh`).
2. **Rotacionar segredos expostos em conversas de trabalho:** senha do Postgres de produção (e atualizar o secret `DATABASE_URL`), senha do usuário do bot `refatorado`, e a **chave do bot hardcoded em `meusecretariofront/src/environments/environment.ts`** (vai no bundle público; mover as chamadas ao bot para o backend).
3. **Front:** commitar as mudanças de `proxy.conf.js`/`environment*.ts` no repositório `meusecretariofront` (feitas e testadas com `tsc`, sem commit); corrigir tela de contas (G-8: criar=400, editar zera dias).
4. **Google:** testar "reconectar Google" no .NET (o fluxo funcionou no Flask em 12/10; o .NET assina o link `?t=`).
5. **Bot antigo removido** (ver §5); falta apagar a cópia `.tgz` e, se quiser, o banco `bot_whatsapp_refatorado`. Decidir também o destino do número de homol.
6. Observar jobs: faturas 00:01, motor 00:05, check-in 18–23h, relatório dia 1 (desvio 132 já implementado).
7. Dívidas pequenas: lista de branches antigas (`backup`, `refatoracao*`, 2 `worktree-agent-*` sem merge); `docs/` com guias do front não versionados; spec 007 AC-007-41 ainda cita `alerta_potes_threshold` (antes do desvio 250); Q-009-9 (`test-notification tipo=agenda`) confirmar em homol.

## 10. Incidentes e lições

- **2026-10-12 virada:** 2 deploys falharam porque `docker compose run/exec` leu o stdin do SSH (produção ficou ~10 min fora). Correção `a3d926d`/`0de4213`. Todo deploy tem janela de indisponibilidade.
- **Bot sem responder:** (a) `x-api-key` ausente no webhook (container SaaS sem `API_SECRET_KEY`) → `metadata.webhook_auth_token`; (b) sessão do bot antigo sem prekeys. Backend estava saudável.
- **HeidiSQL** quebra blocos `DO $$` (divide no `;`); scripts de operação são SQL comum, sem `BEGIN` aberto na sessão (se der erro, `ROLLBACK`).
- **Export do HeidiSQL não restaura no Postgres** (sintaxe MySQL): use `pg_dump -Fc` para backup/restauração.
- Em `TRUNCATE usuarios CASCADE` as categorias globais somem (FK): para limpar usuário use `DELETE FROM usuarios`.

## 11. Onde ler

| Assunto | Arquivo |
|---|---|
| Fluxo SDD e como trabalhar | `dotnet/specs/README.md`, `constitution.md` |
| Arquitetura, stack, contrato HTTP | `dotnet/specs/foundation/{architecture,tech-stack,api-contract}.md` |
| Banco, regras de negócio, integrações | `foundation/{data-model,business-rules,integrations}.md` |
| Testes, deploy, virada | `foundation/{testing-strategy,infra-deploy,cutover-runbook}.md` |
| Specs de cada feature | `dotnet/specs/features/NNN-*/` |
| Operação do homol local | `dotnet/scripts/homol/` |
| Manuais de integrações e chatbot | `docs/MANUAL_*.md`, `docs/TESTES_CHATBOT_WHATSAPP.md` |
