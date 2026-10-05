using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Bills;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.TestFixtures;
using MeuSecretario.UnitTests.TestSupport;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Automation;

public class ToggleReserveScheduleTests
{
    private readonly IUserApiKeyAuthenticator _auth = Substitute.For<IUserApiKeyAuthenticator>();
    private readonly IReserveScheduleStore _store = Substitute.For<IReserveScheduleStore>();
    private readonly IEmergencyReserveService _reserve = Substitute.For<IEmergencyReserveService>();
    private readonly ToggleReserveSchedule _useCase;

    public ToggleReserveScheduleTests()
    {
        _auth.AuthenticateAsync("k", Arg.Any<CancellationToken>()).Returns(new AutomationUser(42, "5511999991072"));
        _store.FindActiveScheduleAsync(42, 7, Arg.Any<CancellationToken>()).Returns(new ReserveScheduleRow("Netflix", 49.9m, "MENSAL", "FIXO"));
        _reserve.GetStatusAsync(42, Arg.Any<CancellationToken>()).Returns(
            new EmergencyReserveStatus(2500, 15000, 6), new EmergencyReserveStatus(2549.9, 15299.4, 6));
        _useCase = new ToggleReserveSchedule(_auth, _store, _reserve);
    }

    private static ReserveToggleParse Body(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ReserveToggleInput.Parse(document.RootElement);
    }

    [Fact]
    [Trait("AC", "011-080")]
    public async Task MissingFields_ShortCircuits()
    {
        (await _useCase.ExecuteAsync(Body("{}"), 7, CancellationToken.None)).ShouldBeOfType<ToggleReserveResult.MissingFields>();

        await _auth.DidNotReceiveWithAnyArgs().AuthenticateAsync(default, default);
    }

    [Fact]
    [Trait("AC", "011-080")]
    public async Task UnknownKey_IsUnauthorized() =>
        (await _useCase.ExecuteAsync(Body("""{"incluir":true,"api_key":"x"}"""), 7, CancellationToken.None)).ShouldBeOfType<ToggleReserveResult.Unauthorized>();

    [Fact]
    [Trait("AC", "011-081")]
    public async Task Updates_AndReportsTheReserveBeforeAndAfter()
    {
        var result = await _useCase.ExecuteAsync(Body("""{"incluir":true,"api_key":"k"}"""), 7, CancellationToken.None);

        var updated = result.ShouldBeOfType<ToggleReserveResult.Updated>();
        updated.Include.ShouldBeTrue();
        updated.Before.Total.ShouldBe(15000);
        updated.After.Total.ShouldBe(15299.4);
        await _store.Received(1).SetIncludeInReserveAsync(42, 7, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "011-081")]
    public async Task False_IsAValidValueToWrite()
    {
        await _useCase.ExecuteAsync(Body("""{"incluir":false,"api_key":"k"}"""), 7, CancellationToken.None);

        await _store.Received(1).SetIncludeInReserveAsync(42, 7, false, Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "011-082")]
    public async Task UnknownOrInactiveSchedule_IsNotFoundAndNothingChanges()
    {
        var result = await _useCase.ExecuteAsync(Body("""{"incluir":true,"api_key":"k"}"""), 99, CancellationToken.None);

        result.ShouldBeOfType<ToggleReserveResult.NotFound>();
        await _store.DidNotReceiveWithAnyArgs().SetIncludeInReserveAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "011-084")]
    public async Task NonBoolean_FailsAndNothingChanges()
    {
        await Should.ThrowAsync<AutomationFailureException>(() => _useCase.ExecuteAsync(Body("""{"incluir":"true","api_key":"k"}"""), 7, CancellationToken.None));

        await _store.DidNotReceiveWithAnyArgs().SetIncludeInReserveAsync(default, default, default, default);
    }
}

public class ListReserveSchedulesTests
{
    private readonly IUserApiKeyAuthenticator _auth = Substitute.For<IUserApiKeyAuthenticator>();
    private readonly IReserveScheduleStore _store = Substitute.For<IReserveScheduleStore>();
    private readonly IEmergencyReserveService _reserve = Substitute.For<IEmergencyReserveService>();
    private readonly ListReserveSchedules _useCase;

