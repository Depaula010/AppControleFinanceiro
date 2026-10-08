using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Categories;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Cenários do harness financeiro (tests/Fixtures/whatsapp_harness_sections): o handler real do Flask com intenções reais.</summary>
internal static class FinanceScenarios
{
    private static readonly JsonElement Root = Load();

    public static DateOnly Today => DateOnly.Parse(Root.GetProperty("today").GetString()!, CultureInfo.InvariantCulture);

    public static IEnumerable<JsonElement> All(params string[] groups) =>
        Root.GetProperty("scenarios").EnumerateArray().Where(s => groups.Contains(s.GetProperty("group").GetString()));

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-financial-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

internal sealed class FinanceClock(DateOnly today) : IClock
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    public DateTimeOffset UtcNow => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public DateTime Now => today.ToDateTime(new TimeOnly(12, 0));

    public DateOnly Today => today;

    public TimeZoneInfo TimeZone => Zone;
}

internal sealed class NullFinanceDiagnostics : IFinanceDiagnostics
{
    public void FeedbackFailed(string errorType)
    {
    }

    public void PaymentItemFailed(string errorType)
    {
    }
}

/// <summary>Gemini roteirizado por tipo de prompt (os mesmos rótulos do harness em Python).</summary>
internal sealed class LabelledLlm(JsonElement config, List<string> calls) : ILlmClient
{
    public List<LlmRequest> Requests { get; } = [];

    public static string Label(string prompt)
    {
        if (prompt.StartsWith("Analise a mensagem de transferência", StringComparison.Ordinal))
        {
            return "extract_transfer_details";
        }

        if (prompt.StartsWith("Analise a mensagem de pagamento de fatura", StringComparison.Ordinal))
        {
            return "extract_fatura_payment_details";
        }

        if (prompt.StartsWith("Analise a pergunta sobre consulta de saldo", StringComparison.Ordinal))
        {
            return "extract_saldo_query";
        }

        if (prompt.StartsWith("Analise a pergunta sobre consulta de fatura", StringComparison.Ordinal))
        {
            return "extract_fatura_query";
        }

        if (prompt.Length >= 60 && prompt[..60].Contains("Minhas subcategorias são", StringComparison.Ordinal)
            || prompt.StartsWith("\n    Minhas subcategorias são", StringComparison.Ordinal))
        {
            return "categorize_transaction";
        }

        if (prompt.StartsWith("Analise a mensagem: \"", StringComparison.Ordinal))
        {
            if (prompt.Contains("O tipo é:", StringComparison.Ordinal))
            {
                return "extract_transaction_details";
            }

            if (prompt.Contains("Extraia APENAS os itens", StringComparison.Ordinal))
            {
                return "extract_payment_items";
            }

            return "get_message_intent";
        }

        if (prompt.StartsWith("Analise a pergunta: \"", StringComparison.Ordinal))
        {
            if (prompt.Contains("Identifique o período", StringComparison.Ordinal))
            {
                return "extract_period_query";
            }

            if (prompt.Contains("nome_categoria", StringComparison.Ordinal))
            {
                return "extract_category_query";
            }
        }

        return "desconhecido";
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var label = Label(request.Prompt);
        calls.Add("gemini_service." + label);

        if (label == "get_message_intent")
        {
            if (config.TryGetProperty("intent", out var intent))
            {
                if (intent.ValueKind == JsonValueKind.Object)
                {
                    throw new InvalidOperationException(intent.GetProperty("__raise__").GetString());
                }

                return Task.FromResult(JsonSerializer.Serialize(new { intent = intent.ValueKind == JsonValueKind.Null ? null : intent.GetString() }));
            }

            return Task.FromResult("""{"intent": null}""");
        }

        if (!config.TryGetProperty("llm", out var llm) || !llm.TryGetProperty(label, out var script))
        {
            throw new InvalidOperationException("Gemini não roteirizado: " + label);
        }

        if (script.ValueKind == JsonValueKind.Object)
        {
            throw new InvalidOperationException(script.GetProperty("__raise__").GetString());
        }

        return Task.FromResult(script.GetString()!);
    }
}

