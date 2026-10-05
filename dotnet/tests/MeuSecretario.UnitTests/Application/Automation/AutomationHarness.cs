using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Categories;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.UnitTests.Application.Transactions;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Automation;

/// <summary><see cref="ILlmClient"/> com respostas roteirizadas: guarda os pedidos (prompt, usuário, política de cache).</summary>
internal sealed class FakeLlm : ILlmClient
{
    private readonly Queue<Func<LlmRequest, string>> _responses = new();

    public List<LlmRequest> Requests { get; } = [];

    public FakeLlm Then(string text)
    {
        _responses.Enqueue(_ => text);
        return this;
    }

    public FakeLlm ThenThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue()(request) : throw new InvalidOperationException("resposta do Gemini não roteirizada"));
    }
}

internal sealed class FixedIdGenerator(string id = "ab12cd34") : IPendingIdGenerator
{
    public string NewId() => id;
}

/// <summary>Conjunto de fakes e os casos de uso montados, com os valores padrão do usuário 42 (WhatsApp 5511999991072).</summary>
internal sealed class AutomationHarness
{
    public const int UserId = FakeTransactionsSession.UserId;
    public const string WhatsApp = "5511999991072";
    public const string Key = "chave-do-atalho";

    public static readonly DateOnly Today = new(2026, 10, 5);

    public AutomationHarness()
    {
        Settings.GeminiApiKey.Returns("gemini");
        Auth.AuthenticateAsync(Key, Arg.Any<CancellationToken>()).Returns(new AutomationUser(UserId, WhatsApp));
        Clock.Today.Returns(Today);
        Health.CheckAsync(Arg.Any<CancellationToken>()).Returns(new DatabaseStatus(true));

        Lookups.ListActiveAccountsAsync(UserId, Arg.Any<CancellationToken>()).Returns(Accounts);
        Lookups.ListCategoriesAsync(UserId, Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Categories);
        Lookups.GetCategoryLabelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns("Alimentação -> Supermercado");
        Lookups.ListFixedBillsAsync(UserId, Arg.Any<CancellationToken>()).Returns(Bills);
        CategoryLookup.FindFallbackAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(2);

        Pending.CreateAsync(WhatsApp, Arg.Any<IReadOnlyDictionary<string, object?>>(), Arg.Any<CancellationToken>()).Returns("ab12cd34");
        Posting.PostSimpleAsync(Arg.Any<PostSimpleTransaction>(), Arg.Any<CancellationToken>())
            .Returns(new PostingResult<PostedTransaction>.Ok(new PostedTransaction(500, -45.9m, null, null)));
        Budget.ListPotsForSubcategoryAsync(UserId, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        Session.AddAccount(3, "Cartão Inter", "Cartão de Crédito", 10, 15);
    }

    public IIntegrationSettings Settings { get; } = Substitute.For<IIntegrationSettings>();

    public IUserApiKeyAuthenticator Auth { get; } = Substitute.For<IUserApiKeyAuthenticator>();

    public FakeLlm Llm { get; } = new();

    public IAutomationLookups Lookups { get; } = Substitute.For<IAutomationLookups>();

    public ICategoryLookup CategoryLookup { get; } = Substitute.For<ICategoryLookup>();

    public IBudgetReader Budget { get; } = Substitute.For<IBudgetReader>();

    public IPendingTransactionStore Pending { get; } = Substitute.For<IPendingTransactionStore>();

    public ITransactionPosting Posting { get; } = Substitute.For<ITransactionPosting>();

    public IInvoiceService Invoices { get; } = Substitute.For<IInvoiceService>();

    public IBotClient Bot { get; } = Substitute.For<IBotClient>();

    public IClock Clock { get; } = Substitute.For<IClock>();

    public IDatabaseHealth Health { get; } = Substitute.For<IDatabaseHealth>();

    public TestSupport.InMemoryCache Cache { get; } = new();

    public FakeTransactionsSession Session { get; } = new();

    public List<AccountRef> Accounts { get; } =
    [
        new(1, "Carteira", "Dinheiro"),
        new(2, "Nubank", "Conta Corrente"),
        new(3, "Cartão Inter", "Cartão de Crédito"),
    ];

    public List<CategoryOption> Categories { get; } =
    [
        new(1, "Supermercado", "Alimentação"),
        new(2, "Outros", "Despesas Gerais"),
        new(5, "Salário", "Renda Ativa"),
    ];

    public List<FixedBill> Bills { get; } =
    [
        new(10, "Luz", 120m, 10, "FIXO", 2, "Energia"),
        new(11, "Aluguel", 1500m, 5, "FIXO", 2, "Moradia"),
    ];

    public TransactionCategorizer Categorizer => new(Lookups, CategoryLookup, Llm);

    public CurrentInvoiceResolver InvoiceResolver => new(Invoices, new FakeUnitOfWork(Session), Clock);

    public DirectPoster Poster => new(Posting);

    public ProcessAutomateNotification Automate =>
        new(Settings, Auth, Llm, Lookups, Categorizer, InvoiceResolver, Cache, Pending, Poster, Bot, Clock);

    public RegisterApiTransaction ApiTransaction =>
        new(Settings, Auth, Lookups, Categorizer, InvoiceResolver, new BudgetValidator(Budget), Cache, Pending, Poster, Bot, Clock);

    public ProcessSmsPayment Sms => new(Health, Auth, Lookups, Llm, Categorizer, Poster, Bot, Clock);

    /// <summary>Corpo JSON do atalho/SMS a partir de texto.</summary>
    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public IReadOnlyDictionary<string, object?> PendingData() =>
        (IReadOnlyDictionary<string, object?>)Pending.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IPendingTransactionStore.CreateAsync)).GetArguments()[1]!;

    public string BotMessage() =>
        (string)Bot.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IBotClient.SendTextAsync)).GetArguments()[1]!;

    public PostSimpleTransaction Posted() =>
        (PostSimpleTransaction)Posting.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(ITransactionPosting.PostSimpleAsync)).GetArguments()[0]!;
}
