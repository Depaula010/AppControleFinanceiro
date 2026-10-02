# Plan 002 — Contas

- **Spec:** `spec.md` (em revisão; este plano só vale depois da aprovação da spec e dos desvios propostos P-002-1 a P-002-9, ou da decisão de reproduzir o Flask em cada um)

Reaproveita o que a 001 entregou: `Envelopes.A`, `RequireUser()`/`ICurrentUser`, `IDbConnectionFactory`, `SafeError`, o padrão de log com `LoggerMessage` (`AuthLog`), `PostgresFixture` e `AuthFixtures`. **Nenhum arquivo compartilhado é editado** (`Program.cs`, `DependencyInjection.cs`, `Directory.*.props`, `JsonBody.cs`): tudo vive em pastas `Accounts/` de cada camada. Sem EF e sem `AppDbContext` (Dapper, como `UserRepository`). Sem pacote novo.

## 1. Domain (`MeuSecretario.Domain/Accounts`)

Funções puras, sem I/O, todas com teste unitário (meta Domain ≥ 90%).

| Tipo | Assinatura | Papel |
|---|---|---|
| `RawValue` | `abstract record`: `Absent`, `Null`, `Text(string)`, `Integer(BigInteger)`, `Decimal(double)`, `Bool(bool)`, `Other` | valor JSON bruto, para reproduzir `float()`/`int()` e distinguir ausente de `null` sem referenciar `System.Text.Json` |
| `RawBody` | `sealed class RawBody(IReadOnlyDictionary<string, RawValue>)` com `Get(string) => RawValue` (devolve `Absent`), `Has(string)`, `IsEmpty` | corpo já lido; `null` no lugar do objeto = corpo ausente/inválido/não-objeto |
| `PyNumber` | `static bool TryFloat(RawValue, out decimal)`; `static bool TryInt(RawValue, out int)` (faixa 1..31 fica no chamador) | RN-002-09: `float()` aceita número, `Bool`, texto com espaços/sinal/expoente (cultura invariante); `int()` trunca decimal, aceita texto inteiro e `Bool`; `float()` aceita `nan`/`inf` mas não cabem em `decimal`: a conversão lança `OverflowException` ⇒ 500 (spec RN-002-09 e P-002-2); valor finito fora de `numeric(15,2)` chega ao banco e falha lá (`22003` ⇒ 500). Se o P-002-2 for aprovado, esses casos passam a `false` ⇒ 400 |
| `AccountTypes` | `const string[] All` (ordem da mensagem), `bool IsValid(string)`, `string InvalidMessageSuffix` | RN-002-07 |
| `AccountName` | `static string Normalize(string raw)` (`trim` + corte em 100 pontos de código via `EnumerateRunes`) e `static string Truncate(string)` | RN-002-08 |
| `AccountRules` | `static CreateValidation ValidateCreate(RawBody body)`, `static UpdateStep…` ver abaixo | ordem das validações das spec §3.2 e §3.3 |

Resultados tipados (sem HTTP, sem texto em português):
- `CreateValidation`: `Ok(NewAccountData)` ou `Error(AccountError)`.
- `AccountError` (enum): `DataMissing`, `InvalidFieldType` (vira o 500 reproduzido, P-002-2), `NameRequired`, `TypeRequired`, `TypeInvalid`, `BalanceInvalid`, `LimitInvalid`, `ClosingDayInvalid`, `DueDayInvalid`, `NameEmpty`, `NothingToUpdate`.
- `NewAccountData(string NomeConta, string TipoConta, decimal SaldoInicial, string? CorHex, string? Icone, decimal? LimiteCredito, int? DiaFechamento, int? DiaVencimento)`; os defaults (`0`, `#3B82F6`, `wallet`) são aplicados só com a chave **ausente**; `null` explícito segue `null`.

`AccountRules.ValidateCreate` aplica as linhas 1 a 9 da §3.2 (a 10, nome duplicado, precisa do banco e fica no caso de uso). Para o PUT, o banco entra **no meio** da sequência (404 antes dos campos; duplicidade entre nome e tipo), então o Domain expõe passos independentes que o caso de uso encadeia na ordem da §3.3:

