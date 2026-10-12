# Servidor (VPS Contabo): guia para novos projetos

Documento para quem vai subir **outro projeto no mesmo servidor** (pessoa ou Claude). Descreve o que existe, como as aplicações rodam e as convenções a seguir. Foi escrito em 2026-10-12 a partir do que foi observado durante a migração do Meu Secretário; o que **não** foi confirmado está marcado com **[confirmar]** e há um roteiro de levantamento no fim (seção 9).

> Segredos nunca entram neste arquivo, em repositório, em log nem em chat. Se colar saída de comando em uma conversa, apague senhas e chaves antes.

## 1. Visão geral

- **Máquina:** VPS Contabo, hostname `vmi2917131`, IP público `212.47.65.37`, Linux, acesso por SSH como `root` (chave). Fuso do servidor: o relógio do host está em CEST (UTC+2); os containers .NET definem `TZ=America/Sao_Paulo` explicitamente.
- **Tudo roda em Docker** (`docker compose` v2), exceto o **nginx do host** (instalado nativamente, porta 80). Não há domínio/HTTPS próprio apontando para o servidor: o front fala com `http://212.47.65.37`, e callbacks que exigem HTTPS (OAuth do Google) passam por um proxy no Render (seção 6).
- **Rede Docker compartilhada:** `rede-global` (externa, criada uma vez). Containers de projetos diferentes se enxergam por **nome de container** nessa rede (ex.: `postgres-central:5432`, `meu-secretario-api:8000`).
- **Deploy:** GitHub Actions por SSH (seção 5). Cada projeto vive em sua própria pasta.

## 2. O que roda hoje (observado em 2026-10-12)

| Container | Projeto | Observação |
|---|---|---|
| `postgres-central` | banco compartilhado | `postgres:15-alpine`, SCRAM-SHA-256, publicado só em `127.0.0.1:5432`, redes `rede-interna` + `rede-global`, healthcheck, volume `pgdata`; compose do próprio projeto `postgres-central` |
| `meu-secretario-api` | Meu Secretário (.NET 10) | porta 8000 interna; build de `./dotnet`; `/opt/meu-secretario` |
| `meu-secretario-nginx` | Meu Secretário | `127.0.0.1:8080->80` e `127.0.0.1:8443->443`; proxy para `meu-secretario-api:8000` |
| `meu-secretario-redis` | Meu Secretário | `redis:7-alpine`, AOF, volume `redis-data`, só na rede |
| `whatsapp-saas-bot` | bot Baileys multi-tenant | host `3001` -> container `3000`; pasta `/root/whatsapp-saas`; compose `name: whatsapp-saas` |
| `caixaestoque-api`, `caixaestoque-api-homolog` | projeto Caixa/Estoque | **[confirmar]** portas e pastas |
| `caixaestoque-postgres`, `caixaestoque-postgres-homolog` | Postgres do Caixa/Estoque | instância própria, separada do `postgres-central` **[confirmar versão e portas]** |
| `redis-local` | Redis avulso | no ar há meses; dono **[confirmar]** |

Imagens são construídas **no próprio servidor** (`docker compose build`), não há registry.

## 3. Banco de dados: `postgres-central`

- **Um servidor, vários bancos** (um por projeto/ambiente): `bot_whatsapp_prod` (Meu Secretário), `bot_whatsapp_homol`, `bot_whatsapp_refatorado` (bot antigo; candidato a remoção) e o do bot SaaS.
- Dentro da rede Docker: `postgresql://USUARIO:SENHA@postgres-central:5432/BANCO`. Do seu computador: **túnel SSH** (o HeidiSQL usa o tipo "PostgreSQL (SSH tunnel)", servidor `127.0.0.1`, porta `5432`). A porta **não** é pública.
- Usuário administrador: `postgres_admin`. **Não use o admin nas aplicações.** Para um projeto novo:
  ```sql
  CREATE ROLE meuprojeto_user LOGIN PASSWORD '<senha-forte>';
  CREATE DATABASE meuprojeto OWNER meuprojeto_user;
  ```
  Um usuário por projeto, dono só do banco dele.
