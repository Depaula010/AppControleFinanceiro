# Plan 004 — Transações

- **Spec:** `spec.md` (em revisão; P-004-1 a P-004-7 aguardam decisão, ver §7 deste plano para o que muda em cada caso)

Reaproveita o que 001 a 003 entregaram: `Envelopes.A`, `RequireUser()`/`ICurrentUser`, `IDbConnectionFactory`, `SafeError`, `IClock`, `ICategoryLookup` (003), `PostgresFixture`/`AuthFixtures`, e da 002 o `RawValue`/`RawBody`/`PyNumber` (namespace `MeuSecretario.Domain.Accounts`), o `AccountsBodyReader` (público, em `Api/Accounts`) e o `NumericIdFilter`/`FlaskNotFoundResult` (`internal` no mesmo assembly `Api`). Mover esses tipos para um namespace comum é refatoração de arquivo compartilhado e fica **fora** desta feature (PR próprio, depois da onda). **Nenhum arquivo compartilhado é editado** (`Program.cs`, `DependencyInjection.cs`, `Directory.*.props`, `PostgresFixture`). Sem EF (Dapper), sem pacote novo. Uma migration: `dotnet/migrations/0050_transacoes_observacoes_origem.sql`.

## 1. Domain (`MeuSecretario.Domain/Transactions`)

Funções puras, sem I/O, todas com teste unitário (meta ≥ 90%).

| Tipo | Papel |
|---|---|
| `TransactionTypes` | constantes do banco (`Income = "Renda"`, `Expense = "Despesa"`, `Transfer = "Transferência"`, `InvoicePayment = "Pagamento Fatura"`); `ToApi(string db)` (`Renda` ⇒ `Receita`, o resto igual); `FromApi(string)` ⇒ `TransactionKind?` (só `Receita`/`Despesa` exatos); `IsEditable(db)` (usado só se P-004-4) |
| `SignedAmount` | `Apply(TransactionKind kind, decimal amount)` (R1); `ForUpdate(currentDbType, bodyType?, amount)` (RN-004-13: positivo só para `Renda`/`Receita`) |
| `MonthDays` | `Clamp(year, month, day)` (dia inexistente ⇒ último dia), `AddMonths(DateOnly, n)` (clamp como `relativedelta`; `DateOnly.AddMonths` já faz) |
| `InvoiceCycle` | `ForPurchase(int closingDay, int dueDay, DateOnly date)` ⇒ `(DateOnly Closing, DateOnly Due)` (RN-004-10, tabela AC-004-90). Duas implementações atrás do mesmo nome, escolhidas pela decisão do P-004-1: **com** aprovação, só a fórmula corrigida (`D > F` vence no mês do fechamento se `V ≥ F`); **sem**, a literal do Flask mais `CurrentFor(F, V, today)` (fórmula do `ensure`, AC-004-96). A decisão é tomada antes da T3 |
| `ListFilter` + `ListQueryParser.Parse(StringValues limit, offset, tipo, from, to)` | `limit`/`offset` por `BigInteger.TryParse` com `trim` e sinal (texto inválido ⇒ padrão; número fora de `int` ⇒ erro de faixa de `limit`, `offset` positivo gigante ⇒ valor máximo), validação na ordem da §3.1, `tipo` ⇒ `TransactionKind?`, datas por `FlaskDate.TryParse` ⇒ `ListQueryResult.Ok(ListFilter)` ou `Error(ListQueryError)` |
| `FlaskDate` | `TryParse(string, out DateOnly)` = `strptime('%Y-%m-%d')` (RN-004-06): regex `^\d{4}-\d{1,2}-\d{1,2}$` e data de calendário válida; cultura invariante |
| `CreateTransactionRules.Validate(RawBody?)` | linhas 1 a 9 da §3.2, **sem** as de banco (10 e 11); corpo `null` ⇒ trata como vazio; devolve `NewTransactionData(Description, Amount, Kind, DateText, Date, SubcategoryId, AccountId, Notes, Consolidated)` ou `TransactionError`; guarda `DateText` (eco na resposta) e `Amount` como `double` + `decimal` (`PyNumber`) para o eco sem arredondar |
| `UpdateTransactionRules` | passos, porque a ordem intercala banco: `ReadHead(RawBody, currentDbType)` (descricao, valor+tipo, tipo, data ⇒ `HeadChanges` ou `Error`), `ReadReferences(RawBody)` (devolve `IdRef` de `subcategoria_id` e `conta_id` quando presentes, para o caso de uso verificar no banco **nessa ordem**), `ReadTail(RawBody)` (observacoes, consolidada), `Combine` ⇒ `TransactionChanges` (`Change<T>`, `IsEmpty` ⇒ `NoValidField`) |
| `IdRef` | id vindo do JSON: inteiro, texto inteiro (`"5"`), falso/`null`/fracionário/booleano ⇒ `IdRef.Invalid` (equivale a "não existe": 404, e 400 de obrigatório só no POST quando falso) |
| `TransactionError` | enum com um membro por mensagem da spec (`DescriptionRequired`, `ValueRequired`, `ValueInvalid`, `ValueNegative`, `TypeInvalid`, `DateRequired`, `DateFormat`, `SubcategoryRequired`, `AccountRequired`, `NoDataProvided`, `NoValidField`, e os de P-004-2 se aprovado) |
| `PostingDescriptions` | `TransferOut(nameTo)`, `TransferIn(nameFrom)`, `PaymentOut(cardName)`, `PaymentIn(fromName)` (textos literais da §7.3) |
| `InstallmentSchedule` | `For(DateOnly purchase, decimal amount, int total)` ⇒ `(StartDate = AddMonths(1), DayOfMonth, InstallmentsExecuted = 1, Total)` |