```
UpdateRules.ReadName(RawBody)   -> NameStep   { NotPresent | Value(string) | Error(InvalidFieldType|NameEmpty) }
UpdateRules.ReadFields(RawBody) -> FieldsResult { Ok(AccountChanges parcial) | Error(AccountError) }   // tipo, saldo, cor, ícone, limite, dias, nessa ordem; NothingToUpdate se nada veio
```
`AccountChanges` usa `Change<T>` (`IsSet`, `Value`) por campo, para distinguir "não veio" de "veio `null`".

## 2. Application (`MeuSecretario.Application/Accounts`)

**Porta** (`Application/Accounts/IAccountRepository.cs`):
```
Task<IReadOnlyList<AccountListItem>> ListActiveAsync(int userId, CancellationToken ct);
Task<bool> ExistsActiveAsync(int userId, int accountId, CancellationToken ct);
Task<bool> NameTakenAsync(int userId, string name, int? exceptAccountId, CancellationToken ct);   // todas as contas, ativas e inativas (016)
Task<CreateAccountResult> CreateAsync(int userId, NewAccountData data, CancellationToken ct);      // Created(AccountDetails) | NameTaken
Task<UpdateAccountResult> UpdateAsync(int userId, int accountId, AccountChanges changes, CancellationToken ct); // Updated(AccountDetails) | NotFound | NameTaken
Task<DeleteAccountResult> SoftDeleteAsync(int userId, int accountId, CancellationToken ct);        // Deleted | NotFound | HasTransactions(int count)
```
`AccountListItem(int Id, string Nome, string Tipo, decimal Saldo)`; `AccountDetails(int Id, string NomeConta, string TipoConta, decimal SaldoInicial, string? CorHex, string? Icone, decimal? LimiteCredito, int? DiaFechamento, int? DiaVencimento)`.

**Casos de uso** (uma classe cada, `ExecuteAsync`, resultado tipado, sem referência a HTTP; `ICurrentUser` é lido no endpoint e entra como `userId`):
- `ListAccounts` → `ListAccountsResult(IReadOnlyList<AccountListItem>)`.
- `CreateAccount(RawBody? body)` → `Created(details)` | `Invalid(AccountError)` | `NameTaken`. Fluxo: corpo nulo/vazio ⇒ `DataMissing`; `AccountRules.ValidateCreate`; `NameTakenAsync(userId, nome, null)`; `CreateAsync` (a corrida também devolve `NameTaken`).
- `UpdateAccount(int accountId, RawBody? body)` → `Updated(details)` | `Invalid(AccountError)` | `NotFound` | `NameTaken`. Fluxo (a ordem **é** a spec §3.3): 1 corpo vazio ⇒ `DataMissing`; 2 `ExistsActiveAsync` ⇒ `NotFound`; 3 `ReadName`; se veio, `NameTakenAsync(userId, nome, accountId)`; 4 `ReadFields`; 5 `UpdateAsync`.
- `DeleteAccount(int accountId)` → `Deleted` | `NotFound` | `HasTransactions(int)` (delegando ao repositório, que faz tudo numa transação).

Os textos em português **não** ficam aqui: o endpoint mapeia resultado ⇒ status + mensagem (`AccountsMessages`), como na 001.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Accounts`)

`AccountRepository(IDbConnectionFactory)` com **Dapper**; singleton como `UserRepository`; registrado por uma extensão própria `AddAccountsInfrastructure(this IServiceCollection)` (em `Infrastructure/Accounts/AccountsRegistration.cs`), chamada pelo `AccountsModule`; assim `DependencyInjection.cs` não é editado. Nomes de coluna minúsculos, sem aspas.

**Listar** (desvio 007: `AND t.consolidada = true`; ordem como o Flask, sem `COLLATE`):
```sql
SELECT c.id AS Id, c.nome_conta AS Nome, c.tipo_conta AS Tipo,
       c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS Saldo
