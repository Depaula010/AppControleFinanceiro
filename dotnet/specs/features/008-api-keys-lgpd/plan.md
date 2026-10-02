# Plan 008 — API keys e LGPD

- **Spec:** `spec.md` (em revisão; este plan só vale depois da aprovação)

## 1. Domain (`MeuSecretario.Domain/ApiKeys`)

Funções puras, sem I/O, com teste unitário.

| Tipo | Assinatura | Papel |
|---|---|---|
| `ApiProviders` | `IReadOnlyList<string> All` (`gemini`, `weather`, `openroute`); `bool IsValid(string?)`; `const string ValidListText = "gemini, weather, openroute"` | RN-008-01 |
| `KeyTypes` | `const Own = "propria"`, `System = "sistema"` | tipo de chave |
| `PreferenceKind` | `static string Label(bool ownKey)` | `"própria (grátis)"` / `"sistema (cobrado)"` (RN-008-05) |
| `ConsentCatalog` | `IReadOnlyList<ConsentType> All` (tipo, descrição, obrigatório, ordem do Flask); `bool IsValid(string)`; `string? Description(string)`; `const string CurrentVersion = "1.0"`; `string InvalidTypeMessage(string tipo)` (lista no formato `repr` do Python) | RN-008-08 |
| `UsageSummary` | `static UsageSummary From(IEnumerable<UsageRow>)` → total, própria, sistema | §4.8 |
| `PlanLimit` | `static bool Exceeded(int? limit, long used)` (`limit is not null && used >= limit`) | §5 porta |
| `AccountDeletion` | `const string Confirmation = "CONFIRMO_DELECAO"`; `bool IsConfirmed(string?)` | RN-008-11 |
| `UsageMonth` | `static string Of(DateTime localNow)` → `yyyy-MM` | RN-008-07 |

## 2. Application

**Portas** (`Application/Abstractions`):
- `IApiKeyProvider { Task<ApiKeyResolution> ResolveAsync(int userId, string provider, CancellationToken ct); }` (**a porta da 010**).
  - `abstract record ApiKeyResolution`: `Resolved(string Key, string KeyType)` (`ToString()` sem a chave), `Failed(ApiKeyFailure Reason, string Message)`.
  - `enum ApiKeyFailure { InvalidProvider, NoPreference, OwnKeyMissing, SystemKeyUnavailable, LimitReached }`. As mensagens literais ficam em `ApiKeyMessages` (Application), usadas pela porta e pelos testes.
- `IApiKeyValidator { bool IsValid(string? presented); }` (compara com a `API_SECRET_KEY`; reutilizável por 009 e 012).
- `IApiKeyRepository`: `InsertAsync(userId, provider, encrypted)` → `InsertResult { Created(id), UserNotFound, AlreadyExists }`; `ListByUserAsync`; `UpdateAsync(keyId, ownerUserId?, encrypted?, active?)` → `KeyRef?` (provedor, dono); `DeactivateAsync(keyId, ownerUserId?)` → `KeyRef?`; `FindActiveEncryptedAsync(userId, provider)`; `TouchLastUseAsync(userId, provider)`.
- `IPreferenceRepository`: `UpsertAsync` → `Created(id)` ou `UserNotFound`; `ListAsync`; `DeleteAsync(userId, provider)` → `bool`; `FindAsync(userId, provider)` → `bool?`.
- `IUsageRepository`: `QueryMonthAsync(userId, month, provider?)`; `IncrementAsync(userId, provider, keyType, month)`; `SumMonthAsync(userId, provider, month)`.
- `IAccessLogRepository`: `ListAsync(userId, provider?, success?, limit)` (só leitura; escrita só se P-008-8 for aprovado).
- `IPlanRepository`: `FindActivePlanAsync(userId)` → `ActivePlan?` (`NomePlano`, três limites); `FindLatestSubscriptionAsync(userId)` (export).
- `IConsentRepository`: `InsertAsync(...)` → `Created(id)`, `UserNotFound`, `AlreadyRegistered`; `ListAsync`; `LatestGivenAsync(userId, tipo)` → `bool?`.
- `IAccountDataRepository` (LGPD): `ExportAsync(userId)` (leituras do §4.13) e `DeleteApiDataAsync(userId)` → contagens, **em uma transação**; `DeleteAccountCompletelyAsync` só nasce se P-008-4 for aprovado.
- `ISystemApiKeys { string? For(string provider); }` (lê `AppSettings`).
- Reaproveitadas: `IEncryptionService`, `IClock`.