- Produção e homol de um mesmo projeto ficam na **mesma instância**: o isolamento é só por nome de banco e por usuário. Nunca aponte homol para o banco de produção (use nomes terminados em `_homol` e usuários diferentes).
- **Backups:** o repositório do `postgres-central` tem `backup-postgres.sh`; não foi confirmado que esteja agendado **[confirmar `crontab -l`]**. Backup manual: `docker exec postgres-central pg_dump -U postgres_admin -d BANCO -Fc > BANCO-AAAAMMDD.dump`. O export do HeidiSQL **não** restaura no Postgres (sintaxe MySQL).
- Collation `en_US.utf8` sobre Alpine (ordem por bytes); timezone do banco UTC.

## 4. Roteamento HTTP

```
Internet :80 ─▶ nginx do host (nativo) ─▶ 127.0.0.1:8080 ─▶ container meu-secretario-nginx ─▶ meu-secretario-api:8000
Internet :3001 ───────────────────────────────────────────▶ whatsapp-saas-bot:3000   (publicado direto)
```

- O nginx do host é quem recebe a porta 80. A configuração dele **não está em nenhum repositório** **[confirmar o caminho: `nginx -T`]**. Para publicar um projeto novo é preciso adicionar um `server`/`location` nele (por IP/porta ou por caminho) e `nginx -s reload`. Cuidado com a regra do Meu Secretario: **o filtro de segurança do app devolve 404 e bloqueia o IP por 1h para caminhos fora das rotas dele**; um projeto novo na mesma porta/caminho precisa ter um prefixo próprio.
- Portas já ocupadas no host: **80**, **8080**, **8443** (loopback), **3001**, **5432** (loopback), e as do Caixa/Estoque **[confirmar]**. Antes de escolher uma porta: `ss -tlnp`.
- Containers de aplicação **não publicam porta**: só o nginx do projeto publica, em `127.0.0.1`. Exposição pública só quando necessário (como o bot na 3001, que o front chama direto).

## 5. Padrão de deploy (GitHub Actions + SSH)

Cada projeto tem um workflow como `.github/workflows/deploy.yml`. O do Meu Secretário é um bom modelo:

1. **Gatilho:** push em `main` (com `paths-ignore` para docs; `[skip ci]` também evita).
2. **Segredos do repositório** (GitHub → Settings → Secrets): `CONTABO_SSH_KEY`, `CONTABO_HOST`, `CONTABO_USER` + os do app (`DATABASE_URL`, chaves de API...). O workflow escreve o `.env` do servidor a partir deles em **todo deploy** (não edite o `.env` na mão, ele é sobrescrito).
3. Passos remotos (`ssh $USER@$HOST bash << 'ENDSSH'`): `cd /opt/<projeto>` → `git fetch && git reset --hard origin/main` → escreve `.env` → `docker compose down` → `docker compose build` → (migrations) → `docker compose up -d` → health check por `curl 127.0.0.1:<porta>`.
4. **Armadilhas já vividas:**
   - `docker compose run/exec` dentro do heredoc **consome o stdin do SSH e engole o resto do script**: sempre `-T` e `< /dev/null`.
   - `down` antes de `build` = o serviço fica **fora do ar durante o build** (~3 min). Para zero downtime, faça o build antes do `down`.
   - `docker system prune -f` no workflow apaga cache de build de todos os projetos; evite em servidor compartilhado.
   - O primeiro `git clone` em `/opt/<projeto>` e a pasta precisam existir antes do primeiro deploy.
5. **Convenções:** `container_name` fixo e prefixado pelo projeto; rede `rede-global` declarada `external: true`; `restart: unless-stopped`; `TZ` explícito; logs no console (`docker logs`).

## 6. Serviços externos e redes de apoio

