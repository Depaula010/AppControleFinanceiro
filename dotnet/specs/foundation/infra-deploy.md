# Infraestrutura, deploy e homologação

## 1. Estado atual (produção, Contabo 212.47.65.37)

- Diretório: `/opt/meu-secretario`. Workflow `.github/workflows/deploy.yml`: push em `main`/`master` ⇒ SSH ⇒ `git fetch` + `git reset --hard origin/main` ⇒ reescreve `.env` ⇒ `docker compose build --no-cache` (com os containers atuais no ar) ⇒ migrations ⇒ `up -d --remove-orphans` ⇒ remove só as imagens antigas do projeto (`docker image rm` das anotadas antes + `docker image prune --filter label=app=meu-secretario`) ⇒ health check `curl http://127.0.0.1:8080/` esperando 200.
- Serviços do `docker-compose.yml` (todos na rede externa **`rede-global`**):

| Serviço | container_name | Notas |
|---|---|---|
| `nginx` | `meu-secretario-nginx` | `127.0.0.1:8080:80` e `127.0.0.1:8443:443`; upstream `meu-secretario-api:8000`; atrás de um **nginx nativo da VPS** (fora do repositório) que expõe 80/443 |
| `web` | `meu-secretario-api` | gunicorn 2 workers, porta 8000, volume `./logs:/app/logs`, labels do Ofelia |
| `redis` | `meu-secretario-redis` | `redis:7-alpine`, AOF, volume `redis-data` |
| `cron-scheduler` | `meu-secretario-cron` | Ofelia; executa `docker exec` no `web` |

- Postgres **não** está no compose: é `postgres-central` (outra stack na `rede-global`), base `bot_whatsapp_prod`, compartilhada com o bot Node.
- Redis é próprio deste compose; `REDIS_URL=redis://redis:6379` resolve pelo nome do serviço (risco de colisão se outra stack da `rede-global` tiver um serviço `redis`).
- Secrets do GitHub (nomes): `CONTABO_SSH_KEY`, `CONTABO_HOST`, `CONTABO_USER`, `GEMINI_API_KEY`, `API_SECRET_KEY`, `ENCRYPTION_KEY`, `WEBHOOK_SIGNATURE_KEY`, `BOT_WHATSAPP_URL`, `BOT_SESSION_NAME`, `BOT_API_KEY`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`, `GOOGLE_REDIRECT_URI`, `DB_PASSWORD` (declarado, não usado), `DATABASE_URL`, `WEATHER_API_KEY`, `OPENROUTE_API_KEY`.
- Seis jobs do Ofelia: resumo matinal, relatórios mensais (2, quebrados por usarem `localhost:5000`), check-in noturno, alertas de tarefas, processador de faturas (ver `business-rules.md` R16).
- nginx (container): rate limit `api` 20 r/s, `admin` 5 r/s, `webhook` 50 r/s; UA vazio ⇒ 403; `/health` responde `200 OK` sem tocar o backend; `client_max_body_size 10M`; TLS autoassinado em `nginx/ssl/` (não versionado). fail2ban e logrotate do host leem `/opt/meu-secretario/nginx/logs/`.

## 2. Estado alvo

Mesma topologia, só o `web` muda:

```yaml
web:
  build: ./dotnet            # Dockerfile multi-stage
  container_name: meu-secretario-api   # NÃO mudar (nginx, bot, scripts dependem)
  expose: ["8000"]                      # ASPNETCORE_URLS=http://+:8000
  networks: [rede-global]
  environment: [ ...mesmas variáveis..., TZ=America/Sao_Paulo ]
  volumes: ["./logs:/app/logs"]
  healthcheck: curl -fs http://localhost:8000/ || exit 1
  # sem labels do Ofelia
```
Removidos: serviço `cron-scheduler` (jobs passam para Quartz), labels `ofelia.*`.

### Dockerfile (esboço)
Multi-stage: `mcr.microsoft.com/dotnet/sdk:10.0` (build/publish, `--no-restore` após restore cacheado) → `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian) com `curl`, `tzdata`, ICU; usuário não root; `ENTRYPOINT ["dotnet","MeuSecretario.Api.dll"]`. O contexto de build é `./dotnet`, e `.dockerignore` próprio exclui `bin/`, `obj/`, `tests/`, `specs/`.

**Fontes e bibliotecas nativas (gráficos, spec 013 RN-013-75).** A imagem `aspnet` não traz fontes nem `libfontconfig`. O Dockerfile instala `fontconfig`, `libfontconfig1`, `libfreetype6` e `fonts-dejavu-core` (e roda `fc-cache -f`). Verificado em 2026-10-09 com o `ScottPlotChartRenderer` real dentro do container: na imagem `aspnet` pura o PNG sai sem nenhum texto (título, percentuais e legenda em branco) e o renderizador não acusa erro; na imagem do Dockerfile os textos saem com acentos (`Saúde`, `R$ 1.200,50`). Para repetir: publicar um console que chama `RenderPie`/`RenderBars`/`RenderLine`, montá-lo em `docker run --entrypoint dotnet meusecretario-api:<tag> /smoke/Smoke.dll /out` e abrir os PNG. Custo: cerca de 15 MB na imagem.

### Mudanças no workflow (Fase 4)
1. **Build antes do `down`** (feito): `docker compose build` ⇒ `docker compose up -d` (recria só o que mudou). Elimina o downtime de build, e um build que falha não derruba a versão atual.
2. **Sem `docker system prune -f` global** (feito; o host é compartilhado com outras stacks): `docker image prune -f --filter "label=app=meu-secretario"` e remoção das imagens antigas anotadas. Acrescentado: `concurrency` para um deploy por vez.
3. `git reset --hard origin/${GITHUB_REF_NAME}` (hoje fixo em `main`).
4. Etapa de testes (`ci.yml` verde) como pré-requisito do deploy.
5. Heredoc do `.env` mantém as mesmas variáveis; remover `PYTHON_VERSION` e `DB_PASSWORD`.
6. Health check mantido (`127.0.0.1:8080/` ⇒ 200) e acrescentar `GET /api/health`.