**Casos de uso** (um por classe, `ExecuteAsync`, resultado tipado, sem HTTP): `RegisterApiKey`, `ListApiKeys`, `UpdateApiKey`, `DeactivateApiKey`, `SetPreference`, `ListPreferences`, `DeletePreference`, `GetUsage`, `GetAccessLogs`, `RegisterConsent`, `ListConsents`, `GetInitialConsents`, `ExportUserData`, `DeleteAccountData`, `CheckConsent`. A validação na ordem da spec fica no caso de uso; o texto em português só no endpoint (`ApiKeysMessages`, como `AuthMessages`).

`ApiKeyProvider : IApiKeyProvider` (Application) implementa a tabela da spec §5 usando as portas acima; best-effort (`TouchLastUse`, `Increment`, verificação de limite e leitura de preferência) com `try/catch` que registra `SafeError.Describe` pela porta `ILogger<ApiKeyProvider>` e segue (exceto o erro de limite). Decifrar: `encryption.LooksEncrypted(v) ? encryption.Decrypt(v) : v`, com falha propagada como exceção (não usar o retorno "texto original" do `DecryptOrPlain` para token cifrado inválido; pergunta 7). Para texto puro legado o resultado é idêntico ao `DecryptOrPlain`.

## 3. Infrastructure (`Infrastructure/ApiKeys`, Dapper; sem `AppDbContext`)

Um repositório por porta, `IDbConnectionFactory`, SQL parametrizado (nomes de tabela minúsculos, sem aspas). `PostgresException` com `SqlState` `23503` ⇒ `UserNotFound`; `23505` ⇒ `AlreadyExists`/`AlreadyRegistered`; demais sobem.