    public ListReserveSchedulesTests()
    {
        _auth.AuthenticateAsync("k", Arg.Any<CancellationToken>()).Returns(new AutomationUser(42, "5511999991072"));
        _store.ListAsync(42, Arg.Any<ReserveListFilter>(), Arg.Any<CancellationToken>()).Returns(new ReserveListPage(0, []));
        _reserve.GetStatusAsync(42, Arg.Any<CancellationToken>()).Returns(new EmergencyReserveStatus(0, 0, 6));
        _useCase = new ListReserveSchedules(_auth, _store, _reserve);
    }

    [Theory]
    [Trait("AC", "011-100")]
    [InlineData(null)]
    [InlineData("")]
    public async Task MissingKey_Is400(string? key) =>
        (await _useCase.ExecuteAsync(new ListReserveInput(key, null, null, null, null, null), CancellationToken.None)).ShouldBeOfType<ListReserveResult.MissingKey>();

    [Fact]
    [Trait("AC", "011-100")]
    public async Task UnknownKey_IsUnauthorized() =>
        (await _useCase.ExecuteAsync(new ListReserveInput("x", null, null, null, null, null), CancellationToken.None)).ShouldBeOfType<ListReserveResult.Unauthorized>();

    [Fact]
    [Trait("AC", "011-103")]
    public async Task InvalidPeriodicity_IsReported_AfterAuthentication() =>
        (await _useCase.ExecuteAsync(new ListReserveInput("k", null, "semestral", null, null, null), CancellationToken.None)).ShouldBeOfType<ListReserveResult.InvalidPeriodicity>();

    [Fact]
    [Trait("AC", "011-105")]
    public async Task InvalidPaging_Fails() =>
        await Should.ThrowAsync<AutomationFailureException>(
            () => _useCase.ExecuteAsync(new ListReserveInput("k", null, null, null, "abc", null), CancellationToken.None));

    [Fact]
    [Trait("AC", "011-101")]
    public async Task Lists_WithTheFiltersAndTheReserveSummary()
    {
        var result = await _useCase.ExecuteAsync(new ListReserveInput("k", "TRUE", "mensal", "net", "5", "2"), CancellationToken.None);

        var listed = result.ShouldBeOfType<ListReserveResult.Listed>();
        listed.Filter.ShouldBe(new ReserveListFilter(true, "MENSAL", "net", 5, 2));
        listed.Status.Months.ShouldBe(6);
        await _store.Received(1).ListAsync(42, listed.Filter, Arg.Any<CancellationToken>());
    }
}

public class ReprocessInvoiceTests
{
    [Fact]
    [Trait("AC", "011-121")]
    public async Task UnknownInvoice_IsNotFound_AndNothingIsProcessed()
    {
        var unitOfWork = Substitute.For<IInvoiceScheduleUnitOfWork>();
        var processor = Substitute.For<IInvoiceScheduleProcessor>();

        var result = await new ReprocessInvoice(unitOfWork, processor).ExecuteAsync(55, CancellationToken.None);

        result.ShouldBeOfType<ReprocessInvoiceResult.NotFound>().InvoiceId.ShouldBe(55);
        await processor.DidNotReceiveWithAnyArgs().ProcessAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "011-122")]
    public async Task KnownInvoice_UsesTheDueDateAsReference()
    {
        var unitOfWork = Substitute.For<IInvoiceScheduleUnitOfWork>();
        var processor = Substitute.For<IInvoiceScheduleProcessor>();
        var target = new ReprocessTarget(5, 3, "Cartão Inter", new DateOnly(2026, 4, 15), "Aberta");
        unitOfWork.FindInvoiceAsync(5, Arg.Any<CancellationToken>()).Returns(target);
        processor.ProcessAsync(5, 3, new DateOnly(2026, 4, 15), Arg.Any<CancellationToken>()).Returns(InvoiceScheduleStats.Empty);

        var result = await new ReprocessInvoice(unitOfWork, processor).ExecuteAsync(5, CancellationToken.None);

        result.ShouldBeOfType<ReprocessInvoiceResult.Processed>().Target.ShouldBe(target);
    }
}

