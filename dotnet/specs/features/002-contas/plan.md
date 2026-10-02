# Plan 002 — Contas

- **Spec:** `spec.md` (aprovada em 2026-10-02, com os desvios 032 a 038; 039 só documentado; 049 rejeitado)

Reaproveita o que a 001 entregou: `Envelopes.A`, `RequireUser()`/`ICurrentUser`, `IDbConnectionFactory`, `SafeError`, o padrão de log com `LoggerMessage` (`AuthLog`), `PostgresFixture` e `AuthFixtures`. **Nenhum arquivo compartilhado é editado** (`Program.cs`, `DependencyInjection.cs`, `Directory.*.props`, `JsonBody.cs`): tudo vive em pastas `Accounts/` de cada camada e o `AccountsModule` registra repositório e casos de uso no próprio `AddServices`. Sem EF (Dapper, como `UserRepository`). Sem pacote novo.

## 1. Domain (`MeuSecretario.Domain/Accounts`)

Funções puras, sem I/O, todas com teste unitário (meta Domain ≥ 90%).

| Tipo | Papel |
|---|---|
| `RawValue` (`Absent`, `Null`, `Text`, `Integer(BigInteger)`, `Decimal(double)`, `Bool`, `Other`) e `RawBody` (`Get(name)` devolve `Absent`, `Has`, `IsEmpty`) | valor JSON bruto, para reproduzir `float()`/`int()` e distinguir ausente de `null` sem referenciar `System.Text.Json` |
| `PyNumber` | `TryMoney(RawValue, out decimal)` = `float()` do Python + finito + cabe em `numeric(15,2)` (módulo < 10^13 depois de arredondar a 2 casas; desvio 033); `TryInt(RawValue, out int)` (faixa 1..31 fica no chamador). `float()` aceita número, `Bool`, texto com espaços/sinal/expoente (cultura invariante); `int()` trunca decimal, aceita texto inteiro e `Bool`. `decimal` vem da representação mais curta `"R"` do `double` (arredondamento do `numeric` como no Flask: `10.005` ⇒ `10.01`) |
| `AccountTypes` | `All` (ordem da mensagem), `IsValid`, `Card` |
| `AccountName` | `Normalize` (`trim` + corte em 100 pontos de código) e `Truncate` |
| `AccountError` | enum: `DataMissing`, `NameRequired`, `TypeRequired`, `TypeInvalid`, `BalanceInvalid`, `ColorInvalid`, `IconInvalid`, `LimitInvalid`, `DaysOnlyForCard`, `ClosingDayInvalid`, `DueDayInvalid`, `CardNeedsDays`, `NameEmpty`, `NothingToUpdate` |
| `CreateRules.Validate(RawBody)` | linhas 1 a 12 da spec §3.2 ⇒ `CreateValidation.Ok(NewAccountData)` ou `Error(AccountError)`; defaults só com chave **ausente**; `null` explícito segue `null`. A linha 13 (nome ativo em uso) é do banco |
| `UpdateRules` | `ReadName(RawBody)` ⇒ `NameStep` (`NotPresent`, `Value`, `Error`); `ReadFields(RawBody)` ⇒ `FieldsResult` (`Ok(AccountChanges)` ou `Error`), na ordem tipo, saldo, cor, ícone, limite, fechamento, vencimento; `CheckConsistency(current, changes)` ⇒ `AccountError?` (linhas 13 e 14 da §3.3: dias fora de cartão e cartão sem dias, só quando o corpo toca tipo ou dias) |
| `NewAccountData`, `AccountChanges` com `Change<T>` (`IsSet`, `Value`) | `AccountChanges.IsEmpty` ⇒ `NothingToUpdate` (depois de juntar o nome) |

## 2. Application (`MeuSecretario.Application/Accounts`)