```sql
-- ApiKeyRepository
INSERT INTO chavesapiusuario (usuario_id, provedor, chave_api_criptografada, ativo, criado_em, atualizado_em)
VALUES (@userId, @provider, @encrypted, TRUE, now(), now()) RETURNING id;

SELECT id, provedor, ativo, ultimo_uso_em, criado_em, atualizado_em
FROM chavesapiusuario WHERE usuario_id = @userId ORDER BY provedor, criado_em DESC;

UPDATE chavesapiusuario SET /* montado só com fragmentos fixos */
  chave_api_criptografada = @encrypted, ativo = @active, atualizado_em = now()
WHERE id = @keyId AND (@ownerUserId::int IS NULL OR usuario_id = @ownerUserId)
RETURNING provedor, usuario_id;

UPDATE chavesapiusuario SET ativo = FALSE, atualizado_em = now()
WHERE id = @keyId AND (@ownerUserId::int IS NULL OR usuario_id = @ownerUserId)
RETURNING provedor, usuario_id;

SELECT chave_api_criptografada FROM chavesapiusuario
WHERE usuario_id = @userId AND provedor = @provider AND ativo = TRUE;

UPDATE chavesapiusuario SET ultimo_uso_em = now(), atualizado_em = now()
WHERE usuario_id = @userId AND provedor = @provider;

-- PreferenceRepository
INSERT INTO preferenciaschaveapi (usuario_id, provedor, usar_chave_propria, atualizado_em)
VALUES (@userId, @provider, @own, now())
ON CONFLICT (usuario_id, provedor) DO UPDATE SET usar_chave_propria = EXCLUDED.usar_chave_propria, atualizado_em = now()
RETURNING id;
SELECT id, provedor, usar_chave_propria, atualizado_em FROM preferenciaschaveapi WHERE usuario_id = @userId ORDER BY provedor;
DELETE FROM preferenciaschaveapi WHERE usuario_id = @userId AND provedor = @provider RETURNING id;
SELECT usar_chave_propria FROM preferenciaschaveapi WHERE usuario_id = @userId AND provedor = @provider;

-- UsageRepository
SELECT provedor, tipo_chave, SUM(quantidade_chamadas) AS total_chamadas, mes_ano, MAX(atualizado_em) AS ultima_atualizacao
FROM rastreamentousoapi
WHERE usuario_id = @userId AND mes_ano = @month AND (@provider::text IS NULL OR provedor = @provider)
GROUP BY provedor, tipo_chave, mes_ano ORDER BY provedor, tipo_chave;

INSERT INTO rastreamentousoapi (usuario_id, provedor, tipo_chave, quantidade_chamadas, mes_ano, atualizado_em)
VALUES (@userId, @provider, @keyType, 1, @month, now())
ON CONFLICT (usuario_id, provedor, tipo_chave, mes_ano)
DO UPDATE SET quantidade_chamadas = rastreamentousoapi.quantidade_chamadas + 1, atualizado_em = now();

SELECT COALESCE(SUM(quantidade_chamadas), 0) FROM rastreamentousoapi
WHERE usuario_id = @userId AND provedor = @provider AND mes_ano = @month;

-- AccessLogRepository
SELECT id, provedor, tipo_chave, operacao, sucesso, mensagem_erro, criado_em
FROM logacessochaveapi
WHERE usuario_id = @userId AND (@provider::text IS NULL OR provedor = @provider) AND (@success::boolean IS NULL OR sucesso = @success)
ORDER BY criado_em DESC LIMIT @limit;

-- PlanRepository
SELECT p.limite_gemini, p.limite_weather, p.limite_openroute, p.nome_plano
FROM assinaturasusuario a JOIN planos p ON a.plano_id = p.id
WHERE a.usuario_id = @userId AND a.status = 'ativo' AND (a.data_fim IS NULL OR a.data_fim > now())
ORDER BY a.data_inicio DESC LIMIT 1;

SELECT a.id, p.nome_plano, p.preco_mensal, p.limite_gemini, p.limite_weather, p.limite_openroute, a.data_inicio, a.data_fim, a.status
FROM assinaturasusuario a JOIN planos p ON a.plano_id = p.id
WHERE a.usuario_id = @userId ORDER BY a.data_inicio DESC LIMIT 1;

-- ConsentRepository
INSERT INTO consentimentousuario (usuario_id, tipo_consentimento, consentimento_dado, versao_consentimento, data_consentimento, ip_consentimento)
VALUES (@userId, @tipo, @given, @version, now(), NULL) RETURNING id;
SELECT id, tipo_consentimento, consentimento_dado, versao_consentimento, data_consentimento
FROM consentimentousuario WHERE usuario_id = @userId ORDER BY data_consentimento DESC;
SELECT consentimento_dado FROM consentimentousuario
WHERE usuario_id = @userId AND tipo_consentimento = @tipo ORDER BY data_consentimento DESC LIMIT 1;

-- AccountDataRepository.Export (mesmo `SELECT` de chaves com chave_api_criptografada, preferências, uso, logs)
SELECT id, provedor, chave_api_criptografada, ativo, ultimo_uso_em, criado_em, atualizado_em
FROM chavesapiusuario WHERE usuario_id = @userId ORDER BY provedor, id;
SELECT provedor, tipo_chave, quantidade_chamadas, mes_ano FROM rastreamentousoapi
WHERE usuario_id = @userId ORDER BY mes_ano DESC, provedor, tipo_chave;
SELECT provedor, tipo_chave, sucesso, mensagem_erro, timestamp_acesso FROM logacessochaveapi
WHERE usuario_id = @userId ORDER BY timestamp_acesso DESC LIMIT 1000;

-- AccountDataRepository.DeleteApiData: uma transação, nesta ordem, contando linhas afetadas
DELETE FROM chavesapiusuario WHERE usuario_id = @userId;
DELETE FROM preferenciaschaveapi WHERE usuario_id = @userId;
DELETE FROM logacessochaveapi WHERE usuario_id = @userId;
DELETE FROM rastreamentousoapi WHERE usuario_id = @userId;
DELETE FROM consentimentousuario WHERE usuario_id = @userId;
DELETE FROM assinaturasusuario WHERE usuario_id = @userId;
```

O `UPDATE` de chave monta o `SET` com fragmentos fixos (nunca concatena entrada). Os parâmetros opcionais do dono usam cast explícito para o Npgsql inferir o tipo. A exportação lê tudo em uma única conexão com `ReadCommitted`; consentimentos reutilizam `IConsentRepository.ListAsync`.

**Migrations:** nenhuma (a spec usa só colunas existentes). Se P-008-4 for aprovado, nenhuma migration também (deleção explícita em código). Faixa reservada 0030–0039 não usada.

**Segurança/validador:** `ApiSecretKeyValidator : IApiKeyValidator` (Infrastructure/Security): `SHA256(presented)` e `SHA256(secret)` com `CryptographicOperations.FixedTimeEquals`; `null`/vazio ⇒ falso.

