# Homol local (Docker) e roteiro de fumaça

Ambiente descartável na máquina do desenvolvedor para exercitar o .NET de ponta a ponta sem tocar em produção nem no servidor. Banco vazio criado pelas próprias migrations (`--migrate`), Redis novo e `BOT_WHATSAPP_URL` vazia (nenhuma mensagem é enviada).

## Subir

Gere os segredos **fora do repositório** (valores aleatórios; nada disto é versionado):

```bash
PGPASS=$(python -c "import secrets;print(secrets.token_urlsafe(18))")
docker run -d --name ms-homol-pg -e POSTGRES_USER=homol -e POSTGRES_PASSWORD=$PGPASS -e POSTGRES_DB=bot_whatsapp_homol -p 127.0.0.1:55432:5432 postgres:15-alpine
docker run -d --name ms-homol-redis -p 127.0.0.1:56379:6379 redis:7-alpine

export DATABASE_URL=postgresql://homol:$PGPASS@localhost:55432/bot_whatsapp_homol
export REDIS_URL=redis://localhost:56379/0
export API_SECRET_KEY=$(python -c "import secrets;print(secrets.token_urlsafe(48))")
export ENCRYPTION_KEY=$(python -c "import base64,os;print(base64.urlsafe_b64encode(os.urandom(32)).decode())")
export GEMINI_API_KEY=homol-fake-key BOT_WHATSAPP_URL= JOBS_ENABLED=false ASPNETCORE_URLS=http://127.0.0.1:5099

dotnet run --project ../../../src/MeuSecretario.Api -- --migrate     # baseline + migrations
dotnet run --project ../../../src/MeuSecretario.Api                  # API em :5099
```

## Rodar o roteiro

```bash
ADMIN_API_KEY=$API_SECRET_KEY HOMOL_PG_CONTAINER=ms-homol-pg BASE_URL=http://127.0.0.1:5099 python smoke.py
```

`smoke.py` cria um usuário de teste pela API e verifica cadastro, login, contas, categorias, transações (inclusive o ciclo da fatura do cartão, desvio 065), contas mensais, dashboard, configurações, alertas de calendário, segurança e as rotas `/admin`. Sai com código 1 se algo falhar. Para repetir, apague os dados do usuário com `DELETE FROM usuarios` (**não** use `TRUNCATE ... CASCADE`: apagaria as categorias globais).

## Limites

- O Gemini é falso: o webhook do WhatsApp e tudo o que classifica mensagem pela IA **não** são exercitados aqui (a paridade da 012 é verificada pelo harness Python dos testes).
- Google (Calendar, Drive) e o bot de WhatsApp não existem neste ambiente.
- A comparação com o Flask exige o homol do servidor (`docker-compose.homol.yml`), com o Flask em porta separada.

## Parar

```bash
docker rm -f ms-homol-pg ms-homol-redis
```
