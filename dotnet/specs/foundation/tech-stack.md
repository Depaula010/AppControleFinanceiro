# Stack tecnológica

SDK: **.NET 10** (LTS). `global.json` fixa a versão estável; a máquina de desenvolvimento tem hoje apenas um preview do SDK 10 (`10.0.400-preview`), então instalar o SDK estável é pré-requisito da Fase 1.

## Bibliotecas

| Área | Escolha | Por quê |
|---|---|---|
| Web | ASP.NET Core Minimal APIs, `Microsoft.AspNetCore.OpenApi`, Scalar | Nativo e leve. Constraints `{id:int}` reproduzem o `<int:id>` do Flask (inclusive o 404 para valor não numérico). |
| Dados (CRUD) | EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` | Modelo relacional simples; entidades geradas por scaffold do schema real; `ToTable("usuarios")` explícito. |
| Dados (relatórios) | Dapper | CTEs, `date_trunc`, `TO_CHAR`, window function e `ANY(:ids)` portados literalmente. |
| Migrations | EF Core Migrations, baseline vazia | Substitui Alembic e as rotas `/admin/setup-*` (que fazem DDL via GET). |
| Redis | `StackExchange.Redis` | Mesmo Redis, mesmas chaves. |
| Auth JWT | `Microsoft.AspNetCore.Authentication.JwtBearer` | HS256 com `API_SECRET_KEY`; `MapInboundClaims = false`, `ClockSkew = 0`, sem issuer/audience, lê o claim `user_id`. |
| Hash de senha | Implementação própria compatível com Werkzeug | `pbkdf2:sha256:<iter>$<salt>$<hex>` via `Rfc2898DeriveBytes.Pbkdf2` (salt como bytes UTF-8, iterações lidas do hash); `scrypt:N:r:p$salt$hex` via `BouncyCastle.Cryptography`. Usuários atuais continuam logando. |
| Fernet | Implementação própria (~80 linhas) | AES-128-CBC + HMAC-SHA256 conforme a especificação; validada com os vetores oficiais. Pacotes NuGet de Fernet estão sem manutenção. |
| Validação | FluentValidation | Mensagens em português idênticas às atuais, testáveis isoladamente. |
| HTTP externo | `IHttpClientFactory` + `Microsoft.Extensions.Http.Resilience` | Timeouts e retry do bot (10s/30s), WeatherAPI (5s), OpenRoute (15s, 2 tentativas). |
| IA | SDK oficial do Gemini atrás de `ILlmClient` | Chave por usuário passada por chamada; o Flask usa `genai.configure` global, que vaza chave entre requisições. Modelo `gemini-2.5-flash-lite` configurável. |
| Google | `Google.Apis.Calendar.v3`, `Google.Apis.Drive.v3`, `Google.Apis.Auth` | SDKs oficiais; escopos `calendar` e `drive.file`. |
| Jobs | Quartz.NET | Cron com fuso, misfire policy, no mesmo processo. |
| Gráficos | ScottPlot 5 | PNG em memória para o relatório mensal e o intent de gráfico. |
| Fuzzy match | FuzzySharp | Equivalente do rapidfuzz no casamento de nomes de conta. |
| Rate limit | `Microsoft.AspNetCore.RateLimiting` + `ForwardedHeaders` | Corrige o balde único por IP do nginx. |
| Logs | Serilog (console + arquivo) | Estruturado, com máscara de segredos. |

## Testes

| Escolha | Uso |
|---|---|
| xUnit 2.9.x | Runner. A v3 exige projeto executável e o runner Microsoft.Testing.Platform; migrar quando estável com o SDK 10 |
| NSubstitute | Fakes de portas |
| Shouldly | Asserções (FluentAssertions virou licença comercial) |
| `Testcontainers.PostgreSql` | Postgres real nos testes de integração |
| Respawn | Limpeza do banco entre testes |
| `Microsoft.AspNetCore.Mvc.Testing` | `WebApplicationFactory` |
| Verify | Snapshots de resposta JSON (paridade) |
| NetArchTest | Regras de dependência entre camadas |
| coverlet | Cobertura |

## Deliberadamente fora

- **MediatR, AutoMapper, MassTransit**: licença comercial e/ou complexidade sem retorno aqui.
- **Moq**: episódio do SponsorLink; NSubstitute cobre o caso.
- **Hangfire**: exige armazenamento próprio; Quartz em memória com lock Redis basta.
- **ORM único para tudo**: relatórios ficam em SQL.

## Requisitos de runtime

Imagem `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian, não Alpine, por causa de ICU e tzdata) com `tzdata` e ICU instalados; `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. Healthcheck por `curl` (instalar) ou endpoint `GET /` sem dependência de Python.
