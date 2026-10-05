# Plan 011 — Automação

- **Spec:** `spec.md` (aprovada por delegação em 2026-10-05; só paridade e desvios já aprovados)

Reaproveita o que já existe: `Envelopes.B`/`Envelopes.E`, `IApiKeyValidator` e `RequireApiKey` (008-0), `IClock`, `IEncryptionService`, `IDbConnectionFactory`, `SafeError`, `ICache` (010a), `IBotClient` (010b), `ILlmClient` e `LlmCachePolicies` (010c), `IIntegrationSettings`, `ICategoryLookup` (003), `ITransactionPosting`, `IInvoiceService`, `IInvoiceSchedulePopulator`, `ITransactionsUnitOfWork` e o `InvoiceLinking` (internal, mesmo assembly Application) da 004, `IEmergencyReserveService` (005), `RawValue`/`PyNumber` (002, só como referência de estilo; as regras de entrada desta feature leem `JsonElement`, porque precisam da verdade do Python, da lista vazia e do nome do tipo), `PyRound`, `PhoneMask`, `IDatabaseHealth`, `PostgresFixture`, `AuthFixtures`.

**Nenhum arquivo compartilhado é editado** (`Program.cs`, `DependencyInjection.cs`, `Directory.*.props`, `PostgresFixture`). Sem EF (Dapper), **sem pacote novo**: o `rapidfuzz` é portado em código puro (`FuzzyScorer`) e validado contra a biblioteca real por fixture gerada em Python (a lib escolhida em `tech-stack.md`, FuzzySharp, é o `fuzzywuzzy` e dá notas diferentes das do `rapidfuzz` 3.14.3 que a produção usa; trocar de biblioteca mudaria o casamento e exigiria PR de pacote à parte). Sem migration.

Ordem dos módulos: `MeuSecretario.Api.Automation.AutomationModule` vem antes de `...Transactions.TransactionsModule` (ordem por nome), então o `AddScoped<IInvoiceSchedulePopulator, InvoiceSchedulePopulator>` daqui vence o `TryAddScoped` da 004 (RN-011-14). Um teste de integração prova que `IInvoiceSchedulePopulator` resolve para a implementação real.

## 1. Domain (`MeuSecretario.Domain/Automation`)

Funções puras, sem I/O. Meta: Domain ≥ 90%.

| Tipo | Papel |
|---|---|
| `PyJsonValue` | sobre `JsonElement`: `IsTruthy` (verdade do Python), `TypeName` (`list`/`dict`), `TryFloat(element, out double, out string error)` (§7.1: bool, número, texto com `float()` do Python, `inf`/`nan`, mensagens exatas), `PyRepr(string)` |
| `InputSanitizer` | `Sanitize(string?, maxLength, allowSpecialChars)` (§7.2), por pontos de código |
| `PaymentType` | `Normalize(string)` (`trim`, minúsculas, NFKD, só ASCII) e `IsValid` (`credito`/`debito`/`pix`/`dinheiro`) |
| `MoneyText` | `Brl(double?)` (`R$ 1.234,56`, `R$ 0,00` para nulo, fallback do Flask para negativo), `Grouped2(double)` (`{:,.2f}` do Python, usa `PyRound`) |
| `PythonJson` | `DumpCategories(IEnumerable<CategoryOption>)` = `json.dumps(lista)` (`ensure_ascii`, `, ` e `: `) |
| `FuzzyScorer` | porte do `rapidfuzz` 3.14.3: `Ratio` (Indel normalizado), `PartialRatio`, `TokenSortRatio`, `TokenSetRatio`, `PartialTokenSortRatio`, `PartialTokenSetRatio`, `WRatio`, `ExtractOne(query, choices, cutoff)` |
| `BillMatching` | `CleanDescription(string)` (prefixos, §7.3) e `FindBest(descriptionClean, bills)` (dicionário de descrições com colapso na última) |
| `BudgetPeriods` | `For(string periodicity, DateOnly reference)` ⇒ `(Start, End)` (RN-011-11) |
| `BudgetValidation` | `Evaluate(pots, spendByPot, amount)` ⇒ `BudgetCheck(RequiresConfirmation, Message)` e as duas mensagens (§7.6) |
| `AutomationPrompts` | os três prompts (§7.4) como funções puras |
| `ConfirmationMessage` | `Build(PendingTransactionData, categories, id)` (§7.5) |
| `PendingTransactionData` | dicionário ordenado da pendência (Automate e `api_endpoint`) e a categoria mostrada |
| `AutomateChoice` | `ChooseAccount(texto, tipoFluxo, contas)` (palavras de cartão, `Conta Corrente`, primeira) e `ChooseCategoryGroup(tipo)` |
| `NotificationExtraction` | `Parse(string geminiText)` ⇒ `Extraction(Tipo, Descricao?, Valor)` ou erro (§3.1 passo 1) |
| `ApiTransactionInput` | `Parse(JsonElement body)` ⇒ `ApiTransactionData` ou `ApiTransactionError` (ordem das linhas 3 a 8 da §3.2) |
| `PyIsoDate` | `TryParse(JsonElement, out DateOnly)` (`YYYY-MM-DD` e `YYYYMMDD`, só texto) |
| `ReserveListQuery` | `Parse(query)` ⇒ filtros e `limit`/`offset` (`int()` do Python por `BigInteger`, `int64` do Postgres) ou erro |
| `ReserveToggleInput` | `Parse(JsonElement?)` ⇒ `(Include, ApiKey)` ou `MissingFields`/`InvalidType` |
| `InvoiceScheduleRules` | `TransactionDate(refDate, dia)`, `Description(...)`, `IsLastInstallment`, `DetailAmount` (texto com 2 casas ou `0`) |
| `AutomationTexts` | textos fixos de WhatsApp e de erro (SMS quitada/avulsa, "Transacao salva!", `CONTA QUITADA`) |