FROM contas c
LEFT JOIN transacoes t ON t.conta_id = c.id AND t.consolidada = true
WHERE c.usuario_id = @userId AND c.ativa = true
GROUP BY c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial
ORDER BY c.tipo_conta, c.nome_conta
```

**Existe ativa:**
```sql
SELECT EXISTS (SELECT 1 FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true)
```

**Nome em uso** (sem `ativa`; desvio 016; `@exceptId` nulo no POST):
```sql
SELECT EXISTS (SELECT 1 FROM contas
               WHERE usuario_id = @userId AND nome_conta = @name
                 AND (@exceptId::int IS NULL OR id <> @exceptId))
```

**Criar** (um `INSERT ... RETURNING`; substitui o `INSERT` + `SELECT` por nome do Flask):
```sql
INSERT INTO contas (usuario_id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone,
                    limite_credito, dia_fechamento, dia_vencimento, ativa, inclui_saldo_total, created_at)
VALUES (@UserId, @NomeConta, @TipoConta, @SaldoInicial, @CorHex, @Icone,
        @LimiteCredito, @DiaFechamento, @DiaVencimento, true, true, CURRENT_TIMESTAMP)
RETURNING id AS Id, nome_conta AS NomeConta, tipo_conta AS TipoConta, saldo_inicial AS SaldoInicial,
          cor_hex AS CorHex, icone AS Icone, limite_credito AS LimiteCredito,
          dia_fechamento AS DiaFechamento, dia_vencimento AS DiaVencimento