- **Proxy HTTPS no Render** (`meu-financeiro-proxy.onrender.com`, repositório `Depaula010/meu-financeiro-proxy`, `proxy_server.py`, plano gratuito: dorme por inatividade): repassa qualquer rota, com query string, para `http://212.47.65.37`. Existe porque o Google OAuth exige redirect HTTPS e o servidor não tem certificado. Qualquer projeto novo que precise de HTTPS público pode reaproveitar o padrão ou, melhor, ganhar um domínio com certificado (Let's Encrypt) no nginx do host.
- **WhatsApp:** bot Baileys multi-tenant (`whatsapp-saas-bot`). Cada projeto é um *tenant*: recebe uma API key (`POST /api/v1/admin/api-keys`, header `X-Admin-Key`) e cria **sessões** (`POST /api/v1/sessions`) com `webhook_url`, `webhook_signature_key` e QR para vincular um número. Detalhes e armadilhas no `CLAUDE.md` do Meu Secretário (seção 5): o SaaS recusa `webhook_url` com nome de container; envie com URL pública e troque por SQL para o nome interno; a chave `x-api-key` do webhook vem de `sessions.metadata.webhook_auth_token`.
- **Gemini, Google (Calendar/Drive), WeatherAPI, OpenRoute:** chaves por projeto, nunca compartilhadas em arquivo.

## 7. Como começar um projeto novo (checklist)

1. Pasta `/opt/<projeto>` no servidor e `git clone` do repositório (chave de deploy somente leitura se possível).
2. `docker-compose.yml` com `container_name` próprio, rede `rede-global` externa, sem publicar portas da aplicação (só o nginx do projeto em `127.0.0.1:<porta livre>`).
3. Banco: criar usuário e banco no `postgres-central` (seção 3); `DATABASE_URL` com o host `postgres-central`.
4. Redis: use uma instância própria no compose do projeto (como `meu-secretario-redis`) em vez do `redis-local`, para não misturar chaves.
5. Workflow de deploy a partir do modelo (seção 5) e secrets no GitHub.
6. Roteamento no nginx do host (seção 4) e teste por `curl -I http://212.47.65.37/...`.
7. Health check, backup do banco e uma linha em **este arquivo** descrevendo o que foi acrescentado.

## 8. Segurança (resumo)

- Postgres só em loopback e SCRAM; acesso externo por túnel SSH. Mantenha assim.
- O `postgres_admin` e as senhas de bancos já apareceram em conversas de trabalho do Meu Secretário e **devem ser rotacionados** (pendente). Trate qualquer segredo colado em chat como exposto.
- Chaves de API não ficam em código de front (`environment.ts`): o navegador as expõe. Injete por proxy no servidor.
- O firewall do host **[confirmar `ufw status`]**: a porta 3001 do bot responde de fora; a 3000 antiga não respondia.
- Bloqueio automático de IP e filtro de rotas do Meu Secretário (`security:*` no Redis): para desbloquear em emergência, `docker exec meu-secretario-redis sh -c "redis-cli --scan --pattern 'security:blocked:*' | xargs -r redis-cli del"`, ou o endpoint admin com `x-api-key`.

## 9. Roteiro de levantamento (somente leitura) para completar os [confirmar]

Rode no servidor e cole o resultado (sem segredos) na conversa do novo projeto:

```bash
hostnamectl | head -5; df -h /; free -m | head -2; uptime
docker ps --format 'table {{.Names}}\t{{.Image}}\t{{.Ports}}\t{{.Status}}'
docker network ls; docker network inspect rede-global --format '{{range .Containers}}{{.Name}} {{end}}'
docker compose ls
ss -tlnp | awk 'NR==1 || /LISTEN/' | cut -c1-110
ls -la /opt /root | head -40
nginx -T 2>/dev/null | grep -E "server_name|listen|proxy_pass|location" | head -60
crontab -l; ls /etc/cron.d
ufw status 2>/dev/null | head -20
for c in caixaestoque-api redis-local; do docker inspect $c --format '{{.Name}} {{index .Config.Labels "com.docker.compose.project.working_dir"}} {{.HostConfig.PortBindings}}'; done
```

Com isso, substitua os **[confirmar]** acima por fatos e remova esta seção.
