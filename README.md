# Meu Secretário

Assistente financeiro e de agenda por WhatsApp: registra gastos e receitas por mensagem, controla contas mensais, faturas de cartão, reserva de emergência, alertas de vencimento e de compromissos (Google Calendar), com integração a Google Drive, clima e rotas.

- **Backend:** .NET 10 (`dotnet/`), PostgreSQL, Redis, Quartz. Em produção desde 2026-10-12. O backend anterior em Flask/Python está na tag `flask-final`.
- **Front:** Angular (repositório `meusecretariofront`).
- **Bot de WhatsApp:** Baileys (Node), multi-sessão.
- **Documentação de manutenção:** [CLAUDE.md](CLAUDE.md). Specs, contrato HTTP, modelo de dados e desvios em [dotnet/specs](dotnet/specs).

## Rodar e testar

```bash
dotnet build dotnet/MeuSecretario.slnx
dotnet test  dotnet/MeuSecretario.slnx      # precisa do Docker Desktop (Testcontainers)
```

Configuração por variáveis de ambiente (modelo em `.env.example`). Homol local descartável em `dotnet/scripts/homol/local`.

## Deploy

Push em `main` dispara `.github/workflows/deploy.yml` (SSH no servidor, build, migrations, `docker compose up`). Detalhes e rollback em [CLAUDE.md](CLAUDE.md#4-produção).