```
`PostgresException` com `SqlState = 23505` em `contas` (`TableName = 'contas'` e `ConstraintName` contendo `nome_conta`; o nome padrão é `contas_usuario_id_nome_conta_key`) ⇒ `NameTaken`. Qualquer outra (CHECK `23514`, `22001` texto longo, `22003` overflow) **propaga** ⇒ 500 (spec §3.2). O teste de integração roda contra o baseline para confirmar o nome da constraint.

**Atualizar** (SQL montado só com fragmentos fixos, nunca com nome vindo do usuário; valores sempre parametrizados). Fragmentos: `nome_conta = @NomeConta`, `tipo_conta = @TipoConta`, `saldo_inicial = @SaldoInicial`, `cor_hex = @CorHex`, `icone = @Icone`, `limite_credito = @LimiteCredito`, `dia_fechamento = @DiaFechamento`, `dia_vencimento = @DiaVencimento`:
```sql
UPDATE contas SET <fragmentos dos campos com IsSet>
WHERE id = @accountId AND usuario_id = @userId AND ativa = true
RETURNING id AS Id, nome_conta AS NomeConta, ... (mesma lista do INSERT)
```
Zero linhas ⇒ `NotFound` (a conta foi removida entre a checagem e o `UPDATE`). 23505 ⇒ `NameTaken`; 23514 (`chk_cartao_credito`) e demais ⇒ propagam (500, P-002-3). O trigger atualiza `updated_at`.

**Remover** (uma transação; RN-002-15):
```sql
SELECT id FROM contas WHERE id = @accountId AND usuario_id = @userId AND ativa = true FOR UPDATE;
SELECT COUNT(*) FROM transacoes WHERE conta_id = @accountId;
UPDATE contas SET ativa = false WHERE id = @accountId AND usuario_id = @userId;
```
Sem linha no primeiro ⇒ `NotFound`; contagem > 0 ⇒ `HasTransactions(n)` (sem `UPDATE`); senão `Deleted`. Se P-002-6 for aprovado, entram aqui o `COUNT` de `agendamentos` ativos e o `UPDATE usuarios SET conta_padrao_* = NULL ...`.

Conversões: `double`/texto do JSON ⇒ `decimal` já no Domain (`PyNumber`, via representação mais curta `"R"`, para o arredondamento ficar com o `numeric(15,2)` do banco como no Flask); leitura devolve `decimal`. O `limite_credito` zero vira `null` **só na serialização** (RN-002-11), não no repositório.

**Migrations: nenhuma.** Os números 0010–0019 reservados a esta feature ficam sem uso. Sugestão fora do escopo: `CREATE INDEX IF NOT EXISTS idx_transacoes_conta_id ON transacoes (conta_id)` (o baseline não tem índice em `transacoes.conta_id`; o JOIN do saldo e o `COUNT` leem a tabela inteira). Com ~330 transações não importa; se virar problema, entra como `0010_idx_transacoes_conta_id.sql`, aditiva e idempotente, por PR próprio (a tabela é da feature 004).

## 4. Api (`MeuSecretario.Api/Accounts`)

- `AccountsModule : IFeatureModule`. `AddServices`: `AddAccountsInfrastructure()`, `AddScoped<ListAccounts/CreateAccount/UpdateAccount/DeleteAccount>()`. `ICurrentUser` já é registrado pelo `AuthModule` (não repetir). `MapEndpoints`: para cada base em `["/api/accounts", "/api/contas"]`:
  - `MapGet(base)`, `MapPost(base)` e `MapPut/MapDelete($"{base}/{{id:regex(^[0-9]+$)}}")`, todos `.RequireUser()`.
  - A restrição `regex(^[0-9]+$)` (e não `:int`) reproduz o `<int:id>` do Flask: `abc`, `-1` e `1.5` não casam (404 HTML do roteamento, antes da autenticação), enquanto o `:int` do ASP.NET aceitaria `-1`. O handler recebe `id` como `string` e converte: valor maior que `int.MaxValue` vira id inexistente (404 JSON `"Conta não encontrada"`; RN-002-18, AC-002-86).
- `AccountsBodyReader`: `static async Task<RawBody?> ReadAsync(HttpRequest, ct)`. Lê o corpo bruto com `JsonDocument.ParseAsync` num `try` (corpo ausente, vazio, inválido ou não-objeto ⇒ `null`; não exige `Content-Type`), e converte cada propriedade do objeto para `RawValue` (número inteiro ⇒ `Integer(BigInteger)`, com parte decimal/expoente ⇒ `Decimal(double)`, texto, `true`/`false`, `null`, objeto/lista ⇒ `Other`). Não usa `JsonBody` para valores (ele devolve `null` para ausente e `null` indistintamente) e não edita `JsonBody.cs`.
- `AccountsResponses`: monta os corpos com `Envelopes.A.Success(data, message, status)`:
  - listar: `Success(data: lista de {id, nome, banco, tipo, saldo})` (lista vazia continua indo em `data`); `banco` = `tipo`; `saldo` como `double`.
  - criar (201) / atualizar (200): `Success(data, message)` com `data` na ordem `id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito, dia_fechamento, dia_vencimento`; `limite_credito` `null` quando 0 ou `null`.
  - remover: `Success(null, "Conta removida com sucesso")` ⇒ `{"status":"success","message":"Conta removida com sucesso"}`.
  - erros: `Envelopes.A.Error(AccountsMessages.X, status)`.
- `AccountsMessages` (constantes em português, copiadas do Flask; mudar uma palavra é mudar o contrato): `DataMissing`, `NameRequired`, `TypeRequired`, `TypeInvalid` (montada de `AccountTypes.All`), `BalanceInvalid`, `LimitInvalid`, `ClosingDayInvalid`, `DueDayInvalid`, `NameDuplicate`, `NameDuplicateOther`, `NameEmpty`, `NothingToUpdate`, `NotFound`, `HasTransactions(int n)`, `Created`, `Updated`, `Deleted`, `ListFailed`, `CreateFailed`, `UpdateFailed`, `DeleteFailed`.
- `AccountsLog` (`internal static partial class`, `LoggerMessage`, EventIds 2001–2004: listar, criar, atualizar, remover): recebe `userId` (e `accountId` onde existir) e `string error = SafeError.Describe(ex)`; nunca nome de conta, corpo nem token. Cada endpoint tem `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + `Envelopes.A.Error(<mensagem fixa>, 500)` (desvio 008).
- Mapeamento resultado ⇒ HTTP é um `switch` por rota: `DataMissing`/`*Invalid`/`NameRequired`/... ⇒ 400; `InvalidFieldType` ⇒ 500 (mensagem fixa da rota; P-002-2); `NotFound` ⇒ 404; `NameTaken` ⇒ 400; `HasTransactions` ⇒ 400; `Created` ⇒ 201.