## 2. Application (`MeuSecretario.Application/Automation`)

**Portas** (cada uma pequena; a Infrastructure implementa com Dapper):

```csharp
public interface IUserApiKeyAuthenticator { Task<AutomationUser?> AuthenticateAsync(string? key, CancellationToken ct); }  // sem chave => null
public sealed record AutomationUser(int UserId, string WhatsApp);

public interface IAutomationLookups   // contas ativas, categorias, contas fixas, nomes
{
    Task<IReadOnlyList<AutomationAccount>> ListActiveAccountsAsync(int userId, CancellationToken ct);              // ORDER BY id
    Task<AutomationAccount?> FindAccountByNameOrTypeAsync(int userId, string name, CancellationToken ct);          // §7.3
    Task<int?> FindAccountIdByNameAsync(int userId, string name, bool fallback, CancellationToken ct);             // 'Carteira'
    Task<IReadOnlyList<CategoryOption>> ListCategoriesAsync(int userId, bool income, CancellationToken ct);        // ORDER BY s.id
    Task<string?> GetCategoryLabelAsync(int subcategoryId, CancellationToken ct);                                  // "Macro -> Sub"
    Task<IReadOnlyList<FixedBill>> ListFixedBillsAsync(int userId, CancellationToken ct);                          // FIXO/LEMBRETE_VARIAVEL ativos, ORDER BY id
    Task<SettleTarget?> FindSettleTargetAsync(int userId, int scheduleId, CancellationToken ct);                   // descricao, conta_id, subcategoria_id
}

public interface IBudgetReader
{
    Task<IReadOnlyList<PotRow>> ListPotsForSubcategoryAsync(int userId, int subcategoryId, CancellationToken ct);
    Task<decimal> SumPotSpendAsync(int userId, int potId, DateOnly from, DateOnly to, CancellationToken ct);       // abs(SUM)
}

public interface IReserveScheduleStore
{
    Task<ReserveScheduleRow?> FindActiveScheduleAsync(int userId, int scheduleId, CancellationToken ct);
    Task SetIncludeInReserveAsync(int userId, int scheduleId, bool include, CancellationToken ct);
    Task<ReserveListPage> ListAsync(int userId, ReserveListFilter filter, CancellationToken ct);                    // COUNT + página (mesma conexão)
}

public interface IInvoiceScheduleProcessor   // process_invoice_schedules, uma transação própria
{
    Task<InvoiceScheduleStats> ProcessAsync(int invoiceId, int cardAccountId, DateOnly referenceDate, CancellationToken ct);
    Task<ReprocessTarget?> FindInvoiceAsync(int invoiceId, CancellationToken ct);                                   // cartão, vencimento, status
}

public interface IPendingTransactionStore  // create_pending_transaction
{
    Task<string?> CreateAsync(string whatsapp, IReadOnlyDictionary<string, object?> data, CancellationToken ct);    // null = Redis falhou
    Task SetLastPendingAsync(string whatsapp, string id, CancellationToken ct);
}
public interface IPendingIdGenerator { string Next(); }       // 8 hex
public interface IAutomationDiagnostics { /* eventos sem texto de exceção: falha do bot, do populador */ }
```