## 2. Application (`MeuSecretario.Application/Transactions`)

**Portas públicas (consumidas por 011/012/013):** `ITransactionPosting`, `IInvoiceService`, `IInvoiceSchedulePopulator` e os tipos de comando/resultado exatamente como na spec §7.1 (arquivo `TransactionPostingContracts.cs`).

**Portas internas:**
```csharp
public interface ITransactionRepository   // leituras e escritas das 4 rotas, cada método com sua transação
{
    Task<TransactionPage> ListAsync(int userId, ListFilter filter, CancellationToken ct);              // COUNT + página (mesma conexão)
    Task<CurrentTransaction?> GetAsync(int userId, int id, CancellationToken ct);                     // id, tipo_transacao, valor, conta_id (null = 404)
    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken ct);
    Task<bool> ActiveAccountOwnedAsync(int userId, int accountId, CancellationToken ct);
    Task<UpdatedTransaction?> UpdateAsync(int userId, int id, TransactionChanges changes, CancellationToken ct);  // RETURNING; null = sumiu (404)
    Task<bool> DeleteWithPairAsync(int userId, int id, CancellationToken ct);                          // false = 404
}

public interface ITransactionsUnitOfWork   // usado pelos serviços de lançamento (uma transação por Post*)
{
    Task<ITransactionsSession> BeginAsync(CancellationToken ct);
}

public interface ITransactionsSession : IAsyncDisposable
{
    Task<PostingAccount?> FindActiveAccountAsync(int userId, int accountId, CancellationToken ct);     // id, nome, tipo, dia_fechamento, dia_vencimento
    Task<bool> SubcategoryVisibleAsync(int userId, int subcategoryId, CancellationToken ct);
    Task<int?> FindInvoiceByDueDateAsync(int cardAccountId, DateOnly due, CancellationToken ct);
    Task<int?> InsertInvoiceIfMissingAsync(int cardAccountId, DateOnly due, DateOnly closing, CancellationToken ct);   // ON CONFLICT DO NOTHING RETURNING id
    Task<OpenInvoice?> FindOldestClosedInvoiceAsync(int cardAccountId, CancellationToken ct);
    Task<int> InsertTransactionAsync(NewTransactionRow row, CancellationToken ct);
    Task LinkPairAsync(int firstId, int secondId, CancellationToken ct);                                // as duas direções
    Task MarkInvoicePaidAsync(int invoiceId, CancellationToken ct);
    Task<int> InsertInstallmentScheduleAsync(NewInstallmentSchedule schedule, CancellationToken ct);
    Task CommitAsync(CancellationToken ct);                                                             // sem Commit ⇒ rollback ao descartar
    // EnsureCurrent:
    Task<IReadOnlyList<PostingAccount>> ListActiveCardsAsync(int userId, int? cardId, CancellationToken ct);
    Task<bool> OpenInvoiceExistsAsync(int cardAccountId, DateOnly due, CancellationToken ct);           // status = 'Aberta'
}
```
Os tipos de linha (`NewTransactionRow`, `NewInstallmentSchedule`, `PostingAccount`, `OpenInvoice`) são records da camada.

