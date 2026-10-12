# Plan 012 — Webhook WhatsApp e intents

- **Spec:** `spec.md` (aprovada por delegação em 2026-10-07; só paridade e desvios já aprovados)

Reaproveita o que já existe: `Envelopes.E`, `IApiKeyValidator`, `IDatabaseHealth`, `IClock`, `ICache`, `IBotClient`, `ILlmClient` + `LlmCachePolicies`, `ICalendarClient`, `IDriveClient`, `IGoogleAccountService`, `GoogleLinks`, `CalendarFormatter`, `DriveFilePolicy`, `IRouteClient`, `IFavoriteAddressRepository` (007), `ITransactionPosting`, `ITransactionsUnitOfWork`, `InvoiceLinking` (internal, mesmo assembly Application), `IInvoiceService` (004), `IEmergencyReserveService` (005), `ICategoryLookup` (003), 011: `InputSanitizer`, `MoneyText`, `PyFloat`, `PyInt`, `PyJsonValue`, `FuzzyScorer`, `BillMatching`, `BudgetPeriods`, `ConfirmationMessage`, `PendingTransaction`, `IPendingTransactionStore`, `IPendingIdGenerator`, `IAutomationLookups`, `TransactionCategorizer`, `DirectPoster`, `PostgresFixture`, `RedisFixture`, `AuthFixtures`, `PhoneMask`, `SafeError`, `LlmJson`.

**Arquivos compartilhados:** `Program.cs`, `DependencyInjection.cs`, `Directory.Build.props`, `Directory.Packages.props` e `PostgresFixture` **não são editados** (o ScottPlot e o `IChartRenderer` já vieram da 013). Pontos tocados e relatados: `MeuSecretario.UnitTests.csproj` (um `None Include` por arquivo de fixture novo, como a 011 fez). Os recursos embutidos (prompts) usam um `ItemGroup` no `MeuSecretario.Domain.csproj` (arquivo da própria feature, hoje vazio).

Sem EF (Dapper), sem migration. Ordem dos módulos: `MeuSecretario.Api.Whatsapp.WhatsAppModule` registra as portas; nenhum `TryAdd` da 004/011 é sobreposto.

## 1. Domain (`MeuSecretario.Domain/Whatsapp`)

Funções puras, sem I/O. Meta: Domain ≥ 90%.

| Tipo | Papel |
|---|---|
| `WhatsAppPrompts` + `Prompts/*.txt` | modelos gerados do Python e renderização por token; um método por prompt (§8 da spec) |
| `WebhookSignature` | `Verify(payload, signature, key)` HMAC-SHA256 hex em tempo constante |
| `MessageIntent` (classe estática) | decisões do roteador por texto: `IsEventConfirm/Cancel`, `WantsRoute`, `IsExclusiveEventWord`, `LooksLikeTransactionReply`, `HasPaymentWord`, `IsBareCancel`, `HasEscapeKeyword` |
| `WhatsAppTexts` | textos fixos (cadastro, nada pendente, "não entendi", "Ops", limite de IA...) |
| `HelpMenu` | menu (recurso `help-menu.txt` gerado) |
| `ConfirmationFlow` | `Process(userMessage, JsonObject? pending)` ⇒ `ConfirmationOutcome.{Saved, AwaitingCategory, Cancelled, Error}` e `CategorySelection.Format(categories)` |
| `TransactionIntentRules` | validações e montagem da pendência de Renda/Despesa (`PendingData`), valor/parcelas |
| `AccountMention` | `ExtractMentionedName(text)` (palavras-chave e 4 palavras) |
| `FeedbackMessage` | `Format(transaction, pot, footer, kind, showPot)` + semáforo |
| `PaymentItems` | `PaymentExtraction.Parse(json)`; `PaymentReply.Build(...)` (todas as variantes) |
| `BalanceMessages`, `ReserveMessages`, `PotMessages`, `AccountListMessage`, `InvoiceMessages`, `CategorySpendMessage`, `PeriodQuery` (`PeriodRange`, `PeriodMessages`) | consultas da 012c |
| `FixedBillsMessages`, `DueDatesQuery`, `DueDatesMessage`, `CheckinSnapshotMessage` | 012d |
| `EventMessages`, `AgendaPeriods`, `AgendaTimeFilter`, `SchedulePatterns`, `FreeSlots` (+`FreeSlotsMessage`) | 012e |
| `NotificationCommand` | regex da hora, ações e mensagens do resumo/check-in |
| `ReportConfigMessages`, `AddressMessages`, `LocationMessage`, `ApiKeyMessage` | 012f |
| `AnalyticsReport`, `ForecastReport`, `MonthlyComparison` | prompts e textos de análise (valores `:,.2f`, `:+.1f`) |
| `DriveFolderPath` | extração do caminho e sanitização (regex do Python) |
| `PyStrings` | `Capitalize()`, `Title()`, `IsDigitsOnly()` do Python usados nos textos |