**Porta** `IAccountRepository`:
```
Task<IReadOnlyList<AccountListItem>> ListActiveAsync(int userId, CancellationToken ct);
Task<AccountDetails?> GetActiveAsync(int userId, int accountId, CancellationToken ct);       // null = 404
Task<bool> NameTakenAsync(int userId, string name, int exceptAccountId, CancellationToken ct); // todas as contas (016), exceto a própria
Task<CreateAccountResult> CreateAsync(int userId, NewAccountData data, CancellationToken ct);   // Created(details) | NameTaken (ativa em uso); reativa a inativa (032)
Task<UpdateAccountResult> UpdateAsync(int userId, int accountId, AccountChanges c, CancellationToken ct); // Updated | NotFound | NameTaken (23505)
Task<DeleteAccountResult> SoftDeleteAsync(int userId, int accountId, CancellationToken ct);  // Deleted | NotFound | HasTransactions(n) | HasActiveSchedules(n)
```
`AccountDetails(Id, NomeConta, TipoConta, SaldoInicial, CorHex, Icone, LimiteCredito, DiaFechamento, DiaVencimento)`; `AccountListItem` = `AccountDetails` + `Saldo` (o GET leva os campos extras do desvio 038).

**Casos de uso** (uma classe cada, `ExecuteAsync`, resultado tipado sem HTTP; o `userId` vem do endpoint):
- `ListAccounts`.
- `CreateAccount(RawBody?)`: corpo nulo/vazio ⇒ `DataMissing`; `CreateRules.Validate`; `CreateAsync`.
- `UpdateAccount(accountId, RawBody?)` (a ordem **é** a §3.3): corpo vazio ⇒ `DataMissing`; `GetActiveAsync` ⇒ `NotFound`; `ReadName` e, se veio, `NameTakenAsync(userId, nome, accountId)`; `ReadFields`; `NothingToUpdate`; `CheckConsistency`; `UpdateAsync`.
- `DeleteAccount(accountId)`: delega ao repositório (uma transação).

Os textos em português ficam no endpoint (`AccountsMessages`), como na 001.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Accounts`)

`AccountRepository(IDbConnectionFactory)` com **Dapper**, registrado pelo `AccountsModule` (`AddScoped<IAccountRepository, AccountRepository>()`). Colunas minúsculas, sem aspas.

- **Listar** (desvio 007: `AND t.consolidada = true`; ordem sem `COLLATE`): `SELECT c.id, c.nome_conta, c.tipo_conta, c.saldo_inicial, c.cor_hex, c.icone, c.limite_credito, c.dia_fechamento, c.dia_vencimento, c.saldo_inicial + COALESCE(SUM(t.valor), 0) AS saldo FROM contas c LEFT JOIN transacoes t ON t.conta_id = c.id AND t.consolidada = true WHERE c.usuario_id = @userId AND c.ativa = true GROUP BY c.id ORDER BY c.tipo_conta, c.nome_conta`.
- **GetActive**: `SELECT <detalhes> FROM contas WHERE id AND usuario_id AND ativa = true`.
- **NameTaken** (sem `ativa`): `SELECT EXISTS (SELECT 1 FROM contas WHERE usuario_id = @userId AND nome_conta = @name AND id <> @exceptId)`.
- **Criar / reativar** (um comando, serializa a corrida):
```sql
INSERT INTO contas (usuario_id, nome_conta, tipo_conta, saldo_inicial, cor_hex, icone, limite_credito,
                    dia_fechamento, dia_vencimento, ativa, inclui_saldo_total, created_at)
VALUES (...,true, true, CURRENT_TIMESTAMP)
ON CONFLICT (usuario_id, nome_conta) DO UPDATE SET tipo_conta = EXCLUDED.tipo_conta, saldo_inicial = EXCLUDED.saldo_inicial,
    cor_hex = EXCLUDED.cor_hex, icone = EXCLUDED.icone, limite_credito = EXCLUDED.limite_credito,
    dia_fechamento = EXCLUDED.dia_fechamento, dia_vencimento = EXCLUDED.dia_vencimento, ativa = true, inclui_saldo_total = true
