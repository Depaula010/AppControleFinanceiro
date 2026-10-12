# Roadmap

Virada única (big-bang): nada vai a produção antes da Fase 4. Valor intermediário vem de homol e dos testes de paridade.

## Fases

| Fase | Entrega | Execução |
|---|---|---|
| **0 — Fundação SDD** | Documentos em `dotnet/specs/`, `CLAUDE.md`, scripts de homol. Rafael roda os scripts no servidor e commita `schema-prod.sql` (só estrutura). Rafael aprova `business-rules.md` e `parity-deviations/`. | sequencial |
| **1 — Esqueleto** | **Concluída em 2026-10-02**, exceto os itens adiados abaixo. Entregue: solução `.slnx` com 4 camadas + 2 projetos de teste, `global.json`, Central Package Management, analisadores com warnings como erro, `IClock` (São Paulo), `AppSettings` (mesmos nomes dos secrets, falha rápida), conversão de `DATABASE_URL`, envelopes A–E, módulos de feature descobertos por reflexão, 404 com barra final, `GET /` e `GET /api/health`, Dockerfile, `ci.yml`, testes de arquitetura (38 testes verdes). | sequencial |
| **2 — Features** | Ver ondas abaixo. | paralela por onda |
| **3 — Homologação** | Stack de homol, suíte de paridade verde, jobs observados por dias, Angular e bot em homol. | sequencial |
| **4 — Virada** | Ver `cutover-runbook.md`. | sequencial |
| **5 — Limpeza** | Remover `app/`, `requirements.txt`, `oauth_proxy/` (após confirmar URI), docs obsoletos. | sequencial |

### Adiado da Fase 1 (com motivo)

| Item | Para onde foi | Motivo |
|---|---|---|
| Scaffold do EF Core (`AppDbContext`, entidades) | cada feature mapeia suas tabelas | depende do `schema-prod.sql` (estrutura exportada pelo Rafael); o mapeamento por feature evita um arquivo compartilhado que bloqueia o paralelismo |
| Middleware de segurança (whitelist, bloqueio de IP, Redis) | feature 009 | precisa do cliente Redis e da spec 009 aprovada (formato de chave duplamente codificado, desvios 005 e 023) |
| Build da imagem validado localmente | job `docker-build` do `ci.yml` | Docker Desktop indisponível na máquina de desenvolvimento |
| ~~SDK 10 estável~~ | **resolvido** | SDK 10.0.401 instalado; `global.json` fixado nele e os 38 testes passam |
| ~~`schema-prod.sql`~~ | **resolvido** | estrutura exportada em 2026-10-02 em `foundation/schema-prod.sql` (sem dados). **Arquivo local, ignorado pelo git** por decisão do Rafael; **os testes de integração com Testcontainers e o CI precisam de um schema versionado: decidir antes da T9 da feature 001** (ex.: baseline em `dotnet/migrations/0000_baseline.sql`). Homol já criado, falta carregar a estrutura e os dados de referência |

> **ONDA 1 CONCLUÍDA em 2026-10-04:** 001 Auth, 002 Contas, 003 Categorias, 008 API keys/LGPD e 010 Integrações (010-0, 010a Redis, 010b bot, 010c Gemini, 010d Google, 010e clima e rotas) implementadas e integradas, com **1.731 testes verdes** (1.073 unitários + 658 de integração com PostgreSQL e Redis reais). Próxima: onda 2 (004 Transações, 005 Contas mensais, 007 Configurações, 009 Admin e segurança); as specs dessas features ainda precisam ser escritas e aprovadas.

## Features e ondas

| # | Feature | Origem principal (Python) | Onda |
|---|---|---|---|
| **001a** | **Blocos compartilhados de auth** (JWT, filtro `RequireUser`, hasher Werkzeug, Fernet, conexão Dapper) | `app/routes/auth.py`, `api.py:34-90`, `encryption_service.py` | **1 — entra primeiro, sozinha** |
| 001b | Auth: rotas `/auth/register`, `/auth/login`, `/auth/verify` | `app/routes/auth.py` | 1 |
| 002 | Contas | `app/routes/api.py:283-727` | 1 |
| 003 | Categorias | `api.py:1702`, seed em `setup_service.py:78-105` | 1 |
| 008 | API keys e LGPD | `app/routes/api_keys.py`, `servico_consentimento_lgpd.py`, `gerenciador_chaves_api.py` | 1 |
| 010 | Integrações (bot, Gemini, Google, clima, rotas) | `app/services/*_service.py` | 1 |
| 004 | Transações + regra de fatura | `api.py:728-1700`, `services/finance/invoice_service.py`, `transaction_service.py`, `installment_service.py` | 2 |
| 005 | Contas mensais / agendamentos | `api.py:1795-2430`, `services/queries/agendamentos_queries.py`, `bills_service.py` | 2 |
| 007 | Configurações do usuário | `api.py:2430-3011` | 2 |
| 009 | Admin e segurança | `app/routes/admin.py`, `app/presentation/admin/`, `app/middleware/security.py` | 2 |
| 006 | Dashboard | `api.py:97-282, 955-1300`, `analytics_service.py` | 3 |
| 011 | Automação (iPhone, Automate, SMS, reserva, reprocessar fatura) | `routes/webhooks/handlers/transaction_handler.py`, `reserve_handler.py` | 3 |
| 014 | Alertas de calendário | `app/routes/calendar_alerts.py`, `calendar_alert_service.py` | 3 |
| 012 | Webhook WhatsApp e intents | `whatsapp_handler.py`, `routes/webhooks/intents/` | 4 |
| 013 | Jobs | `app/jobs/`, `nightly_checkin_service.py`, `daily_briefing_service.py`, `monthly_report_service.py` | 4 |

## Execução em paralelo

Uma sessão/agente por feature, cada uma em seu **git worktree e branch** (`feat/NNN-nome`), terminando em PR com CI verde.

```bash
git worktree add ../acf-002-contas -b feat/002-contas
```

Regras para evitar conflito:
- `Program.cs`, `AppDbContext`, `Directory.Packages.props` e `Directory.Build.props` ficam **congelados após a Fase 1**. Cada feature registra-se em `Add<Feature>Module()` / `Map<Feature>Endpoints()`, descobertos por reflexão.
- Desvios de paridade: um arquivo por desvio.
- Specs de uma onda são escritas e **aprovadas antes** de liberar os agentes dela.
- Pacote NuGet novo ou mudança em arquivo compartilhado entra por PR separado, antes da onda.
- Cada agente só altera a pasta da sua feature (`*/NNN-nome/`), os testes dela e, se necessário, um arquivo de desvio.

A onda N+1 só começa quando os PRs da onda N estão em `main`. **Exceção na onda 1:** a 001a (blocos compartilhados de auth) entra em `main` primeiro e sozinha, porque toda rota `/api/*` das outras features depende do filtro `RequireUser`; só depois a 001b, a 002, a 003, a 008 e a 010 rodam em paralelo.

## Marcos

- **M0**: specs aprovadas e homol criado.
- **M1**: esqueleto compila, testa, builda a imagem e responde `GET /`.
- **M2**: ondas 1–3 em `main` (toda a API do Angular).
- **M3**: ondas 4 (bot e jobs) em `main`.
- **M4**: 7 dias estáveis em homol.
- **M5**: virada.