Serialização: `JsonDefaults` já cuida de nomes exatos, acentos e `null`. Números monetários saem de `double` (`(double)decimal`): `100` no lugar de `100.0` (spec §3, sem impacto para clientes JSON).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `Domain/Accounts/PyNumberTests` | AC-002-17, 18 (tabela de entradas: `"12.5"`, `" 7 "`, `true`, `"1,5"`, `[]`, `10.9`, `"10"`, `1e400`, `"nan"`) |
| Unitário | `Domain/Accounts/AccountNameTests` | AC-002-16, 43 (`trim`, corte em 100 pontos de código, emoji, acentos) |
| Unitário | `Domain/Accounts/AccountTypesTests` | AC-002-15 (6 válidos; maiúsculas/acentos; mensagem com os 6 valores) |
| Unitário | `Domain/Accounts/AccountRulesTests` | AC-002-12 a 14, 17 a 19, 24, 27, 43 a 51, 56 (ordem das validações do POST e do PUT, defaults só com chave ausente, `null` explícito, `NothingToUpdate`) |
| Unitário (NSubstitute) | `Application/Accounts/CreateAccountTests`, `UpdateAccountTests`, `DeleteAccountTests`, `ListAccountsTests` | ordem (404 antes dos campos; duplicidade entre nome e tipo), `NameTakenAsync` chamado sem `ativa` e com `exceptId` no PUT, repositório não chamado quando há erro de validação, `NameTaken` vindo de `CreateAsync`/`UpdateAsync` (corrida), AC-002-20 a 23, 41, 44, 70 a 74 |
| Unitário (Api) | `Api/Accounts/AccountsBodyReaderTests` | AC-002-12 (vazio, sem `Content-Type`, JSON inválido, `[1]`, `"abc"`, `null`, `{}`); ausente x `null`; tipos |
| Unitário (Api) | `Api/Accounts/AccountsResponsesTests` (com `ResultRunner`) | corpo exato de cada resposta; ordem das chaves; `limite_credito` 0 ⇒ `null`; `data: []`; mensagens do `AccountsMessages` |
| Integração (Docker) | `Accounts/AccountRepositoryTests` | AC-002-03, 04, 21 a 23, 41, 44, 70 a 75: SQL real, desvios 007 e 016, constraint `contas_usuario_id_nome_conta_key`, `chk_cartao_credito` (23514 propaga), `FOR UPDATE` (AC-002-75) |
| Integração | `Accounts/AccountsEndpointsTests` | AC-002-01 a 06, 10 a 27, 40 a 56, 70 a 74, 80 a 86 com corpo exato; primeira prova do `RequireUser` numa rota real (AC-002-80, as 4 mensagens 401 em cada uma das 8 operações) |
| Integração | `Accounts/AccountsTenantIsolationTests` | AC-002-04, 22, 41, 53, 72: usuários A e B com contas, nomes iguais e tokens diferentes; B nunca lê nem altera nem remove conta de A; contas de A intactas no banco |
| Integração | `Accounts/AccountsRoutingTests` | AC-002-82 a 84, 86: alias, barra final, ids inválidos (404 HTML mesmo sem token), 405 |
| Contrato | dentro de `AccountsEndpointsTests` (corpo exato comparado como texto, como na 001) | spec §3; a captura contra o Flask em homol fica na Fase 3 (`testing-strategy.md` §3) |