**Casos de uso** (uma classe cada, `ExecuteAsync`, resultado tipado sem HTTP, `userId` vem do endpoint):
- `ListTransactions(ListFilter)`: delega a `ITransactionRepository.ListAsync`.
- `CreateTransaction(RawBody?)`: `CreateTransactionRules.Validate` ⇒ erro `TransactionError`; monta `PostSimpleTransaction` (origem `"api"`, `Notes`, `Consolidated`) e chama `ITransactionPosting.PostSimpleAsync`; mapeia `Failed(AccountNotFound|SubcategoryNotFound)` para o resultado do endpoint. O eco da resposta (`valor` sem arredondar, `data` em texto, `tipo` da entrada) vem do `NewTransactionData`, não do serviço.
- `UpdateTransaction(id, RawBody?)` (ordem **é** a §3.3): corpo vazio ⇒ `NoDataProvided`; `GetAsync` ⇒ `NotFound`; `ReadHead(body, current.Type)`; `ReadReferences` e, na ordem, `SubcategoryVisibleAsync` ⇒ `SubcategoryNotFound`, `ActiveAccountOwnedAsync` ⇒ `AccountNotFound`; `ReadTail`; `IsEmpty` ⇒ `NoValidField`; (P-004-4: tipo atual `Transferência`/`Pagamento Fatura` ⇒ `NotEditable` logo depois do 404; P-004-5: recalcular `fatura_id` via `ITransactionsSession`); `UpdateAsync`.
- `DeleteTransaction(id)`: `DeleteWithPairAsync` (P-004-6: no mesmo método, reabrir fatura).
- `TransactionPosting` (implementa `ITransactionPosting`): orquestra as regras sobre `ITransactionsSession`: valida, resolve contas e subcategoria, calcula fatura com `InvoiceCycle`, insere e `CommitAsync`. Os 4 métodos compartilham `ResolveInvoiceAsync(session, account, date)` (RN-004-09/11: busca, insere, busca de novo).
- `InvoiceService` (implementa `IInvoiceService`): `EnsureCurrentAsync` (§7.6) sobre `ITransactionsSession` e `IClock`; chama `IInvoiceSchedulePopulator` depois do `CommitAsync`, engolindo exceção (`ILogger` só com `SafeError.Describe`).
- `NullInvoiceSchedulePopulator : IInvoiceSchedulePopulator` (vazio), registrado com `TryAddScoped` pelo módulo.

Textos em português ficam no endpoint (`TransactionsMessages`), como na 001 e 002.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Transactions`)

`TransactionRepository(IDbConnectionFactory)` e `TransactionsUnitOfWork(NpgsqlDataSource)`/`TransactionsSession` com **Dapper**. Colunas minúsculas, sem aspas, nenhum texto do cliente concatenado (filtros por fragmentos fixos escolhidos por `if`/`switch`). Datas como `DateOnly` com `DbType.Date` explícito.

**Listar** (dois comandos na mesma conexão):
```sql
SELECT COUNT(*) FROM transacoes t WHERE t.usuario_id = @userId
  /* AND t.tipo_transacao = @tipo */ /* AND t.data_transacao >= @from */ /* AND t.data_transacao <= @to */;

SELECT t.id, t.descricao, t.valor, t.tipo_transacao, t.data_transacao,
       m.nome_macro AS categoria, s.nome_sub AS subcategoria, c.nome_conta, t.consolidada,
       t.conta_id, t.subcategoria_id, t.observacoes          -- as três últimas só se P-004-3