## 2. Application (`MeuSecretario.Application/Whatsapp`)

**Portas** (cada uma pequena; Infrastructure implementa com Dapper/Redis):

```csharp
public interface IWhatsAppUserLookup { Task<WhatsAppUser?> FindByNumberAsync(string number, CancellationToken ct); }   // id, nome
public interface IPendingTransactionState { // pending_tx/last_pending
    Task<string?> GetLastPendingIdAsync(string number, CancellationToken ct);
    Task<JsonObject?> GetAsync(string number, string id, CancellationToken ct);
    Task SaveAsync(string number, string id, JsonObject data, CancellationToken ct);       // TTL 300
    Task DeleteBothAsync(string number, string id, CancellationToken ct);                  // pending_tx e last_pending
    Task DeleteLastPendingAsync(string number, CancellationToken ct);
}
public interface IPendingEventStore { // pending_event
    Task<string?> CreateAsync(string number, PendingEvent data, CancellationToken ct);     // id de 8 hex
    Task<(string Id, PendingEvent Data)?> GetLatestAsync(string number, CancellationToken ct);   // última chave de KEYS
    Task<PendingEvent?> GetAsync(string number, string id, CancellationToken ct);
    Task DeleteAsync(string number, string id, CancellationToken ct);
}
public interface INightlyCheckinResponder { Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken ct); }  // null = não tratada
public interface IWhatsAppCheckinSession { Task<string?> GetActiveIdAsync(string number, CancellationToken ct); Task ClearActiveAsync(string number, CancellationToken ct); }
public interface IWhatsAppFinanceReader { ... }    // contas, categorias, saldos, potes, fatura, período, categoria, vencimentos, snapshot
public interface IWhatsAppFixedBillsReader { ... } // pendentes do mês, catálogo
public interface IWhatsAppConfigStore { ... }      // notificationconfigs, monthlyreportconfigs, usuarios.cidade/estado, api_key_automate, saldo_inicial
public interface IFeedbackReader { ... }           // dados da transação + pote + rodapé
public interface IAnalyticsReader { ... }          // consultas de análise/previsão/gráfico
public interface IChartRenderer { byte[]? Pie(...); byte[]? Bars(...); byte[]? Line(...); }
```

**Casos de uso e intenções** (uma classe por intenção, `HandleAsync(IntentContext, ct)` ⇒ `string`):