/// <summary>Os serviços do mundo simulado (contas, categorias, lançamentos) que gravam o mesmo rótulo de chamada do harness.</summary>
internal sealed class FinanceWorld(JsonElement config, List<string> calls)
{
    private readonly Dictionary<string, int> _sequences = [];

    public IReadOnlyList<AccountRef> Accounts { get; } = config.TryGetProperty("accounts", out var accounts)
        ? [.. accounts.EnumerateArray().Select(a => new AccountRef(a.GetProperty("id").GetInt32(), a.GetProperty("nome_conta").GetString()!, a.GetProperty("tipo_conta").GetString()!))]
        : [];

    public JsonElement Config => config;

    public void Call(string label) => calls.Add(label);

    /// <summary>O valor roteirizado de <c>returns[label]</c> (com <c>__seq__</c> a cada chamada); <c>null</c> se ausente.</summary>
    public JsonElement? Returned(string label)
    {
        if (!config.TryGetProperty("returns", out var returns) || !returns.TryGetProperty(label, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("__seq__", out var items))
        {
            var index = _sequences.GetValueOrDefault(label);
            _sequences[label] = index + 1;
            var list = items.EnumerateArray().ToList();
            value = list[Math.Min(index, list.Count - 1)];
        }

        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("__raise__", out var message))
        {
            throw new InvalidOperationException(message.GetString());
        }

        return value;
    }

    public int? ReturnedInt(string label) => Returned(label) is { ValueKind: JsonValueKind.Number } value ? value.GetInt32() : null;
}

internal sealed class WorldLookups(FinanceWorld world) : IAutomationLookups
{
    public Task<IReadOnlyList<AccountRef>> ListActiveAccountsAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult(world.Accounts);

