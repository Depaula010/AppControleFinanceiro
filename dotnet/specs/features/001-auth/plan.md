# Plan 001 — Auth

- **Spec:** `spec.md` (aprovada em 2026-10-02)

## 1. Domain (`MeuSecretario.Domain/Auth`)

Funções puras, sem I/O, todas com teste unitário.

| Tipo | Assinatura | Papel |
|---|---|---|
| `PhoneNumber` | `static string Digits(string? raw)` | só dígitos (RN-001-05) |
| `CardDays` | `static CardDaysResult Parse(JsonElement? venc, JsonElement? fech)` | conversão estilo `int()` e faixa 1..31, na ordem da spec §4.1 |
| `PasswordRules` | `static bool IsValid(string trimmed)` | mínimo de 6 caracteres |
| `DefaultAccounts` | `static IReadOnlyList<NewAccount> For(int vencimento, int fechamento)` | as 3 contas padrão (nomes e tipos literais) |

`JsonElement` fica na camada Application; o Domain recebe `object?`/tipos primitivos já extraídos. (Se o teste de arquitetura reclamar, `CardDays.Parse` recebe `object?` e a Application faz a extração.)

## 2. Application (`MeuSecretario.Application/Auth`)

**Portas** (em `Application/Abstractions`):
- `IPasswordHasher { string Hash(string password); bool Verify(string hash, string password); }`
- `IEncryptionService { string Encrypt(string plain); string Decrypt(string token); bool LooksEncrypted(string value); string DecryptOrPlain(string value); }`
- `IJwtTokenService { string Issue(int userId); JwtValidation Validate(string token); }` com `JwtValidation { bool IsValid; int? UserId; }` (UserId nulo = token válido sem `user_id`)
- `IUserRepository { Task<UserRecord?> FindByWhatsappAsync(string digits, ct); Task<CreateUserResult> CreateWithDefaultAccountsAsync(NewUser, IReadOnlyList<NewAccount>, ct); }` onde `CreateUserResult` é `Created(id)` ou `WhatsappTaken`.
- `ICurrentUser { int UserId { get; } }`

**Casos de uso** (uma classe cada, método `ExecuteAsync`, resultado tipado com o código de erro da spec, sem referência a HTTP):
- `RegisterUser` → `RegisterResult` (`Success(userId)`, `MissingFields`, `PasswordTooShort`, `MissingDays`, `DayOutOfRange(campo)`, `DaysNotInteger`, `WhatsappTaken`)
- `LoginUser` → `LoginResult` (`Success(token, user)`, `MissingFields`, `NotRegistered`, `NoPassword`, `WrongPassword`)
- `VerifyToken` → `VerifyResult` (`Valid(userId)`, `MissingToken`, `Invalid`, `NoUserId`)

O mapeamento de resultado para status e texto fica no endpoint (único lugar com as mensagens em português), não no caso de uso.

## 3. Infrastructure

| Classe | Detalhe |
|---|---|
| `WerkzeugPasswordHasher` | `Hash`: `pbkdf2:sha256:<iter>` com iterações lidas de configuração (padrão 1.000.000, igual ao Werkzeug 3.1; avaliar custo de CPU no container, ver §7) e salt de 16 caracteres `[A-Za-z0-9]` via `RandomNumberGenerator`. `Verify`: faz o parse de `método$salt$hex`; `pbkdf2:sha256:N` via `Rfc2898DeriveBytes.Pbkdf2(passwordUtf8, saltUtf8, N, SHA256, 32)`; `scrypt:N:r:p` via `BouncyCastle.Cryptography` (`SCrypt.Generate(passwordUtf8, saltUtf8, N, r, p, 64)`); `CryptographicOperations.FixedTimeEquals`; formato desconhecido ou mal formado devolve `false`. Pacote novo: `BouncyCastle.Cryptography` |
| `FernetEncryptionService` | Chave base64 url-safe de 32 bytes (16 de assinatura + 16 de AES). Token: `0x80 ‖ timestamp(8, big-endian) ‖ IV(16) ‖ AES-128-CBC/PKCS7 ‖ HMAC-SHA256(32)`, tudo em base64 url-safe com padding. `Decrypt` valida o HMAC antes de decifrar e **não** aplica TTL. Chave inválida: falha no boot (validação no `AppSettings` ou no construtor) |
| `JwtTokenService` | `System.IdentityModel.Tokens.Jwt`/`JsonWebTokenHandler` com `SymmetricSecurityKey(UTF8(API_SECRET_KEY))`; `ValidAlgorithms = [HS256]`, `ValidateIssuer/Audience = false`, `RequireExpirationTime = true`, `ClockSkew = Zero`, `MapInboundClaims = false`; emissão: `user_id` como número, `iat` e `exp` em segundos. Pacote novo: `Microsoft.IdentityModel.JsonWebTokens` (sem o middleware JwtBearer: a validação é por filtro, para reproduzir as 4 mensagens) |
| `DbConnectionFactory` | `IDbConnectionFactory.OpenAsync()` devolve `NpgsqlConnection` aberta com a `AppSettings.ConnectionString` |
| `UserRepository` (**Dapper**) | `FindByWhatsappAsync`: `SELECT id, nome, numero_whatsapp, senha_hash FROM usuarios WHERE numero_whatsapp = @whatsapp`. `CreateWithDefaultAccountsAsync`: uma transação com `INSERT INTO usuarios (nome, numero_whatsapp, senha_hash, api_key_automate) VALUES (...) RETURNING id` e um `INSERT INTO contas (...)` por conta; `PostgresException` com `SqlState = 23505` e `ConstraintName = 'usuarios_numero_whatsapp_key'` vira `WhatsappTaken` |