**DI:** `AddApiKeysInfrastructure(IServiceCollection)` chamada pelo `ApiKeysModule.AddServices` (Api referencia Infrastructure); não se edita `DependencyInjection.cs` nem `Program.cs`. `ISystemApiKeys` lê `AppSettings.GeminiApiKey/WeatherApiKey/OpenRouteApiKey`.

## 4. Api (`MeuSecretario.Api/ApiKeys/` e `Api/Http/`)

- **`Api/Http/RequireApiKeyFilter.cs` (008-0, compartilhado):**
  `RequireApiKeyFilter(IApiKeyValidator validator, Func<HttpContext, IResult> unauthorized) : IEndpointFilter` lê `X-API-KEY` (primeiro valor), valida e, se falhar, devolve `unauthorized(context)` sem chamar `next` (nem ler o corpo). Extensão `RequireApiKey<TBuilder>(this TBuilder b, Func<HttpContext, IResult> unauthorized)` via `AddEndpointFilterFactory` (resolve o validador pelo `ApplicationServices`). Presets em `ApiKeyUnauthorized`: `EnvelopeC` (`Envelopes.C.Error("Não autorizado", "API key inválida ou ausente", 401)`); 009 e 012 acrescentam B e E sem alterar a classe.
- **`Api/Http/PythonDateFormat.cs` (008-0):** `Iso(DateTimeOffset?)` → `yyyy-MM-ddTHH:mm:ss[.ffffff]+00:00` (fração só se os microssegundos não forem 0; `null` ⇒ `null`) e `IsoLocal(DateTime)` → `yyyy-MM-ddTHH:mm:ss[.ffffff]` sem fuso (usado em `data_exportacao`; mesma regra de fração do `isoformat()`).
- **`Api/ApiKeys/ApiKeysModule : IFeatureModule`**: registra serviços e mapeia as 16 rotas num `MapGroup("/api-keys")`; todas menos `/health` com `.RequireApiKey(ApiKeyUnauthorized.EnvelopeC)`. IDs com `{usuarioId:regex(^\d+$)}`/`{chaveId:regex(^\d+$)}` e conversão `int.TryParse` (acima de `int.MaxValue` ⇒ `FlaskNotFound`); `provedor` e `tipo_consentimento` como `string`.
- Corpo lido com `JsonBody.ReadObjectAsync` (024); campos de texto com `GetText`; `GetBool(name)` novo em `ApiKeys/ApiKeysBody.cs` (local à feature; devolve `null` se ausente/nulo, ou um marcador `Invalid` para tipo errado ⇒ 500 de texto fixo da rota, P-008-9). `usuario_id` inteiro do corpo por `GetRaw`.
- Envelope C em tudo: `Envelopes.C.Success(IReadOnlyDictionary)` com as chaves na ordem da spec; `Envelopes.C.Error(erro, mensagem, status)`. Os 500 usam `ApiKeysMessages.<Rota>Failed` (texto fixo).
- Logs: `ApiKeysLog` (`[LoggerMessage]`, EventIds 8001+, mesmo padrão do `AuthLog`) com `ILogger<ApiKeysModule>`; parâmetros só `Error` (de `SafeError.Describe`) e ids numéricos. Nunca o corpo, a chave, o `X-API-KEY`.
- Cabeçalho `Cache-Control: no-store` em `/lgpd/exportar`.
- `ApiKeysMessages`: constantes em português literais da spec (títulos, mensagens, lista `repr` do Python, texto de confirmação).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário | `Domain/ApiKeys/*Tests` | provedores, catálogo e mensagem `repr`, `UsageSummary`, `PlanLimit.Exceeded` (borda `>=`), confirmação exata, mês `yyyy-MM` na virada (AC-008-12, 30, 42, 66) |
| Unitário | `Infrastructure/ApiSecretKeyValidatorTests` | AC-008-02 (vazio, nulo, tamanhos diferentes, correto) |
| Unitário | `Api/RequireApiKeyFilterTests` (`DefaultHttpContext`) | AC-008-01 e o 401 em envelopes C/B/E, `next` não chamado |
| Unitário | `Api/PythonDateFormatTests` | fração só com microssegundos, `+00:00`, `null` |
| Unitário | `Application/ApiKeys/ApiKeyProviderTests` (NSubstitute, `FakeTimeProvider`) | AC-008-60 a 68: ordem das decisões, mensagens literais, fail-open, nenhum segredo em `ToString()` e logs |
| Unitário | `Application/ApiKeys/*UseCaseTests` | ordem das validações por rota, resultado por cenário, IDOR no caso de uso (AC-008-11, 12, 16, 17, 21, 41, 42, 48) |
| Integração (Docker) | `ApiKeys/ApiKeysEndpointsTests` (`PostgresFixture`, `[Trait("Category","Docker")]`) | AC-008-01, 03, 04, 10 a 22, 30, 31; corpo exato como texto; 500 sem vazamento (AC-008-13, 50) |
| Integração | `ApiKeys/IdorTests` | AC-008-17 com dois usuários semeados; chave de A intacta após tentativa com `usuario_id` de B |
| Integração | `ApiKeys/FernetKeysTests` | chave do Python (fixture Fernet) e texto puro legado em `exportar` e na porta (AC-008-19, 47, 63) |
| Integração | `ApiKeys/LgpdEndpointsTests` | AC-008-40 a 49 (colunas reais, `timestamp_acesso`, `assinatura` com plano Prata/expirada/sem, rollback de `deletar-conta` com falha injetada, dados de outro usuário e `usuarios` intactos) |
| Integração | `ApiKeys/ApiKeyProviderRepositoryTests` | upsert de uso, soma do mês, plano ativo/expirado, `ultimo_uso_em` (AC-008-63 a 66) |
| Arquitetura | existente (NetArchTest) | Application sem Npgsql; Api sem Infrastructure.Dapper |
| Contrato | asserções de corpo exato nos testes de integração (como na 001) | spec §4 |

