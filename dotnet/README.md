# Meu Secretário — backend .NET 10

Reescrita do backend Flask. Leia `../CLAUDE.md` e `specs/README.md` antes de mexer.

## Rodar

```bash
cp .env.example .env            # preencha; use o banco de HOMOL, nunca o de produção
# exporte as variáveis do .env (ou use dotnet user-secrets) e:
dotnet run --project src/MeuSecretario.Api
```

`GET http://localhost:5000/` e `GET /api/health` respondem sem login. A aplicação aborta no boot, listando os nomes, se faltar variável obrigatória.

## Testes

```bash
dotnet test MeuSecretario.slnx
```

- `MeuSecretario.UnitTests`: sem I/O (configuração, relógio, envelopes, regras de dependência entre camadas).
- `MeuSecretario.IntegrationTests`: API em memória via `WebApplicationFactory`; os testes com Postgres em container entram junto com a primeira feature que grava no banco.

## Estrutura

`src/MeuSecretario.{Domain,Application,Infrastructure,Api}`, `tests/`, `specs/`, `scripts/homol/`. Regras de dependência em `specs/foundation/architecture.md` (verificadas por `ArchitectureTests`).

## Nova feature

1. Spec aprovada em `specs/features/NNN-nome/`.
2. Crie a pasta `Api/<Feature>/` com uma classe `<Feature>Module : IFeatureModule`; ela é descoberta sozinha (não edite `Program.cs`).
3. Use os envelopes de `Api/Contracts/Envelopes.cs` (A–E) escolhidos pela spec.
4. `Application` e `Domain` não referenciam EF, Npgsql, Redis nem ASP.NET.