Dapper (não EF) porque `usuarios` e `contas` são compartilhadas com a feature 002 e um mapeamento EF único criaria um arquivo comum que bloqueia o trabalho em paralelo. O `AppDbContext` nasce quando houver uma feature que realmente precise dele.

## 4. Api (`MeuSecretario.Api/Auth`)

- `AuthModule : IFeatureModule` registra os serviços (`AddScoped<RegisterUser>` etc.) e mapeia `/auth/register`, `/auth/login`, `/auth/verify`.
- **Leitura do corpo sem model binding**: o endpoint lê o corpo bruto (`HttpRequest.Body`) e faz `JsonDocument.Parse` dentro de `try`. Corpo ausente, inválido ou não-objeto vira o 400 da rota (desvio 024). Valores não-texto em campos de texto contam como vazios. Isso evita o 400 automático do ASP.NET (que teria outro formato).
- `Envelopes.A` ganha `SuccessWithFields(IReadOnlyDictionary<string, object?> fields, string? message, int statusCode)`, que põe os campos na raiz na ordem informada. Teste unitário novo.
- `RequireUserFilter : IEndpointFilter` (+ `RequireUser()` como extension em `RouteHandlerBuilder`) implementa a §4.4 da spec e popula `ICurrentUser` (scoped, preenchido pelo filtro).
- Os textos dos erros ficam numa classe estática `AuthMessages` (constantes em português), usada pelos endpoints e pelos testes.

## 5. Migration (desvio 017)

Arquivo `dotnet/migrations/0001_usuarios_api_key_varchar255.sql`, idempotente, seguindo `adr/0001-migrations.md`:
```sql
ALTER TABLE usuarios ALTER COLUMN api_key_automate TYPE varchar(255);
```
Aplicada primeiro na homol e na virada (passo no `cutover-runbook.md`). Sem ela, o cadastro falha no `INSERT` (valor longo demais).

## 6. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário | `Domain/Auth/*Tests.cs` | AC-001-02, 04, 06, 07 (conversão e faixa de dias, dígitos do telefone, regra da senha, contas padrão) |
| Unitário | `Infrastructure/WerkzeugPasswordHasherTests` | AC-001-26, 27, 50 (fixtures Python: pbkdf2 e scrypt; formato inválido devolve `false`; hash gerado verificável) |
| Unitário | `Infrastructure/FernetEncryptionServiceTests` | AC-001-51, 52 (vetores oficiais e token do Python) |
| Unitário | `Infrastructure/JwtTokenServiceTests` | AC-001-28, 32, 34 (emissão, validação, `alg=none`, expirado, sem `exp`, token do PyJWT) |
| Unitário | `Application/Auth/*Tests.cs` com NSubstitute | casos de uso: ordem das validações, resultado por cenário (AC-001-03 a 09, 20 a 25, 30 a 33) |
| Unitário | `Api/EnvelopesTests` (acrescentar) | `SuccessWithFields` |
| Integração (Docker) | `Auth/RegisterEndpointTests`, `LoginEndpointTests`, `VerifyEndpointTests` | AC-001-01, 08 a 13, 20, 21, 30; transação, corrida de cadastro, 409 por unicidade |
| Integração | `Auth/RequireUserFilterTests` (rota de teste protegida registrada só no teste) | AC-001-40 a 44 |
| Contrato | fixtures `tests/Fixtures/auth/*.json` | respostas exatas (corpo e status) de cada caso, comparadas campo a campo |

**Fixtures geradas pelo Python** (script versionado `dotnet/tests/Fixtures/generate_auth_fixtures.py`, saída versionada, chaves e senhas de teste fictícias): hashes pbkdf2 e scrypt do Werkzeug, token Fernet, JWT do PyJWT (válido, expirado, sem `user_id`, com `alg=none`). Rodar o script exige o Python do `.venv` (já tem Werkzeug, PyJWT e cryptography); o resultado é commitado, então o CI não precisa de Python.

**Testes de integração precisam de Docker** (Testcontainers sobe um PostgreSQL 15 com `schema-prod.sql`). Ficam marcados `[Trait("Category","Docker")]`; o `ci.yml` os roda; localmente exigem o Docker Desktop ligado.

## 7. Riscos e decisões

- **Custo do PBKDF2 com 1.000.000 iterações**: cerca de 0,3 a 0,5 s por login por núcleo. Aceitável para 1 usuário. O número fica em configuração (`PASSWORD_HASH_ITERATIONS`, padrão 1.000.000) e é medido no homol antes da virada.
- **Scrypt com N=32768, r=8**: usa 32 MiB por verificação; o BouncyCastle aloca isso na memória gerenciada. Sem problema no volume atual.
- **`MapInboundClaims`**: se esquecido, o claim `user_id` é lido corretamente mas `sub`-mapeamentos podem surpreender; coberto por AC-001-28 e 34.
- **Sem JwtBearer middleware**: deliberado, para devolver as 4 mensagens específicas de 401 em envelope A e permitir `Bearer` sem diferenciar maiúsculas.
- **Corrida de cadastro**: tratada pela violação de unicidade (23505), não por checagem prévia.
- **Fernet sem TTL**: o Python decifra sem `ttl`; o .NET também não valida a idade do token.
- **Desvio 031** altera mensagens de validação do cadastro; fica `proposto` até aprovação. Sem aprovação, o .NET reproduz o Flask (cadastra com WhatsApp vazio e responde 500 em dias não inteiros).