`PostgresFixture.ResetAsync` precisa limpar as sete tabelas e semear `planos` (Bronze, Prata, Ouro), pois o baseline não traz dados; o seed fica no arquivo de teste da feature, não no fixture compartilhado.

## 6. Fakes e fixtures

- **Fixture Fernet do Python:** `tests/Fixtures/generate_apikeys_fixtures.py` (usa o `.venv`) gera `apikeys-fixtures.json` com chaves de API fictícias cifradas com a `ENCRYPTION_KEY` fictícia já usada nas `AuthFixtures`, mais um texto puro legado e um token adulterado; saída versionada, CI sem Python.
- `FakeTimeProvider` para o mês de uso (inclui 31/10 23:30 UTC-3 virando mês em UTC).
- Fake de `IApiKeyRepository` & cia. nos testes de caso de uso; `ISystemApiKeys` fake.
- `SystemApiKeys` de teste via `UseSetting("GEMINI_API_KEY", ...)` do `PostgresFixture`.
- Chaves de teste sempre fictícias; nenhum teste imprime a chave.

## 7. Riscos e decisões

- **Segredo único e exposição externa (desvios 005 e 006).** Ver spec §3 e pergunta 1. Recomendação: restringir `/api-keys/` no nginx; o código não faz restrição por IP.
- **IDOR residual:** `usuario_id` opcional deixa `PUT/DELETE /chave/<id>` sem dono quando omitido (P-008-5).
- **`DeleteAccountData` em paridade deixa o usuário no banco**, com mensagem de sucesso enganosa; é o comportamento do Flask até P-008-4 ser aprovado. Se aprovado, a ordem explícita (§4.14 da spec) é obrigatória por causa dos `RESTRICT`; teste com usuário com contas, transações, agendamentos e subcategorias próprias.
- **`DecryptOrPlain`** engole falha de decifra. A porta e o export usam `LooksEncrypted` + `Decrypt` para não devolver cifrado como chave. Se a pergunta 7 for rejeitada, trocar por `DecryptOrPlain`.
- **Formato de data:** `+00:00` assume sessão UTC no Flask (pergunta 4). O formatador é um ponto único para ajustar.
- **Chave global comparada com hash:** não vaza tamanho nem conteúdo por tempo; limite de tentativas inválidas é da 009.
- **Porta consumida pela 010:** a 010 deve tratar `Failed.Message` como texto a exibir ao usuário e não registrar `Resolved.Key`. Mudar a assinatura depois da aprovação exige reabrir esta spec.
- **Uso conta tentativa de obtenção, não sucesso da chamada externa** (paridade), e conta chave própria no limite do plano (P-008-7).
- **`assinaturasusuario` vazia e sem rota de criação:** o limite de plano nunca dispara hoje; o teste cobre a regra, mas não há dado real que a exercite.