**Casos de uso** (uma classe cada, `ExecuteAsync`, resultado tipado sem HTTP):
- `ProcessAutomateNotification` (§3.1): ordem da tabela, `AutomateChoice`, `ILlmClient` (extração sem cache; categoria com `LlmCachePolicies.Category` e `UserId`), `InvoiceCycle` por `InvoiceLinking` (via `ITransactionsUnitOfWork`, comita antes do Redis), `ICache.IsAvailableAsync`, `IPendingTransactionStore`, `ITransactionPosting`, `IBotClient`. Retorna `AutomateResult.{Saved, Pending(id), Unauthorized, MissingData, NotConfigured}`; exceção do `ILlmClient` sobe com o tipo para o endpoint mapear (RN-011-05).
- `RegisterApiTransaction` (§3.2): `ApiTransactionInput` ⇒ autentica ⇒ conta ⇒ categoria ⇒ `EnsureCurrentAsync` + `InvoiceLinking` (quando `credito`) ⇒ `BudgetValidator` ⇒ pendência ou lançamento direto.
- `ProcessSmsPayment` (§3.3): `IDatabaseHealth.CheckAsync` primeiro ⇒ campos ⇒ chave ⇒ data ⇒ `BillMatching` ⇒ `PostSimple` (achou) ou Gemini + categoria + `Carteira` + `PostSimple` (não achou) ⇒ aviso ⇒ resposta.
- `ToggleReserveSchedule` (§3.4) e `ListReserveSchedules` (§3.5): `IReserveScheduleStore` + `IEmergencyReserveService` (antes e depois no toggle).
- `ReprocessInvoice` (§3.6): `IInvoiceScheduleProcessor`.
- `BudgetValidator` (§7.6): `IBudgetReader` + `BudgetPeriods`.
- `InvoiceScheduleProcessor` (`IInvoiceScheduleProcessor`) e `InvoiceSchedulePopulator` (`IInvoiceSchedulePopulator`, chama o processador com o vencimento como referência).
- `UserApiKeyAuthenticator` (`IUserApiKeyAuthenticator`): lê `IUserKeyReader.ListAsync()` (porta da Infrastructure), `IEncryptionService.LooksEncrypted/Decrypt`, `CryptographicOperations.FixedTimeEquals`.