public class UserApiKeyAuthenticatorTests
{
    private readonly FernetEncryptionService _encryption = new FernetEncryptionService(AuthFixtures.FernetKey, TimeProvider.System);
    private readonly IUserKeyReader _reader = Substitute.For<IUserKeyReader>();

    private UserApiKeyAuthenticator Build(params StoredUserKey[] rows)
    {
        _reader.ListAsync(Arg.Any<CancellationToken>()).Returns(rows);
        return new UserApiKeyAuthenticator(_reader, _encryption);
    }

    [Fact]
    [Trait("AC", "011-001")]
    public async Task PlainTextLegacyKey_Authenticates()
    {
        var auth = Build(new StoredUserKey(1, "5511", "segredo-legado"));

        (await auth.AuthenticateAsync("segredo-legado", CancellationToken.None))!.UserId.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "011-001")]
    public async Task EncryptedKey_AuthenticatesWithTheClearKey()
    {
        var token = _encryption.Encrypt("a-chave-em-claro-do-atalho-do-usuario-0000");
        var auth = Build(new StoredUserKey(1, "5511", "outra"), new StoredUserKey(2, "5522", token));

        var user = await auth.AuthenticateAsync("a-chave-em-claro-do-atalho-do-usuario-0000", CancellationToken.None);

        user.ShouldBe(new AutomationUser(2, "5522"));
    }

    [Fact]
    [Trait("AC", "011-001")]
    public async Task AFernetTokenThatDoesNotDecrypt_NeverMatches_NotEvenItself()
    {
        var token = _encryption.Encrypt("segredo")[..^4] + "AAAA";
        var auth = Build(new StoredUserKey(1, "5511", token));

        (await auth.AuthenticateAsync(token, CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "011-002")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("errada")]
    public async Task UnknownOrEmptyKey_DoesNotAuthenticate(string? key)
    {
        var auth = Build(new StoredUserKey(1, "5511", "segredo"));

        (await auth.AuthenticateAsync(key, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-147")]
    public async Task ReturnsTheFirstMatchAmongSeveralUsers()
    {
        var auth = Build(
            new StoredUserKey(1, "5511", "a"),
            new StoredUserKey(2, "5522", _encryption.Encrypt("b-chave-longa-para-parecer-fernet-0000000000000")),
            new StoredUserKey(3, "5533", "c"));

        (await auth.AuthenticateAsync("c", CancellationToken.None))!.WhatsApp.ShouldBe("5533");
    }
}

public class TransactionCategorizerTests
{
    private readonly AutomationHarness _h = new();

    private Task<Categorization> Run(bool income = false) =>
        _h.Categorizer.CategorizeAsync(AutomationHarness.UserId, income, "Padaria", "Despesa", CancellationToken.None);

    [Theory]
    [Trait("AC", "011-018")]
    [InlineData("1", 1)]
    [InlineData(" 1 \n", 1)]
    [InlineData("`1`", 1)]
    [InlineData("```1```", 1)]
    [InlineData("+1", 1)]
    [InlineData("1_0", 2)]
    [InlineData("99", 2)]
    [InlineData("abc", 2)]
    [InlineData("1.5", 2)]
    [InlineData("", 2)]
    [InlineData("   ", 2)]
    [InlineData("-1", 2)]
    [InlineData("99999999999999999999", 2)]
    public async Task OnlyAnIdFromTheListIsAccepted_ElseTheFallback(string response, int expected)
    {
        _h.Llm.Then(response);

        (await Run()).CategoryId.ShouldBe(expected);
    }

    [Fact]
    [Trait("AC", "011-018")]
    public async Task TheRequestCarriesTheUserTheCategoryPolicyAndTheFallbackInThePrompt()
    {
        _h.Llm.Then("1");

        await Run();

        var request = _h.Llm.Requests.Single();
        request.UserId.ShouldBe(AutomationHarness.UserId);
        request.Cache.ShouldBe(LlmCachePolicies.Category);
        request.Prompt.ShouldBe(AutomationPrompts.Categorize(_h.Categories, "Padaria", "Despesa", 2));
    }

    [Fact]
    [Trait("AC", "011-018")]
    public async Task Blocked_UsesTheFallback()
    {
        _h.Llm.ThenThrow(new LlmBlockedException("x"));

        (await Run()).CategoryId.ShouldBe(2);
    }

    [Theory]
    [Trait("AC", "011-019")]
    [InlineData(typeof(LlmQuotaExceededException))]
    [InlineData(typeof(LlmPermissionDeniedException))]
    [InlineData(typeof(LlmKeyUnavailableException))]
    public async Task OtherGeminiFailures_Propagate(Type type)
    {
        _h.Llm.ThenThrow((Exception)Activator.CreateInstance(type, "texto literal")!);

        await Should.ThrowAsync(() => Run(), type);
    }

    [Fact]
    [Trait("AC", "011-018")]
    public async Task WithoutFallbackCategory_Fails()
    {
        _h.CategoryLookup.FindFallbackAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns((int?)null);
        _h.Llm.Then("99");

        await Should.ThrowAsync<InvalidOperationException>(() => Run());
    }

    [Fact]
    [Trait("AC", "011-013")]
    public async Task IncomeAsksForTheIncomeCategoriesAndTheIncomeFallback()
    {
        _h.Llm.Then("5");

        await Run(income: true);

        await _h.Lookups.Received(1).ListCategoriesAsync(AutomationHarness.UserId, true, Arg.Any<CancellationToken>());
        await _h.CategoryLookup.Received(1).FindFallbackAsync(true, Arg.Any<CancellationToken>());
    }
}

public class InvoiceScheduleProcessorTests
{
    private sealed class FakeSession(IReadOnlyList<ScheduleToPost> schedules) : IInvoiceScheduleSession
    {
        public List<NewScheduledTransaction> Inserted { get; } = [];

        public List<(int Id, int Executed, bool Active)> Advanced { get; } = [];

        public HashSet<(int Invoice, int Schedule)> Existing { get; } = [];

        public bool Committed { get; private set; }

        public int? RequestedMonth { get; private set; }

        public int FailOnInsert { get; set; }

        public Task<IReadOnlyList<ScheduleToPost>> ListSchedulesAsync(int cardAccountId, int referenceMonth, CancellationToken cancellationToken)
        {
            RequestedMonth = referenceMonth;
            return Task.FromResult(schedules);
        }

        public Task<bool> TransactionExistsAsync(int invoiceId, int scheduleId, CancellationToken cancellationToken) =>
            Task.FromResult(Existing.Contains((invoiceId, scheduleId)));

        public Task InsertTransactionAsync(NewScheduledTransaction row, CancellationToken cancellationToken)
        {
            if (FailOnInsert > 0 && Inserted.Count + 1 == FailOnInsert)
            {
                throw new InvalidOperationException("falha injetada");
            }

            Inserted.Add(row);
            return Task.CompletedTask;
        }

        public Task AdvanceInstallmentAsync(int scheduleId, int newInstallmentsExecuted, bool active, CancellationToken cancellationToken)
        {
            Advanced.Add((scheduleId, newInstallmentsExecuted, active));
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            Committed = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static InvoiceScheduleProcessor Build(FakeSession session)
    {
        var unitOfWork = Substitute.For<IInvoiceScheduleUnitOfWork>();
        unitOfWork.BeginAsync(Arg.Any<CancellationToken>()).Returns(session);
        return new InvoiceScheduleProcessor(unitOfWork);
    }

    private static ScheduleToPost Fixed(int id, string description, decimal? amount, int day = 10, string group = "Despesa Essencial", string type = "FIXO") =>
        new(id, 42, type, description, amount, day, 31, null, null, group);

    [Fact]
    [Trait("AC", "011-122")]
    public async Task PostsEachScheduleAndBuildsTheStats()
    {
        var session = new FakeSession(
        [
            Fixed(1, "Netflix", 49.90m, 31),
            new ScheduleToPost(2, 42, "PARCELADO", "Notebook", 300.00m, 5, 31, 3, 2, "Despesa Discricionária"),
            Fixed(3, "Salário", 100.00m, 1, "Renda", "LEMBRETE_VARIAVEL"),
        ]);

        var stats = await Build(session).ProcessAsync(9, 3, new DateOnly(2026, 2, 15), CancellationToken.None);

        stats.Processed.ShouldBe(3);
        stats.Fixed.ShouldBe(1);
        stats.Installments.ShouldBe(1);
        stats.Reminders.ShouldBe(1);
        stats.Details.Select(d => (d.Type, d.Description, d.Amount)).ShouldBe(
        [
            ("FIXO", "Netflix", (object)"49.90"),
            ("PARCELADO", "Notebook (3/3)", "300.00"),
            ("LEMBRETE_VARIAVEL", "Salário", "100.00"),
        ]);
        session.RequestedMonth.ShouldBe(2);
        session.Committed.ShouldBeTrue();

        session.Inserted[0].Date.ShouldBe(new DateOnly(2026, 2, 28));
        session.Inserted[0].Amount.ShouldBe(-49.9m);
        session.Inserted[0].DbType.ShouldBe("Despesa");
        session.Inserted[0].ScheduleId.ShouldBe(1);
        session.Inserted[0].InvoiceId.ShouldBe(9);
        session.Inserted[0].AccountId.ShouldBe(3);
        session.Inserted[1].Amount.ShouldBe(-300m);
        session.Inserted[2].Amount.ShouldBe(100m);
        session.Inserted[2].DbType.ShouldBe("Renda");
        session.Advanced.ShouldBe([(2, 3, false)]);
    }

    [Fact]
    [Trait("AC", "011-146")]
    public async Task AnInstallmentBeforeTheLastStaysActive()
    {
        var session = new FakeSession([new ScheduleToPost(2, 42, "PARCELADO", "Notebook", 300m, 5, 31, 5, 1, "Despesa Essencial")]);

        await Build(session).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None);

        session.Advanced.ShouldBe([(2, 2, true)]);
    }

    [Fact]
    [Trait("AC", "011-123")]
    public async Task AScheduleAlreadyPostedOnTheInvoice_IsSkipped()
    {
        var session = new FakeSession([Fixed(1, "Netflix", 49.9m), new ScheduleToPost(2, 42, "PARCELADO", "Notebook", 300m, 5, 31, 3, 0, "Despesa Essencial")]);
        session.Existing.Add((9, 1));
        session.Existing.Add((9, 2));

        var stats = await Build(session).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None);

        stats.ShouldBe(InvoiceScheduleStats.Empty, new StatsComparer());
        session.Inserted.ShouldBeEmpty();
        session.Advanced.ShouldBeEmpty();
        session.Committed.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "011-125")]
    public async Task WithoutExpectedAmount_PostsZeroAndReportsTheNumberZero()
    {
        var session = new FakeSession([Fixed(1, "Variável", null)]);

        var stats = await Build(session).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None);

        session.Inserted.Single().Amount.ShouldBe(0m);
        stats.Details.Single().Amount.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "011-146")]
    public async Task AnInstallmentWithoutCounter_IsSkippedLikeTheFlaskTypeError()
    {
        var session = new FakeSession([new ScheduleToPost(2, 42, "PARCELADO", "Notebook", 300m, 5, 31, 3, null, "Despesa Essencial")]);

        var stats = await Build(session).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None);

        stats.Processed.ShouldBe(0);
        session.Inserted.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "011-126")]
    public async Task AFailure_PropagatesAndNothingIsCommitted()
    {
        var session = new FakeSession([Fixed(1, "A", 1m), Fixed(2, "B", 2m)]) { FailOnInsert = 2 };

        await Should.ThrowAsync<InvalidOperationException>(() => Build(session).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None));

        session.Committed.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "011-127")]
    public async Task ThePopulator_ProcessesTheInvoiceWithTheDueDateAsReference()
    {
        var processor = Substitute.For<IInvoiceScheduleProcessor>();

        await new InvoiceSchedulePopulator(processor).PopulateAsync(9, 3, new DateOnly(2026, 4, 15), CancellationToken.None);

        await processor.Received(1).ProcessAsync(9, 3, new DateOnly(2026, 4, 15), Arg.Any<CancellationToken>());
    }

    private sealed class StatsComparer : IEqualityComparer<InvoiceScheduleStats>
    {
        public bool Equals(InvoiceScheduleStats? x, InvoiceScheduleStats? y) =>
            x is not null && y is not null && x.Processed == y.Processed && x.Fixed == y.Fixed && x.Installments == y.Installments
            && x.Reminders == y.Reminders && x.Details.Count == y.Details.Count;

        public int GetHashCode(InvoiceScheduleStats obj) => obj.Processed;
    }
}