FROM transacoes t
JOIN subcategoria s ON t.subcategoria_id = s.id
JOIN macrocategoria m ON s.macro_id = m.id
JOIN contas c ON t.conta_id = c.id
WHERE t.usuario_id = @userId /* mesmos fragmentos */
ORDER BY t.data_transacao DESC, t.created_at DESC, t.id DESC
LIMIT @limit OFFSET @offset;
```
`@tipo` é `'Renda'` ou `'Despesa'` (mapeado no Domain). `limit` e `offset` como `bigint`. Os fragmentos de PUT/POST a seguir idem.

**Obter para o PUT:** `SELECT id, tipo_transacao, valor, conta_id FROM transacoes WHERE id = @id AND usuario_id = @userId`.
**Subcategoria visível:** `SELECT EXISTS (SELECT 1 FROM subcategoria WHERE id = @id AND (usuario_id IS NULL OR usuario_id = @userId))`.
**Conta ativa do usuário:** `SELECT id, nome_conta, tipo_conta, dia_fechamento, dia_vencimento FROM contas WHERE id = @id AND usuario_id = @userId AND ativa = true`.

**Atualizar** (uma transação; fragmentos fixos dos campos `IsSet`, sempre com `updated_at = CURRENT_TIMESTAMP`):
```sql
UPDATE transacoes SET descricao = @descricao, valor = @valor, tipo_transacao = @tipo, data_transacao = @data,
       subcategoria_id = @subcategoriaId, conta_id = @contaId, observacoes = @observacoes, consolidada = @consolidada,
       updated_at = CURRENT_TIMESTAMP          -- só os presentes
WHERE id = @id AND usuario_id = @userId
RETURNING id, descricao, valor, tipo_transacao, data_transacao;
```
Zero linhas ⇒ `null` (a transação sumiu entre o `GetAsync` e o `UPDATE`: 404).

**Apagar com par** (uma transação):
```sql
SELECT id, transferencia_par_id FROM transacoes WHERE id = @id AND usuario_id = @userId FOR UPDATE;
DELETE FROM transacoes WHERE id = @par AND usuario_id = @userId;   -- só se par não nulo
DELETE FROM transacoes WHERE id = @id  AND usuario_id = @userId;
```
(`transferencia_par_id` tem `ON DELETE SET NULL`: apagar o par antes não quebra a FK.) Sem linha no primeiro `SELECT` ⇒ `false`.

**Sessão de lançamento** (uma conexão e uma transação Npgsql; `DisposeAsync` sem `CommitAsync` faz rollback):
```sql
-- FindInvoiceByDueDate
SELECT id FROM faturas WHERE conta_id = @cardId AND data_vencimento = @due;
-- InsertInvoiceIfMissing
INSERT INTO faturas (conta_id, data_vencimento, data_fechamento, status)
VALUES (@cardId, @due, @closing, 'Aberta')
ON CONFLICT (conta_id, data_vencimento) DO NOTHING RETURNING id;
-- FindOldestClosedInvoice (desvio 010)
SELECT id, data_vencimento FROM faturas WHERE conta_id = @cardId AND status = 'Fechada'
ORDER BY data_vencimento, id LIMIT 1;
-- InsertTransaction
INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, descricao, valor, tipo_transacao,
                        data_transacao, observacoes, consolidada, origem, agendamento_id)
VALUES (@userId, @contaId, @subcategoriaId, @faturaId, @descricao, @valor, @tipo, @data, @observacoes, @consolidada, @origem, @agendamentoId)
RETURNING id, valor;
-- LinkPair (duas vezes, em ambos os sentidos)
UPDATE transacoes SET transferencia_par_id = @par WHERE id = @alvo AND usuario_id = @userId;
-- MarkInvoicePaid
UPDATE faturas SET status = 'Paga' WHERE id = @id;
-- InsertInstallmentSchedule
INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento, periodicidade,
                          data_inicio, dia_execucao, total_parcelas, parcelas_executadas, ativo)