- `HandleWhatsAppMessage` (pipeline da §4 da spec): recebe `WebhookInput(Text, Number, Media)` já validado; devolve `WebhookReply(string Text)`; lança as exceções do pipeline para o endpoint mapear (429/500).
- `IWhatsAppIntent { string Name; bool Swallows; Task<string> HandleAsync(IntentContext ctx, CancellationToken ct); }` e `IntentRouter` (dicionário por nome exato). `Swallows` = template: o router captura `Exception` (menos cancelamento) e devolve `WhatsAppTexts.IntentFailed`.
- `IntentClassifier` (`get_message_intent`): `ILlmClient` + `LlmCachePolicies.Intent` + `LlmJson.Parse`.
- `TransactionReplyHandler` (`process_confirmation_response` + salvar + feedback), `EventReplyHandler`, `RouteRequestHandler`, `PaymentHandler`, e as intenções: `ExpenseIntent`, `IncomeIntent`, `TransferIntent`, `InvoicePaymentIntent`, `BalanceIntent`, `ReserveIntent`, `ReserveDetailsIntent`, `PotsIntent`, `ListAccountsIntent`, `AdjustBalanceIntent`, `InvoiceValueIntent`, `CategorySpendIntent`, `PeriodIntent`, `FixedBillsIntent`, `AllBillsIntent`, `DueTodayIntent`, `DueTomorrowIntent`, `DueWeekIntent`, `OverdueIntent`, `CreateEventIntent`, `DeleteEventIntent`, `AgendaIntent`, `FreeSlotsIntent`, `NotificationsIntent`, `LocationIntent`, `MonthlyReportIntent`, `AddressConfigIntent`, `AddressListIntent`, `AddressDeleteIntent`, `ApiKeyIntent`, `HelpIntent`, `AnalysisIntent`, `MonthlyComparisonIntent`, `ForecastIntent`, `ChartIntent`, `DriveUploadIntent`.
- `AccountResolver` (`extract_mentioned_account`, `resolve_account_for_transaction`, `choose_account_for_transaction`) sobre `IWhatsAppFinanceReader` e `FuzzyScorer`.
- `InvoiceForDate` (resolve a fatura de uma data: sessão da 004 + `InvoiceLinking`).
- `AgendaService` (`CalendarQueryService` sobre `ICalendarClient`), `FreeTimeFinder` (orquestra), `CheckinSnapshotService`.

## 3. Infrastructure (`MeuSecretario.Infrastructure/Whatsapp`)

Dapper, colunas minúsculas, sem aspas, texto do cliente sempre parâmetro (fragmentos fixos escolhidos por `if`):

- `WhatsAppUserLookup`: `SELECT id, nome FROM usuarios WHERE numero_whatsapp = @number`.
- `PendingTransactionState`, `PendingEventStore`, `CheckinSessionState`: `ICache` (`SetJsonAsync`/`GetJsonAsync`/`KeysByPatternAsync`/`DeleteAsync`); `JsonObject` para preservar o formato.
- `WhatsAppFinanceReader`: as consultas de §6.c/d/e listadas abaixo, portadas literalmente (datas como parâmetro).
- `WhatsAppFixedBillsReader`, `WhatsAppConfigStore`, `FeedbackReader`, `AnalyticsReader`.
- `ScottPlotChartRenderer` (012g).
- `AddWhatsAppInfrastructure` dentro do `WhatsAppModule` (como a 011).

Consultas portadas (SQL em `WhatsAppSql.cs`, uma constante por consulta):

| Consulta | Origem | Notas |
|---|---|---|
| contas ativas | `get_user_accounts` | `+ ativa = true ORDER BY id` (039) |
| conta por nome | `get_account_by_name` | exato, `ILIKE` (menor id), fallback opcional, tipo opcional; `ativa = true` |
| categorias do usuário | `get_user_categories` | já em `IAutomationLookups` |
| saldos | `get_saldo_contas` | idem Flask (`consolidada = true`, `ativa = true`) |
| potes | `get_pote_status` | período por pote (015); um `SUM` por pote |
| faturas resumo/detalhe | `get_fatura_valor`, `get_fatura_detalhada` | `+ c.ativa = true`; `ensure_current_invoice_exists` por `IInvoiceService` |
| período e categoria no período | `period_query_service.py:94-141` | `ORDER BY ... , t.id DESC` |
| gasto da categoria no mês | `get_category_spending` | mês de hoje (`IClock`) |
| vencimentos | `get_vencimentos_periodo` | duas consultas na virada de mês; sem filtro de usuário no `NOT EXISTS` (paridade; desvio 161) |
| contas fixas do mês e catálogo | `fixed_bills_service.py` | idem |
| snapshot do check-in | `agendamentos_queries.py:116-230, 338-457` + `faturas_queries.py` | SQL idêntico; datas como parâmetro |
| lembrete variável | `get_lembrete_variavel_by_description` | `ILIKE` do cliente sem escapar |
| pote da subcategoria + alerta | `transaction_feedback_service.py:20-250` | período pelo `BudgetPeriods` |
| configurações | `notification_config_service.py`, `monthly_report_config_service.py` | `get_or_create` + `UPDATE` parcial com `updated_at` |
| análise e previsão | `analytics_service.py:36-250`, `forecast_service.py:21-190` | `TO_CHAR` mantido; dia da semana no código |
| gráficos | `chart_service.py` | `SUM(...) OVER (ORDER BY data_transacao)` mantido |

