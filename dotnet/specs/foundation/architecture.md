# Arquitetura

**Monólito modular em camadas (Clean Architecture enxuta), um único container.**
Escolhido por ter um único mantenedor: microserviços e CQRS com barramento só adicionariam custo. As camadas existem para isolar o que mais precisa de teste unitário, as regras financeiras.

## Solução

```
dotnet/
  MeuSecretario.sln
  global.json                  fixa o SDK 10 estável
  Directory.Build.props        TreatWarningsAsErrors, nullable, analyzers
  Directory.Packages.props     versões centralizadas (Central Package Management)
  Dockerfile
  src/
    MeuSecretario.Domain/
    MeuSecretario.Application/
    MeuSecretario.Infrastructure/
    MeuSecretario.Api/
  tests/
    MeuSecretario.UnitTests/
    MeuSecretario.IntegrationTests/
  specs/
  scripts/
```

## Camadas e dependências

```
Api ──► Application ──► Domain
 │           ▲
 └─► Infrastructure ──┘ (implementa interfaces da Application)
```

Regras: `Domain` não referencia nada. `Application` referencia só `Domain`. `Infrastructure` referencia `Application` e `Domain`. `Api` referencia tudo e compõe a DI. Um teste de arquitetura (NetArchTest) falha o build se a regra for violada.

| Camada | Contém | Não contém |
|---|---|---|
| Domain | Entidades, value objects, cálculo de fatura, parcelas, reserva, potes, sinal do valor, enums de tipo | EF, HTTP, Redis, relógio do sistema |
| Application | Casos de uso por feature, DTOs de entrada/saída, validadores, portas (`IBotClient`, `ILlmClient`, `ICalendarClient`, `ICache`, repositórios) | Detalhes de Npgsql, HttpClient, Redis |
| Infrastructure | `AppDbContext`, queries Dapper, Redis, Fernet, hasher Werkzeug, clientes HTTP, Gemini, Google, jobs Quartz | Regra de negócio |
| Api | Minimal APIs agrupadas por feature, envelopes, filtros de auth, middleware de segurança, `Program.cs` | Regra de negócio, SQL |

## Organização por feature

Dentro de cada camada, o código é agrupado por feature, não por tipo técnico:

```
Application/Accounts/CreateAccount.cs, UpdateAccount.cs, AccountDtos.cs, AccountValidators.cs
Api/Accounts/AccountsEndpoints.cs, AccountsModule.cs
Infrastructure/Accounts/AccountRepository.cs
```

Cada feature expõe `Add<Feature>Module(IServiceCollection)` e `Map<Feature>Endpoints(IEndpointRouteBuilder)`. `Program.cs` descobre os módulos por reflexão, para duas features nunca editarem o mesmo arquivo.

## Decisões

**Casos de uso como classes simples.** Cada um é uma classe com um método `ExecuteAsync`, registrada na DI. Sem MediatR.

**EF Core para CRUD, Dapper para relatórios.** EF mapeia o modelo relacional simples e os upserts. As ~15 queries analíticas (CTE, `date_trunc`, window function, `TO_CHAR`) são portadas literalmente em Dapper para evitar riscos de tradução. Ver `data-model.md`.

**Transação por caso de uso.** O caso de uso abre a unidade de trabalho; repositórios não commitam. Isso corrige a mistura atual de commits dentro de serviços.

**Envelopes como tipos.** `EnvelopeA`, `EnvelopeB` etc. em `Api/Contracts`, escolhidos explicitamente por rota. Nunca um handler global que "uniformiza" as respostas.

**Serialização JSON.** `System.Text.Json` com nomes de propriedade exatos (`[JsonPropertyName]` ou política snake_case por DTO, conforme o contrato), `Decimal` como número, datas `YYYY-MM-DD`, `UnsafeRelaxedJsonEscaping` e sem ordenação alfabética de chaves. Os clientes não dependem da ordem; o Flask ordena alfabeticamente e escapa Unicode (`\uXXXX`), o que é equivalente em JSON.

**Segurança de rotas.** O middleware deriva os caminhos válidos do `EndpointDataSource` em vez de uma lista manual. Mantém: 404 para caminho desconhecido, contagem de tentativas no Redis (5 em 10 min, bloqueio por 60 min), blacklist, e desbloqueio por `/admin/security-unblock-ip`. Os formatos de chave Redis e corpo de erro são preservados (ver `integrations.md`).

**Jobs.** `BackgroundService` + Quartz.NET no mesmo processo, com lock em Redis (`job_lock:{nome}`) mantido para o caso de mais de uma instância. Substitui Ofelia e os scripts Python.

**Tempo.** `TimeProvider` injetado; `IClock.Today` devolve a data em `America/Sao_Paulo`. Nenhum `DateTime.Now` no código.

**Configuração.** `IOptions<T>` validado na inicialização; falha rápida se faltar `API_SECRET_KEY` (mínimo 32 caracteres), `ENCRYPTION_KEY` ou `DATABASE_URL`. `DATABASE_URL` em formato URL é convertido para connection string Npgsql.

**Logs.** Serilog com saída em console e em `/app/logs/*.log`; logger `security` separado em `security.log`. Filtro que remove valores de `x-api-key`, `user_api_key`, `Authorization` e tokens.