WHERE contas.ativa = false
RETURNING <detalhes>
```
  Nenhuma linha devolvida ⇒ `NameTaken` (a conta ativa já existe).
- **Atualizar**: `UPDATE contas SET <fragmentos fixos dos campos IsSet> WHERE id AND usuario_id AND ativa = true RETURNING <detalhes>`; zero linhas ⇒ `NotFound`; `23505` ⇒ `NameTaken`; demais (CHECK etc.) propagam ⇒ 500.
- **Remover** (uma transação; RN-002-15): `SELECT id ... FOR UPDATE`; `COUNT(*)` de `transacoes`; `COUNT(*)` de `agendamentos` com `ativo = true`; `UPDATE contas SET ativa = false`; `UPDATE usuarios SET conta_padrao_* = NULL` nos campos que apontam para a conta (do próprio usuário).
- O `limite_credito` zero vira `null` só na serialização.

**Migrations: nenhuma.** O índice em `transacoes.conta_id` continua sugestão fora do escopo (`0010_idx_transacoes_conta_id.sql`, aditiva), por PR próprio.

## 4. Api (`MeuSecretario.Api/Accounts`)

- `AccountsModule : IFeatureModule`. `AddServices`: repositório e os 4 casos de uso (`AddScoped`). `MapEndpoints`: para `["/api/accounts", "/api/contas"]`: `MapGet(base)`, `MapPost(base)`, `MapPut/MapDelete($"{base}/{{id:regex(^[0-9]+$)}}")`, todos `.RequireUser()`. Mais, para reproduzir o 404 HTML do Flask em `<int:id>` inválido (`abc`, `-1`, `1.5`) **antes** da autenticação e para qualquer método, uma rota `{base}/{id}` com restrição regex "não é só dígitos" ligada a `FlaskNotFound`. O handler recebe `id` como `string`; valor maior que `int.MaxValue` vira conta inexistente (404 JSON).
- `AccountsBodyReader.ReadAsync(HttpRequest, ct)` ⇒ `RawBody?` (`JsonDocument`, corpo ausente/inválido/não-objeto ⇒ `null`; não exige `Content-Type`).
- `AccountsResponses`: corpos com `Envelopes.A`; `data` na ordem da spec; `limite_credito` 0 ⇒ `null`; valores monetários como `double`.
- `AccountsMessages` (constantes da spec, copiadas literalmente) e `AccountsLog` (`LoggerMessage`, EventIds 2001–2004, só `SafeError.Describe`).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com a mensagem fixa (desvio 008).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `PyNumberTests`, `AccountNameTests`, `AccountTypesTests`, `CreateRulesTests`, `UpdateRulesTests` | AC-002-12 a 19, 24, 28, 29, 43 a 52, 54 a 56 (tabelas de entradas; ordem das validações; defaults só com chave ausente; `null` explícito) |
| Unitário (Application, NSubstitute) | `ListAccountsTests`, `CreateAccountTests`, `UpdateAccountTests`, `DeleteAccountTests` | ordem (404 antes dos campos; duplicidade entre nome e tipo), repositório não chamado com erro de validação, `NameTaken`/`NotFound` do repositório |
| Unitário (Api) | `AccountsBodyReaderTests`, `AccountsResponsesTests` (com `ResultRunner`) | corpo exato; ordem das chaves; `limite_credito` 0 ⇒ `null`; `data: []` |
| Integração | `AccountRepositoryTests` | SQL real: desvios 007, 016, 032, 037, UNIQUE, `chk_cartao_credito`, `FOR UPDATE` (AC-002-75), corrida (AC-002-23) |
| Integração | `AccountsEndpointsTests` | cada rota e mensagem, corpo como texto exato; `RequireUser` real (AC-002-80, 4 mensagens x 8 operações) |
| Integração | `AccountsTenantIsolationTests`, `AccountsRoutingTests` | isolamento A x B nas 8 operações; alias; barra final; ids inválidos; 405 |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","002-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Fakes e fixtures

- `PostgresFixture` existente; `ResetAsync()` já reaplica o seed de categorias.
- Token: `JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId)`; inválido/expirado/sem `user_id` via `AuthFixtures.JwtToken(...)`.
- `AccountsTestData` (só teste): `InsertUserAsync`, `InsertAccountAsync`, `InsertTransactionAsync` (categoria mínima), `InsertScheduleAsync`.
- Ordem do GET: nomes ASCII.

## 7. Riscos e decisões

- **Contrato do front quebrado (spec §8):** o backend não aceita aliases; o AC-002-54 documenta o PUT do Angular.
- **Ordem das validações do PUT** intercala banco e regras; por isso o Domain expõe passos e o caso de uso os encadeia.
- **Rota 404 HTML** para `id` inválido exige a rota-coringa regex do módulo (não há fallback global; não se edita `Program.cs`). Outros módulos que queiram o mesmo precisam de rota própria.
- **`limite_credito` do GET** sai `null` quando 0, por consistência com POST/PUT (049 rejeitado).
- **Saldo (desvio 007):** a 006 deve reutilizar `IAccountRepository.ListActiveAsync` (ou o mesmo SQL).
- **Sem índice em `transacoes.conta_id`:** irrelevante com o volume atual.
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