VALUES (@userId, @contaId, @subcategoriaId, @descricao, @valor, 'PARCELADO', 'MENSAL', @dataInicio, @dia, @total, 1, TRUE)
RETURNING id;
-- ListActiveCards (EnsureCurrent)
SELECT id, nome_conta, tipo_conta, dia_fechamento, dia_vencimento FROM contas
WHERE usuario_id = @userId AND tipo_conta = 'Cartão de Crédito' AND ativa = true /* AND id = @cardId */;
-- OpenInvoiceExists
SELECT EXISTS (SELECT 1 FROM faturas WHERE conta_id = @cardId AND data_vencimento = @due AND status = 'Aberta');
```
`InsertTransaction` devolve `valor` para `StoredAmount`. As subcategorias fixas vêm de `ICategoryLookup` (já em Infrastructure/Categories, registrada pela 003).

**Migration:** `dotnet/migrations/0050_transacoes_observacoes_origem.sql` (spec §6.1). Embutida no assembly como as demais (`MigrationRunner`); teste que aplica o banco, roda de novo (idempotência) e confere as duas colunas e que as 330-linhas-simuladas (inserts sem as colunas) continuam válidas. **Sugestão fora do escopo:** índice `(usuario_id, data_transacao DESC)` (`0051`, aditivo) só se o volume crescer.

`AddTransactionsInfrastructure` fica no próprio `TransactionsModule` (registro de `TransactionRepository`, `TransactionsUnitOfWork`); o `NpgsqlDataSource` já é singleton da 001a.

## 4. Api (`MeuSecretario.Api/Transactions`)

- `TransactionsModule : IFeatureModule`. `AddServices`: `ITransactionRepository`, `ITransactionsUnitOfWork`, os casos de uso (`ListTransactions`, `CreateTransaction`, `UpdateTransaction`, `DeleteTransaction`), `ITransactionPosting` ⇒ `TransactionPosting`, `IInvoiceService` ⇒ `InvoiceService`, `TryAddScoped<IInvoiceSchedulePopulator, NullInvoiceSchedulePopulator>()`. `MapEndpoints`: `MapGet`/`MapPost("/api/transactions")` com `.RequireUser()`; `MapPut`/`MapDelete("/api/transactions/{id}")` com `NumericIdFilter` antes do `RequireUser` (404 HTML antes da autenticação, como na 002); `app.Map("/api/transactions/{id}", OtherMethods)`: id numérico ⇒ 405; `recent` ⇒ **405** (a 006 mapeia o GET; literal vence o parâmetro); outro texto ⇒ 404 HTML. Id maior que `int.MaxValue` vira 0 (404 JSON, RN-004-18).
- `GET` lê `HttpRequest.Query` (primeiro valor de cada chave) e chama `ListQueryParser`.
- Corpo: `AccountsBodyReader.ReadAsync` (⇒ `RawBody?`; ausente/inválido/não-objeto ⇒ `null`).
- `TransactionsResponses` (`Envelopes.A`): GET, POST (201, `message`, `data` com `id, descricao, valor, tipo, data`), PUT (200), DELETE (200 sem `data`) e erros; chaves na ordem da spec; `valor` como `double`; `tipo` mapeado por `TransactionTypes.ToApi`.
- `TransactionsMessages` (todas as mensagens da spec, copiadas literalmente) e `TransactionsLog` (`LoggerMessage`, EventIds **4001–4004** para as rotas e **4101–4103** para o serviço de lançamento e o ensure; só `SafeError.Describe`, `userId` e ids).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com a mensagem fixa (desvio 008).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `TransactionTypesTests`, `SignedAmountTests`, `FlaskDateTests`, `InvoiceCycleTests` (a tabela de 12 linhas, as duas colunas), `ListQueryParserTests`, `CreateTransactionRulesTests`, `UpdateTransactionRulesTests`, `InstallmentScheduleTests`, `PostingDescriptionsTests` | AC-004-02 a 06, 12 a 19, 21, 22, 40 a 52, 90 a 94, 134 |
| Unitário (Application, NSubstitute) | `ListTransactionsTests`, `CreateTransactionTests`, `UpdateTransactionTests`, `DeleteTransactionTests`, `TransactionPostingTests` (sessão falsa), `InvoiceServiceTests` (`FakeTimeProvider`/`IClock` falso) | ordem das validações; repositório não chamado em erro de corpo; conta antes de subcategoria; fatura criada só depois das checagens; `Commit` só no sucesso; populador depois do commit e erro engolido (AC-004-13, 20, 25, 49, 53, 95, 110 a 134, 140, 141) |
| Unitário (Api) | `TransactionsResponsesTests`, `TransactionsMessagesTests` (com `ResultRunner`) | corpo exato, ordem das chaves, eco de `valor` e `data`, `tipo` mapeado, 500 fixo (AC-004-01, 10, 16, 18, 83) |
| Integração | `TransactionRepositoryTests` | SQL real: filtros, ordem, paginação, `Renda`↔`Receita`, `FOR UPDATE` + par, rollback do par (AC-004-01 a 09, 70 a 77) |
| Integração | `TransactionPostingTests` | os 4 serviços no banco: sinal, tipos, pares ligados, fatura criada/reutilizada, corrida de duas compras (`Task.WhenAll`), desvio 010 (duas `Fechada`), rollback quando falha no meio (injetando falha por `CHECK`/subcategoria removida), agendamento da parcela, tenant (AC-004-23 a 26, 110 a 134) |
| Integração | `TransactionsEndpointsTests` (por rota: `TransactionsGetEndpointsTests`, `…Post…`, `…Put…`, `…Delete…`) | cada linha da §3 com corpo como texto exato; `RequireUser` real (AC-004-80); corpo do Angular (AC-004-29, 54) |
| Integração | `TransactionsRoutingTests`, `TransactionsTenantIsolationTests` | alias inexistente, barra final, `abc`/`-1`/`1.5` ⇒ 404 HTML sem token, 405, `/recent` ⇒ 405; isolamento A × B nas 4 rotas (AC-004-81 a 85) |
| Integração | `Migration0050Tests` | idempotência e colunas (AC-004-10, 21) |
| Contrato (Fase 3) | goldens do GET e dos erros do Flask de homologação | o que o Flask consegue responder (§1 Achado); sucesso de POST/PUT/DELETE só contra a spec |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","004-NN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`.

