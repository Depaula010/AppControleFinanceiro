# Runbook da virada (big-bang)

Estratégia: construir e validar tudo em homol; em produção, trocar o container `web` de uma vez. **Não executar nenhum passo sem o Rafael presente e sem o rollback testado.**

## Pré-requisitos (todos obrigatórios)

- [ ] Todas as features do `roadmap.md` com DoD cumprida (`constitution.md` §10).
- [ ] Suíte de paridade sem diferença fora de desvios `aprovado`.
- [ ] Todos os desvios com estado `aprovado` ou `rejeitado` (nenhum `proposto`).
- [ ] Homol estável por **7 dias** com os jobs ativos.
- [ ] Respostas confirmadas pelo Rafael: URL que o bot usa para chamar o backend; `GOOGLE_REDIRECT_URI` real e se passa pelo `oauth_proxy`; quem chama `/admin/run-motor-agendamentos` e `/admin/trigger-*` hoje (bot, UptimeRobot, cron do host).
- [ ] `oauth_proxy` (Render) **reimplantado** com a versão que repassa a query string no `/connect-calendar` (desvio 053); testar `https://<proxy>/connect-calendar/<id>?t=x` e conferir que o servidor recebe `?t=x`.
- [ ] `pg_dump` completo de `bot_whatsapp_prod` guardado fora do servidor.
- [ ] Tag `flask-final` criada no commit do Flask em produção.
- [ ] Janela de baixo uso (madrugada), evitando 00:01 (processador de faturas) e as horas 18–23.

## Migrations de schema (ADR 0001)

Antes do passo 5 da sequência, aplicar em produção, com o Rafael presente, as migrations de `dotnet/migrations/` ainda não aplicadas. As previstas até aqui, todas aditivas:

| # | Migration | Desvio |
|---|---|---|
| 0001 | `usuarios.api_key_automate` para `varchar(255)` | 017 |
| (a definir) | `transacoes.observacoes text`, `transacoes.origem varchar(20)` | 027 |

Aplicar antes é seguro: o Flask não usa as colunas novas e a alteração de tipo só alarga.

## Sequência

1. **Congelar** o Flask: parar o recebimento de novos atalhos avisando o bot (ou aceitar alguns minutos de falha).
2. **Backup**: `pg_dump` + `redis-cli SAVE` e cópia do volume `redis-data`.
3. **Desligar schedulers externos** que disparam `/admin/run-motor-agendamentos` e `/admin/trigger-*`, se o Quartz assumir.
4. **Limpar Redis** das chaves específicas do Python (`pending_*`, `nightly_checkin*`, `security:*`, `gemini_cache:*`, Limiter). Manter chaves de dedupe (`invoice_*`, `calendar_*`, `job_lock:*` expiram sozinhas).
5. **Merge do PR de virada** ⇒ o workflow faz build do .NET **antes** de derrubar o Flask, depois `up -d` com `container_name: meu-secretario-api`.
6. **Smoke imediato** (ver abaixo).
7. **Observar 2 horas** logs (`docker compose logs -f web`), `security.log`, bot, jobs.
8. **Reativar** gatilhos externos se necessários (apontando para as mesmas rotas).

## Smoke test (obrigatório, nesta ordem)

| # | Teste | Prova |
|---|---|---|
| 1 | `curl 127.0.0.1:8080/` ⇒ 200 `{"status":"online",...}` | processo, nginx, DB |
| 2 | Login no Angular com **usuário antigo** | hasher Werkzeug |
| 3 | Reabrir o Angular com **token emitido pelo Flask** (ainda válido) | JWT compatível |
| 4 | Listar contas, transações, contas mensais, dashboard | EF/Dapper no schema real |
| 5 | Criar e excluir uma transação de teste | escrita, sinal do valor |
| 6 | Atalho do iPhone em `/api/transacao` ⇒ confirmação por WhatsApp | Fernet + Redis + bot |
| 7 | Mensagem de texto ao bot ⇒ resposta | webhook, Gemini, HMAC, `x-api-key` |
| 8 | `POST /admin/security-stats` com `x-api-key` | segurança e Redis |
| 9 | Disparar manualmente o processador de faturas | job + idempotência |
| 10 | Próximo alerta de tarefa / briefing matinal | jobs agendados |

## Rollback

Critério: qualquer falha nos itens 1–7, ou erro 5xx sustentado.

1. `git revert` do PR de virada (ou `git checkout flask-final`) e novo deploy: build do Flask (~minutos).
2. Reativar labels do Ofelia e o serviço `cron-scheduler` (voltam com o revert).
3. Redis: nada a restaurar além do que expira; se houve escrita de chaves incompatíveis, apagar `security:*` e `pending_*`.
4. Banco: **o .NET não faz migration destrutiva**, então não há rollback de schema. Migrations só aditivas.
5. Se houve dados gravados incorretamente pelo .NET, usar o `pg_dump` do passo 2 apenas com aprovação do Rafael.

## Pós-virada (primeira semana)

- Conferir diariamente: faturas fechadas às 00:01, resumo matinal, check-in noturno, relatório do dia 1.
- Rotacionar a senha do Postgres e `API_SECRET_KEY` (esta invalida JWTs: combinar com o Rafael) — a connection string de produção foi exposta em conversa de planejamento.
- Só então iniciar a Fase 5 (remoção do Python).