## 4. Api (`MeuSecretario.Api/Whatsapp`)

- `WhatsAppModule : IFeatureModule`. `AddServices`: portas, intenções (`IEnumerable<IWhatsAppIntent>`), `IntentRouter`, `HandleWhatsAppMessage`, `IChartRenderer`. `MapEndpoints`: `MapPost("/webhook-whatsapp")`.
- O endpoint **não** usa `RequireApiKey` (a ordem 503 → assinatura → chave exige código próprio): lê o corpo bruto (`EnableBuffering`, `ReadAllBytes`), confere banco/Gemini, assinatura (`WebhookSignature`) e chave (`IApiKeyValidator`), parseia JSON dos mesmos bytes e chama o caso de uso.
- `WhatsAppResponses` (Envelope E e o 400/503 com `mensagem`), `WhatsAppMessages` (textos do 503/401/400/429/500), `WhatsAppLog` (`LoggerMessage`, EventIds **12001–12099**; só `SafeError.Describe`, ids e número mascarado, nunca texto da mensagem).

## 5. Testes

| Tipo | Arquivo | Cobre |
|---|---|---|
| Fixtures | `tests/Fixtures/generate_whatsapp_fixtures.py` ⇒ `whatsapp-fixtures.json` + `Prompts/*.txt` | prompts renderizados, menu de ajuda, formatadores (`vencimentos`, `contas fixas`, `período`, `feedback`, `fatura`, `reserva`, `eventos`, `endereços`, `análise`, `previsão`, `slots livres`, `padrões`), `process_confirmation_response`, `extract_mentioned_account` (palavras-chave) |
| Harness | `tests/Fixtures/whatsapp_handler_harness.py` ⇒ `whatsapp-pipeline-scenarios.json` | executa o `WhatsAppHandler` real com serviços simulados; resposta e chamadas por cenário |
| Unitários Domain | `UnitTests/Domain/Whatsapp/*` | cada tipo puro contra as fixtures |
| Unitários Application | `UnitTests/Application/Whatsapp/*` | pipeline, intenções, resolvedor de conta, com fakes (`FakeClock`, `InMemoryCache`, NSubstitute) |
| Paridade | `WhatsAppPipelineParityTests` | cenários do harness |
| Integração | `IntegrationTests/Whatsapp/*` | repositórios no Postgres real, estado no Redis real, rotas por `WebApplicationFactory` com Gemini/bot/Google falsos |

Fakes: o harness do Python guarda o roteiro (`intent`, saídas dos serviços); o teste do .NET monta `ILlmClient`/portas com o mesmo roteiro e compara texto + sequência de portas chamadas.

## 6. Ordem de entrega

012-0 → 012a → 012b → 012c → 012d → 012e → 012f → 012g → 012h. Cada uma termina com `dotnet build` sem warnings e `dotnet test dotnet/MeuSecretario.slnx` verde, `tasks.md` marcado e commit. A 012g depende do pacote ScottPlot; se o restore ou as bibliotecas nativas falharem, a porta `IChartRenderer` e todo o resto da 012g entram e o renderizador fica como pendência registrada em `tasks.md`.