public class BudgetValidatorTests
{
    [Fact]
    [Trait("AC", "011-037")]
    public async Task UsesThePeriodOfEachPotAndTheAbsoluteSpend()
    {
        var reader = Substitute.For<IBudgetReader>();
        reader.ListPotsForSubcategoryAsync(42, 1, Arg.Any<CancellationToken>()).Returns(
            [new PotRow(1, "Semanal", 100, "SEMANAL"), new PotRow(2, "Anual", 1000, "ANUAL")]);
        reader.SumPotSpendAsync(42, 1, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11), Arg.Any<CancellationToken>()).Returns(-90m);
        reader.SumPotSpendAsync(42, 2, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), Arg.Any<CancellationToken>()).Returns(10m);

        var check = await new BudgetValidator(reader).ValidateAsync(42, 1, 20, new DateOnly(2026, 10, 7), CancellationToken.None);

        check.RequiresConfirmation.ShouldBeTrue();
        check.Checks.Select(c => c.Exceeds).ShouldBe([true, false]);
        check.Message.ShouldContain("Pote: Semanal");
    }

    [Fact]
    [Trait("AC", "011-037")]
    public async Task WithoutPots_NothingToWarn()
    {
        var reader = Substitute.For<IBudgetReader>();
        reader.ListPotsForSubcategoryAsync(42, 1, Arg.Any<CancellationToken>()).Returns([]);

        (await new BudgetValidator(reader).ValidateAsync(42, 1, 20, new DateOnly(2026, 10, 7), CancellationToken.None)).RequiresConfirmation.ShouldBeFalse();
    }
}