    public Task<AccountRef?> FindAccountByNameOrTypeAsync(int userId, string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int?> FindAccountIdWithFallbackAsync(int userId, string name, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<CategoryOption>> ListCategoriesAsync(int userId, bool income, CancellationToken cancellationToken)
    {
        world.Call("finance_service.get_user_categories");
        IReadOnlyList<CategoryOption> categories = world.Returned("finance_service.get_user_categories") is { ValueKind: JsonValueKind.Array } list
            ? [.. list.EnumerateArray().Select(c => new CategoryOption(c.GetProperty("id").GetInt32(), c.GetProperty("nome_sub").GetString()!, c.GetProperty("nome_macro").GetString()!))]
            : [];
        return Task.FromResult(categories);
    }

    public Task<string?> GetCategoryLabelAsync(int subcategoryId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<FixedBill>> ListFixedBillsAsync(int userId, CancellationToken cancellationToken)
    {
        IReadOnlyList<FixedBill> bills = world.Config.TryGetProperty("bills", out var list)
            ? [.. list.EnumerateArray().Select(b => new FixedBill(
                b.GetProperty("id").GetInt32(),
                b.GetProperty("descricao").GetString()!,
                b.GetProperty("valor_previsto").ValueKind == JsonValueKind.Null ? null : b.GetProperty("valor_previsto").GetDecimal(),
                b.GetProperty("dia_execucao").GetInt32(),
                b.GetProperty("tipo_agendamento").GetString()!,
                b.GetProperty("conta_id").GetInt32(),
                b.GetProperty("categoria").GetString()!))]
            : [];
        return Task.FromResult(bills);
    }

    public Task<SettleTarget?> FindSettleTargetAsync(int userId, int scheduleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class WorldCategories(FinanceWorld world) : ICategoryLookup
{
    public Task<int?> FindShortTermInvestmentAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int?> FindLoanPrincipalAsync(int userId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<int?> FindFallbackAsync(bool income, CancellationToken cancellationToken)
    {
        world.Call("finance_service.get_fallback_category_id");
        return Task.FromResult(world.ReturnedInt("finance_service.get_fallback_category_id"));
    }
}

internal sealed class WorldFinanceReader(FinanceWorld world) : IWhatsAppFinanceReader
{
    public Task<AccountRef?> FindActiveAccountAsync(int userId, int accountId, CancellationToken cancellationToken) =>
        Task.FromResult(world.Accounts.FirstOrDefault(a => a.Id == accountId));

    public Task<DefaultAccounts> GetDefaultAccountsAsync(int userId, CancellationToken cancellationToken)
    {
        if (!world.Config.TryGetProperty("default_accounts", out var defaults) || defaults.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult(new DefaultAccounts(null, null));
        }

        return Task.FromResult(new DefaultAccounts(Id(defaults, "renda"), Id(defaults, "despesa")));
    }

    public Task<RecurringSchedule?> FindRecurringByDescriptionAsync(int userId, string description, CancellationToken cancellationToken)
    {
        if (!world.Config.TryGetProperty("schedule", out var schedule) || schedule.ValueKind != JsonValueKind.Object)
        {
            return Task.FromResult<RecurringSchedule?>(null);
        }

        return Task.FromResult<RecurringSchedule?>(new RecurringSchedule(
            schedule.GetProperty("descricao").GetString()!,
            schedule.GetProperty("valor_previsto").GetDecimal(),
            schedule.GetProperty("conta_id").GetInt32(),
            schedule.GetProperty("subcategoria_id").GetInt32()));
    }

    public Task<int?> FindAccountIdByNameAsync(int userId, string name, bool fallback, string? type, CancellationToken cancellationToken)
    {
        world.Call("finance_service.get_account_by_name");
        return Task.FromResult(world.ReturnedInt("finance_service.get_account_by_name"));
    }

    private static int? Id(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}

internal sealed class WorldInvoiceForDate(FinanceWorld world) : IInvoiceForDate
{
    public Task<int?> ResolveAsync(int userId, int accountId, DateOnly day, CancellationToken cancellationToken)
    {
        world.Call("finance_service.get_or_create_fatura");
        return Task.FromResult(world.ReturnedInt("finance_service.get_or_create_fatura"));
    }
}

internal sealed class WorldPosting(FinanceWorld world) : ITransactionPosting
{
    public Task<PostingResult<PostedTransaction>> PostSimpleAsync(PostSimpleTransaction command, CancellationToken cancellationToken)
    {
        world.Call("finance_service.create_transaction");
        return Task.FromResult<PostingResult<PostedTransaction>>(new PostingResult<PostedTransaction>.Ok(
            new PostedTransaction(world.ReturnedInt("finance_service.create_transaction") ?? 0, command.Amount, null, null)));
    }

    public Task<PostingResult<PostedTransfer>> PostTransferAsync(PostTransfer command, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<PostingResult<PostedInvoicePayment>> PostInvoicePaymentAsync(PostInvoicePayment command, CancellationToken cancellationToken)
    {
        world.Call("finance_service.create_fatura_payment");
        var card = world.Returned("finance_service.create_fatura_payment")?.GetString() ?? "Cartão";
        return Task.FromResult<PostingResult<PostedInvoicePayment>>(new PostingResult<PostedInvoicePayment>.Ok(
            new PostedInvoicePayment(1, 2, "Origem", card, null, null, true)));
    }

    public Task<PostingResult<PostedInstallmentPurchase>> PostInstallmentPurchaseAsync(PostInstallmentPurchase command, CancellationToken cancellationToken)
    {
        world.Call("finance_service.create_transaction");
        var transactionId = world.ReturnedInt("finance_service.create_transaction") ?? 0;
        world.Call("finance_service.create_parcelamento_agendamento");
        return Task.FromResult<PostingResult<PostedInstallmentPurchase>>(new PostingResult<PostedInstallmentPurchase>.Ok(
            new PostedInstallmentPurchase(transactionId, world.ReturnedInt("finance_service.create_parcelamento_agendamento") ?? 0, null)));
    }
}

internal sealed class WorldFeedback(FinanceWorld world) : IFeedbackService
{
    public Task<string> GenerateAsync(int transactionId, CancellationToken cancellationToken)
    {
        world.Call("feedback");
        return Task.FromResult("FEEDBACK");
    }
}

internal sealed class WorldSettler(FinanceWorld world) : IFixedBillSettler
{
    public Task<int> SettleAsync(int userId, int scheduleId, double amount, DateOnly day, CancellationToken cancellationToken)
    {
        world.Call("FixedBillsService.settle_fixed_bill");
        return Task.FromResult(world.ReturnedInt("FixedBillsService.settle_fixed_bill") ?? 0);
    }
}

internal sealed class FixedPendingIds : IPendingIdGenerator
{
    public string NewId() => "ab12cd34";
}

/// <summary>
/// Pipeline completo (passos e intenções financeiras reais) sobre um mundo simulado por cenário do harness. Os rótulos de chamada
/// são os do Flask, para comparar a sequência.
/// </summary>
internal sealed class FinanceRig
{
    public FinanceRig(JsonElement scenarioConfig)
    {
        var userExists = !scenarioConfig.TryGetProperty("user", out var user) || user.GetBoolean();
        Cache = new InMemoryCache(new FakeTimeProvider()) { Unavailable = scenarioConfig.TryGetProperty("redis_fail", out var fail) && fail.GetBoolean() };
        World = new FinanceWorld(scenarioConfig, Calls);
        Llm = new LabelledLlm(scenarioConfig, Calls);

        var lookups = new WorldLookups(World);
        var categories = new WorldCategories(World);
        var reader = new WorldFinanceReader(World);
        var posting = new WorldPosting(World);
        var clock = new FinanceClock(FinanceScenarios.Today);
        var transactionState = new PendingTransactionState(Cache);
        var pendingStore = new PendingTransactionStore(Cache, new FixedPendingIds());
        var events = new PendingEventStore(Cache, new FixedPendingIds());
        var categorizer = new TransactionCategorizer(lookups, categories, Llm);
        var resolver = new AccountResolver(lookups, reader);
        var invoices = new WorldInvoiceForDate(World);
        var feedback = new WorldFeedback(World);
        var saver = new TransactionConfirmationSaver(new DirectPoster(posting), posting);
        var settler = new WorldSettler(World);

        var steps = new List<IWhatsAppStep>
        {
            new CheckinStep(new CheckinSessionState(Cache), new RecordingCheckinResponder(Cache, Calls, "RESPOSTA-DO-CHECKIN")),
            new TransactionReplyStep(transactionState, lookups, saver, feedback),
            new PaymentStep(Llm, lookups, categories, categorizer, resolver, settler, new DirectPoster(posting), clock, new NullFinanceDiagnostics()),
            new BareCancelStep(transactionState, events),
        };
        var intents = new List<IWhatsAppIntent>
        {
            new HelpIntent(),
            new IncomeIntent(Llm, reader, lookups, categorizer, resolver, invoices, pendingStore, clock),
            new ExpenseIntent(Llm, reader, lookups, categorizer, resolver, invoices, pendingStore, clock),
            new TransferIntent(Llm, lookups, reader),
            new InvoicePaymentIntent(Llm, lookups, reader, posting, clock),
        };

        Router = new IntentRouter(intents, new NullDiagnostics());
        UseCase = new HandleWhatsAppMessage(new FakeUsers(userExists, Calls), steps, new IntentClassifier(Llm), Router);
    }

    public List<string> Calls { get; } = [];

    public InMemoryCache Cache { get; }

    public FinanceWorld World { get; }

    public LabelledLlm Llm { get; }

    public IntentRouter Router { get; }

    public HandleWhatsAppMessage UseCase { get; }

    public Task<string> SendAsync(string text, string rawNumber) =>
        UseCase.ExecuteAsync(new WebhookInput(text, rawNumber, null), CancellationToken.None);

    public async Task SeedAsync(string key, JsonNode? value) =>
        await Cache.SetJsonAsync(key, value, TimeSpan.FromSeconds(300));
}