Nomes dos testes: `Metodo_Cenario_Resultado`, com `[Trait("AC","002-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Fakes e fixtures

- **`PostgresFixture`** existente (PostgreSQL 15 com `0000_baseline.sql` + migrations); `ResetAsync()` (`TRUNCATE usuarios ... CASCADE`) entre testes.
- **Token:** `new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId)`; token inválido/expirado/sem `user_id` via `AuthFixtures.JwtToken(...)` (as fixtures do PyJWT da 001).
- **`AccountsTestData`** (helper só de teste, `tests/.../Accounts/`): `InsertUserAsync(whatsapp)` (`INSERT INTO usuarios (nome, numero_whatsapp) ...`, o resto usa defaults), `InsertAccountAsync(userId, nome, tipo, saldoInicial, ativa, dias...)`, `InsertTransactionAsync(userId, accountId, valor, consolidada)`. Esta última precisa de `grupocategoria` → `macrocategoria` → `subcategoria` mínimos (NOT NULL e FKs RESTRICT em `transacoes.subcategoria_id`); o helper cria uma vez por teste. `tipo_transacao` válido (`Renda`/`Despesa`).
- **Corrida (AC-002-23, 75):** duas conexões abertas no teste; para o AC-002-75, uma transação SQL fica aberta inserindo em `transacoes`, o `DELETE` HTTP roda numa `Task` e o teste espera (poll em `pg_stat_activity`, `wait_event_type = 'Lock'`) o `DELETE` bloquear antes de confirmar a transação; sem `Sleep` fixo.
- **Dados com ordenação:** nomes e tipos em ASCII nos testes de ordem (a collation do container pode diferir da de produção).
- Unitários: `FakeTimeProvider` não é necessário (a feature não usa relógio no código; `created_at`/`updated_at` vêm do banco); repositório falso com NSubstitute.

## 7. Riscos e decisões

- **Contrato do front quebrado (spec §8):** o Angular não consegue criar conta, edita zerando os dias e não agrupa as contas. O .NET reproduz o Flask; a correção está no front ou em P-002-7. O AC-002-54 documenta o PUT do Angular para ninguém "consertar" isso por engano.
- **Ordem das validações do PUT** intercala banco e regras (404 entre corpo e campos; duplicidade entre nome e tipo). Por isso o Domain expõe passos e o caso de uso os encadeia; o teste de ordem (AC-002-50) é obrigatório.
- **Distinguir ausente de `null`** (`cor_hex`/`icone` no POST, todos os campos do PUT) exige `RawValue`; `JsonBody.GetText/GetRaw` não distingue e não deve ser usado aqui.
- **500 reproduzidos de propósito** (tipo errado em nome/tipo, CHECK de dias, texto longo): ficam como `throw`/propagação ⇒ mensagem fixa. Se P-002-2, 3 ou 4 forem aprovados, viram 400 no Domain sem mudar a estrutura (a validação sobe do banco para `AccountRules`).
- **`double` ⇒ `decimal`:** `PyNumber` converte pela representação `"R"` para reproduzir o arredondamento do `numeric(15,2)` que o Flask obtinha ao enviar o `float` ao driver (ex.: `10.005` ⇒ `10.01`); teste dedicado.
- **Constraint da UNIQUE:** o nome `contas_usuario_id_nome_conta_key` é o padrão do PostgreSQL para a UNIQUE sem nome do baseline; se a produção tiver outro nome (tabela criada à mão), a corrida viraria 500. Mitigação: o repositório já não compara o nome inteiro (ver §3: tabela `contas` e `ConstraintName` contendo `nome_conta`), e o teste valida contra o baseline; confirmar o nome real no `schema-prod.sql` da produção.
- **Ordenação do GET** depende da collation do banco (Flask idem); sem `COLLATE` explícito para não divergir.
- **Saldo (desvio 007):** a consulta de saldo é a única definição do sistema; a 006 deve chamar `IAccountRepository.ListActiveAsync` (ou extrair o mesmo SQL) em vez de reescrevê-la. Avisar a 006 quando esta feature entrar.
- **Sem índice em `transacoes.conta_id`:** ver §3 (sugestão opcional).
- **Cobertura:** Domain ≥ 90% e Application ≥ 80% (`testing-strategy.md`); as regras de `AccountRules` e `PyNumber` têm tabelas de casos para atingir.