## 6. Fakes e fixtures

- `PostgresFixture` (migrations aplicadas até a `0050`; `ResetAsync` já reaplica o seed de categorias); `FakeTimeProvider`/`IClock` falso nas regras de `EnsureCurrent`.
- Token: `JwtTokenService` e `AuthFixtures` como na 002.
- `TransactionsTestData` (só teste): `InsertUserAsync`, `InsertAccountAsync` (reaproveitar o helper da 002 por cópia, sem editar o dele), `InsertCardAsync(fechamento, vencimento)`, `InsertInvoiceAsync(status)`, `InsertTransactionAsync(tipo, valor, data, fatura?)`, `FindGlobalSubcategoryAsync`.
- Fixture Python do ciclo da fatura: `tests/Fixtures/generate_invoice_cycle_fixtures.py` importa `get_or_create_fatura` (mockando `conn`) e gera `invoice-cycle-fixtures.json` com ~200 combinações de `F`, `V`, `D` (inclui 31/02, bissexto, virada de ano) para provar a fórmula **literal**; os casos corrigidos do P-004-1 vêm da tabela da spec.
- Falha injetada no meio do serviço: `ITransactionsSession` decorador de teste que lança no N-ésimo comando (Application), e no banco um trigger de teste ou subcategoria removida por conexão paralela (Integração).

## 7. Riscos e decisões

- **O Flask não serve de referência para o sucesso do POST/PUT/DELETE** (§1 Achado): o contrato de sucesso vem do código e do Angular. Pedir ao Rafael a confirmação Q-004-1 antes da homologação.
- **P-004-1 muda o Domain.** Sem a decisão, implementar as duas fórmulas literais com testes (a fixture do Python as prova); com ela, só `ForPurchase` corrigida (o teste literal vira regressão da diferença). A decisão precisa vir antes da T3.
- **P-004-4/5/6/7 mudam use cases, não o contrato de sucesso.** Cada um tem task isolada e marcada (T18 a T23) que só entra se aprovada; as demais não dependem delas.
- **Unidade de trabalho por método.** `ITransactionsSession` expõe só os comandos necessários; a regra fica no Application (testável com sessão falsa) e o SQL na Infrastructure, como pede `architecture.md`.
- **Eco do POST** usa o `double` da entrada e o texto da data, não o valor do banco (paridade, AC-004-16 e 18); o do PUT e do GET, o do banco.
- **Dapper e `DateOnly`:** passar parâmetros de data com `DbType.Date`/`NpgsqlDbType.Date` e ler `date` como `DateOnly` (verificar o registro do handler na T5; se faltar, usar `DateTime` + `DateOnly.FromDateTime` no repositório, sem arquivo compartilhado).
- **Reutilização cruzada de tipos da 002** (`RawBody`, `PyNumber`, `AccountsBodyReader`, `NumericIdFilter`): dependência intencional e temporária; o PR de arrumação posterior move para `Common`/`Http` sem mudar comportamento.
- **`/api/transactions/recent` (006):** se a 006 chegar depois, o `OtherMethods` já responde 405 a `PUT|DELETE|POST`; se a 006 mapear `MapGet("/api/transactions/recent")` antes, a precedência do literal funciona sem mudança aqui.
- **Sem índice** em `(usuario_id, data_transacao)`: 330 linhas hoje.
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