### CI (`.github/workflows/ci.yml`, novo)
`actions/setup-dotnet` com a versão do `global.json` ⇒ restore ⇒ build (`-warnaserror`) ⇒ `dotnet test` com cobertura ⇒ artefato do relatório. Roda em PRs e em push.

## 3. Homologação

Objetivo: testar o .NET e capturar respostas douradas do Flask sem tocar a produção.

- **Banco**: `bot_whatsapp_homol` no mesmo `postgres-central`, criado a partir da **estrutura** de produção (sem dados) + categorias globais. Scripts em `dotnet/scripts/homol/` (ver `README.md` de lá). Dados de teste: um usuário de teste criado por `POST /auth/register` no ambiente de homol.
- **Stack**: `docker-compose.homol.yml` (nomes `-homol`, porta local `127.0.0.1:8081`, Redis próprio `meu-secretario-redis-homol`, `REDIS_URL` com nome único para não colidir com o alias `redis`).
- **Envio de WhatsApp**: apontar para um número de teste do Rafael; nunca para usuários reais. Se o bot de homol não existir, usar `BOT_WHATSAPP_URL` de um fake (WireMock) e validar o payload.
- **Flask em homol** (para respostas douradas): o mesmo código atual, subido com `DATABASE_URL` de homol, em porta separada.
- **Angular**: `ng serve` com proxy apontando para a porta de homol.

## 4. Riscos operacionais (resumo; plano completo em `cutover-runbook.md`)

1. Nomes referenciados por terceiros: `meu-secretario-api:8000`, `meu-secretario-nginx` (logrotate, monitor-security.sh, health check), `meu-secretario-redis`, nginx nativo da VPS apontando para `127.0.0.1:8080/8443`.
2. Quatro jobs executavam `python` dentro do container; com imagem .NET param no momento da troca ⇒ Quartz precisa estar ativo no mesmo deploy.
3. Motor de agendamentos sem idempotência e disparado por agente externo ⇒ nunca ter Flask e .NET com scheduler ativo ao mesmo tempo.
4. Formato dos valores no Redis (ver `integrations.md` §7).
5. Volumes de log: `./logs` recebe `security.log` e `{job}.log`; `./nginx/logs` é lido por fail2ban/logrotate no host.
6. Fuso e cultura: fixar no app; garantir `tzdata` e ICU na imagem.
7. `oauth_proxy`: confirmar o `GOOGLE_REDIRECT_URI` real (default aponta para Render). Se o URI atual passa pelo proxy, o .NET precisa responder em `/oauth2callback` no host registrado no Google Cloud Console.
8. Segredo `API_SECRET_KEY` aparece nos labels do Ofelia (visível em `docker inspect`); resolvido ao remover o Ofelia.

## 5. Variáveis novas da feature 009 (filtro de segurança, entrega 009-0)

Entram nos três lugares (heredoc do `.env` do workflow, `environment` do compose e, se secretas, secrets do GitHub). Nenhuma é secreta e todas têm padrão, então nada precisa mudar para o app subir.

| Variável | Padrão | Efeito |
|---|---|---|
| `BOT_PROTECTION_ENABLED` | `true` | liga o filtro de segurança (bloqueio de IP, blacklist, padrões suspeitos). Como no Flask, só o texto `true` liga |
| `SECURITY_HEADERS_ENABLED` | `true` | liga os headers de segurança (HSTS, CSP, `X-Frame-Options`...) |
| `SECURITY_LOG_FILE` | `<LOG_DIR>/security.log` | arquivo do `security.log`; só é gravado com `LOG_TO_FILE=true` |
| `SECURITY_LOG_LEVEL` | `WARNING` | nível mínimo do `security.log` (`DEBUG`, `INFO`, `WARNING`, `ERROR`, `CRITICAL`) |
| `RATE_LIMIT_ENABLED` | `false` | liga o rate limit por IP real (desvio 081). **Só ligar depois de conferir os 429 nos logs de produção** (Q-009-1) |
| `RATE_LIMIT_PER_HOUR` | `300` | balde geral por IP |
| `RATE_LIMIT_ADMIN_PER_HOUR` | `60` | balde de `/admin/*` por IP |

`AUTO_BLOCK_ENABLED`, `BLOCK_DURATION_MINUTES` e `MAX_SUSPICIOUS_ATTEMPTS` existem no `.env` do Flask mas nenhum código as lia; o .NET também as ignora (5 tentativas em 10 min, bloqueio de 60 min, fixos).

Pontos de atenção:
- O IP do cliente vem do `X-Forwarded-For` lido **da direita para a esquerda**, só quando a origem da conexão é da rede privada (loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, `fc00::/7`, `fe80::/10`). O nginx do compose precisa continuar mandando `X-Forwarded-For`.
- Chamadas internas (origem privada **sem** `X-Forwarded-For`: bot, health do compose, Ofelia) passam sem filtro e sem rate limit.
- **Ofelia:** os labels de `trigger-monthly-reports-*` chamam `http://localhost:5000`, mas o .NET escuta na porta 8000. Ou os labels passam para `localhost:8000`, ou o Ofelia deixa de existir na virada (o Quartz da 013 assume). Sem isso essas chamadas falham como já falhavam no Flask.
- Respostas 404 de rota inexistente: IP externo recebe `{"error":"Not found"}` (JSON, conta tentativa); chamada interna recebe o HTML do Flask.