public class PendingTransactionStoreTests
{
    [Fact]
    [Trait("AC", "011-011")]
    public async Task WritesThePendingAndTheLastPendingWith300Seconds()
    {
        var cache = new InMemoryCache();
        var store = new PendingTransactionStore(cache, new FixedIdGenerator());
        var data = PendingTransaction.FromAutomate(42, 2, 1, null, "Mercado", -45.9, 45.9, "Despesa", new DateOnly(2026, 10, 5)).ToData();

        var id = await store.CreateAsync("5511999991072", data, CancellationToken.None);

        id.ShouldBe("ab12cd34");
        var pending = JsonDocument.Parse(cache.RawValue("pending_tx:5511999991072:ab12cd34")!).RootElement;
        pending.GetProperty("descricao").GetString().ShouldBe("Mercado");
        pending.GetProperty("valor_db").GetDouble().ShouldBe(-45.9);
        pending.GetProperty("fatura_id").ValueKind.ShouldBe(JsonValueKind.Null);
        cache.TtlOf("pending_tx:5511999991072:ab12cd34")!.Value.TotalSeconds.ShouldBeInRange(299, 300);
        cache.RawValue("last_pending:5511999991072").ShouldBe("\"ab12cd34\"");
        cache.TtlOf("last_pending:5511999991072")!.Value.TotalSeconds.ShouldBeInRange(299, 300);
    }

    [Fact]
    [Trait("AC", "011-016")]
    public async Task WhenTheCacheIsDown_ReturnsNull()
    {
        var cache = new InMemoryCache { Unavailable = true };

        (await new PendingTransactionStore(cache, new FixedIdGenerator()).CreateAsync("5511", new Dictionary<string, object?>(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "011-011")]
    public void TheGeneratedId_IsEightLowercaseHexCharacters()
    {
        var ids = Enumerable.Range(0, 50).Select(_ => new UuidPendingIdGenerator().NewId()).ToList();

        ids.ShouldAllBe(id => id.Length == 8 && id.All(c => char.IsAsciiHexDigitLower(c)));
        ids.Distinct().Count().ShouldBeGreaterThan(40);
    }
}