Textos em português dos erros ficam no endpoint (`AutomationMessages`), como nas outras features; textos de WhatsApp ficam no Domain (`AutomationTexts`) porque são regra de negócio testada por snapshot.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Automation`)

Dapper, colunas minúsculas, sem aspas, texto do cliente sempre como parâmetro (a única concatenação são fragmentos fixos escolhidos por `if`):

- `UserKeyReader`: `SELECT id, numero_whatsapp, api_key_automate FROM usuarios WHERE api_key_automate IS NOT NULL ORDER BY id`.
- `AutomationLookups`: `SELECT id, nome_conta, tipo_conta FROM contas WHERE usuario_id = @u AND ativa = true ORDER BY id`; busca por nome: `nome_conta = @n`, `nome_conta ILIKE @like`, `tipo_conta ILIKE @like` (cada um `AND usuario_id = @u AND ativa = true ORDER BY id LIMIT 1`, o `@like` com `%` e `_`/`\` do cliente **sem escapar**, como o Flask); categorias com os grupos fixos do Flask; rótulo `Macro -> Sub`; contas fixas `JOIN subcategoria`.
- `BudgetReader`: `potesdegastos ⨝ potesubcategorias` (`ativo = true`); soma `abs(COALESCE(SUM(t.valor),0))` com `tipo_transacao = 'Despesa'` no período.
- `ReserveScheduleStore`: `SELECT ... FROM agendamentos a JOIN subcategoria s JOIN macrocategoria m JOIN grupocategoria g WHERE a.usuario_id = @u AND a.ativo = TRUE [AND a.incluir_na_reserva = @i] [AND a.periodicidade = @p] [AND s.nome_sub ILIKE @c] ORDER BY a.valor_previsto DESC, a.descricao ASC LIMIT @limit OFFSET @offset` e o `COUNT(*)` com os mesmos fragmentos (sem macro/grupo: `JOIN` de FK `NOT NULL`, sem diferença); `UPDATE agendamentos SET incluir_na_reserva = @i WHERE id = @id AND usuario_id = @u`.
- `InvoiceScheduleStore` (usado por `InvoiceScheduleProcessor`): uma conexão e uma transação; leitura dos agendamentos (SQL do §7.7), `EXISTS` de (`fatura_id`, `agendamento_id`), `INSERT INTO transacoes (usuario_id, conta_id, subcategoria_id, fatura_id, agendamento_id, descricao, valor, tipo_transacao, data_transacao, consolidada)` e o `UPDATE agendamentos SET parcelas_executadas, ativo`. É o único SQL desta feature em `transacoes` (o Flask também grava em lote aqui; a 004 não oferece um método em lote com `agendamento_id` por fatura e a 013 deve substituir). O restante dos lançamentos usa a 004.
- `PendingTransactionStore`: `ICache.SetJsonAsync("pending_tx:{whatsapp}:{id}", dados, 300 s)` e `last_pending`; id por `IPendingIdGenerator` (`Guid.NewGuid().ToString("D")[..8]`).

`AddAutomationInfrastructure` fica dentro do `AutomationModule` (como a 004).

## 4. Api (`MeuSecretario.Api/Automation`)

- `AutomationModule : IFeatureModule`. `AddServices`: todas as portas e casos de uso, `IInvoiceSchedulePopulator`, `IAutomationDiagnostics`, `IPendingIdGenerator`. `MapEndpoints`: `MapPost("/webhook-automate")`, `MapPost("/api/transacao")`, `MapPost("/webhook-sms-payment")`, `MapMethods("/api/agendamento/{id:regex(^\d+$)}/reserva", ["PATCH"])`, `MapGet("/api/agendamentos/reserva")`, `MapPost("/api/fatura/{id:regex(^\d+$)}/reprocessar").RequireApiKey(AutomationResponses.Unauthorized)`. Sem `RequireUser`. Id maior que `int.MaxValue` ⇒ tratado como 0 (404 do próprio caso).
- Corpo lido como `JsonElement?` (`JsonBody.ReadObjectAsync` compartilhado, desvio 024; corpo inválido ⇒ `null`).
- `AutomationResponses` (`Envelopes.B`, `Envelopes.E` no 503 do SMS, e o JSON próprio de `/api/transacao`); chaves na ordem da spec; `double` para números, texto `"49.90"` em `detalhes`.
- `AutomationMessages` (todas as mensagens da spec, copiadas literalmente) e `AutomationLog` (`LoggerMessage`, EventIds **11001–11099**; só `SafeError.Describe`, ids e número mascarado).
- Cada endpoint: `try/catch (Exception ex) when (ex is not OperationCanceledException)` ⇒ log + 500 com a mensagem fixa; as exceções de `ILlmClient` com texto literal do usuário saem como estão (RN-011-05).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Unitário (Domain) | `PyJsonValueTests`, `InputSanitizerTests`, `PaymentTypeTests`, `MoneyTextTests`, `FuzzyScorerTests` (fixture do `rapidfuzz`), `BillMatchingTests`, `BudgetPeriodsTests`, `BudgetValidationTests` (fixture), `AutomationPromptsTests` (fixture), `ConfirmationMessageTests` (fixture), `AutomateChoiceTests`, `NotificationExtractionTests`, `ApiTransactionInputTests`, `PyIsoDateTests`, `ReserveListQueryTests`, `ReserveToggleInputTests`, `InvoiceScheduleRulesTests` | AC-011-032, 033, 040, 140 a 146, 148 |
| Unitário (Application, NSubstitute) | `UserApiKeyAuthenticatorTests`, `ProcessAutomateNotificationTests`, `RegisterApiTransactionTests`, `ProcessSmsPaymentTests`, `ToggleReserveScheduleTests`, `ListReserveSchedulesTests`, `ReprocessInvoiceTests`, `InvoiceScheduleProcessorTests` | ordem de validações, Redis fora, pendência, chamadas à 004 e ao bot, AC-011-001 a 003, 010 a 019, 030 a 042, 060 a 068, 080 a 085, 120 a 127, 147 |
| Unitário (Api) | `AutomationResponsesTests`, `AutomationMessagesTests` | corpo exato e ordem das chaves |
| Integração | `UserKeyReaderTests`, `AutomationLookupsTests`, `ReserveScheduleStoreTests`, `InvoiceScheduleStoreTests`, `BudgetReaderTests` | SQL real: conta ativa, `ILIKE`, ordem, filtros, paginação, idempotência, rollback |
| Integração | `AutomateEndpointTests`, `ApiTransacaoEndpointTests`, `SmsPaymentEndpointTests`, `ReserveEndpointsTests`, `ReprocessEndpointTests`, `AutomationRoutingTests` | cada linha da §3 com corpo como texto exato; `ILlmClient`, `IBotClient` e `ICache` falsos por `WithWebHostBuilder`; banco real |
| Contrato (Fase 3) | goldens do Flask de homologação | o que for observável sem Gemini; payloads reais do atalho |

Nomes `Metodo_Cenario_Resultado` com `[Trait("AC","011-NNN")]`; integração com `[Trait("Category","Docker")]` e `[Collection(PostgresTestGroup.Name)]`. Fixture Python: `tests/Fixtures/generate_automation_fixtures.py` gera `automation-fixtures.json` com a referência do Flask (carregando os módulos por texto, sem importar o pacote `app`) e do `rapidfuzz` 3.14.3 (`pip install rapidfuzz==3.14.3` só para regenerar; o CI não precisa de Python).

## 6. Fakes e fixtures

- `FakeLlmClient` (devolve respostas por prompt e guarda o prompt recebido), `FakeBotClient` (registra número e texto), `InMemoryCache` (010-0) com liga/desliga de disponibilidade e falha de escrita, `FakePendingIdGenerator`, relógio falso (`IClock`/`FakeTimeProvider`).
- `AutomationTestData` (só teste): usuário com `api_key_automate` em texto puro ou cifrada, contas, cartão com dias, agendamentos de cada tipo, potes, faturas.
- `WebApplicationFactory` com `WithWebHostBuilder` substituindo `ILlmClient`, `IBotClient` e `ICache` (e `GEMINI_API_KEY` presente ou vazia por `UseSetting`).

## 7. Riscos e decisões

- **`rapidfuzz` sem pacote.** O porte (`FuzzyScorer`) é ~250 linhas e o teste compara ~300 pares com o `rapidfuzz` real; qualquer diferença quebra o teste.
- **Gemini lento no meio da requisição.** O Flask segura a conexão do banco durante a chamada; o .NET não segura conexão (cada passo abre a sua), sem mudança de contrato.
- **SQL em `transacoes` fora da 004.** Só no populador (§7.7), pois a regra (dia do vencimento, `agendamento_id`, contador) é do `process_invoice_schedules` e não do lançamento simples. A 013 deve reaproveitar ou substituir.
- **`InvoiceLinking` é `internal` da 004.** Dependência intencional dentro do mesmo assembly; se a 004 mudar a assinatura, o compilador avisa.
- **Duas versões do populador.** A 004 registra o vazio com `TryAdd`; este módulo vence por ordem de nome. Teste de resolução cobre.
- **Ordem do dicionário no casamento difuso.** O Python usa `dict` (última linha vence em descrições repetidas, primeira posição de inserção); o porte copia.
- **Cobertura:** Domain ≥ 90% e Application ≥ 80%.
