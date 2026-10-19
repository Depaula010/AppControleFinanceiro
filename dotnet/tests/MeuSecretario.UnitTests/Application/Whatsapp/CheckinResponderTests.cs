using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Casos do responder do check-in que o harness do Flask não cobre (desvios 008 e 011, cancelamento, chamadas do executor).</summary>
public class CheckinResponderTests
{
    private const string Number = "5531999990001";
    private const string Id = "chk00001";
    private static readonly DateOnly Today = new(2026, 10, 7);

    private readonly InMemoryCache _cache = new(new FakeTimeProvider());
    private readonly NullCheckinDiagnostics _diagnostics = new();
    private readonly List<ScheduleExecutionRequest> _requests = [];
    private Func<ScheduleExecutionRequest, ScheduleExecutionOutcome> _execute;

    public CheckinResponderTests()
    {
        _execute = request => new ScheduleExecutionOutcome.Posted(1, "x", 1m, null);
    }

    private CheckinResponder Responder() =>
        new(_cache, new CheckinSessionState(_cache), new DelegateExecutor(this), new FinanceClock(Today), _diagnostics);

    private async Task SeedAsync(params CheckinBill[] bills)
    {
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckin(Number, Id), CheckinSession.Build(bills, Today), TimeSpan.FromHours(1));
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(Number), Id, TimeSpan.FromHours(1));
    }

    private static CheckinBill Bill(int id, string description, int day = 7, DateOnly? due = null, string kind = "FIXO") =>
        new(id, description, 100m, day, 1, 10, 7, "Nubank", "Conta Corrente", "Moradia", "Moradia", "Despesa", kind, null, null, due);

    private async Task<bool> SessionEndedAsync() =>
        !await _cache.ExistsAsync(CacheKeys.NightlyCheckin(Number, Id)) && !await _cache.ExistsAsync(CacheKeys.NightlyCheckinActive(Number));

    [Fact]
    [Trait("AC", "012-404")]
    public async Task Ok_PostsEachBillOnItsRealDueDate_InSessionOrder()
    {
        await SeedAsync(Bill(1, "Aluguel", due: Today), Bill(2, "Luz", due: Today.AddDays(-1)), Bill(3, "Água", due: Today.AddDays(-5)), Bill(4, "Sem data", day: 6));

        var reply = await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None);

        _requests.ShouldAllBe(r => r.AllowVariableReminder);
        _requests.Select(r => (r.ScheduleId, r.Date)).ShouldBe([(1, Today), (2, Today.AddDays(-1)), (3, Today), (4, Today.AddDays(-1))]);
        reply.ShouldBe("✅ *Todas as 4 contas confirmadas!*\n\n• Aluguel\n• Luz\n• Água\n• Sem data\n\n💚 Transações registradas com sucesso!");
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task AlreadyPostedInTheMonth_CountsAsConfirmed_NotEligibleIsListedAsNotConfirmed()
    {
        await SeedAsync(Bill(1, "Já lançada", due: Today), Bill(2, "Inativa", due: Today), Bill(3, "Nova", due: Today));
        _execute = request => request.ScheduleId switch
        {
            1 => new ScheduleExecutionOutcome.Skipped("já lançado no mês"),
            2 => new ScheduleExecutionOutcome.NotEligible(),
            _ => new ScheduleExecutionOutcome.Posted(9, "Nova", 100m, null),
        };

        var reply = await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None);

        reply.ShouldBe("✅ *2 conta(s) confirmada(s):*\n\n• Já lançada\n• Nova\n\n💚 Transações registradas com sucesso!\n\n⚠️ *Não foi possível confirmar:*\n\n• Inativa (agendamento inativo ou sem parcelas restantes)");
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task NothingEligible_ListsWhatWasNotConfirmed_AndEndsTheSession()
    {
        await SeedAsync(Bill(1, "Inativa", due: Today));
        _execute = _ => new ScheduleExecutionOutcome.NotEligible();

        (await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None)).ShouldBe("⚠️ *Nenhuma conta foi confirmada:*\n\n• Inativa (agendamento inativo ou sem parcelas restantes)");

        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task ExecutorFailure_EndsTheSession_AnswersWithoutTheReason_AndLogsOnlyTheType()
    {
        await SeedAsync(Bill(1, "Aluguel", due: Today), Bill(2, "Luz", due: Today));
        _execute = request => request.ScheduleId == 2 ? throw new InvalidOperationException("senha=segredo") : new ScheduleExecutionOutcome.Posted(1, "x", 1m, null);

        var reply = await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None);

        reply.ShouldBe("❌ Erro ao salvar transações.");
        reply!.ShouldNotContain("segredo");
        _diagnostics.Failures.ShouldBe(["InvalidOperationException"]);
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task InvalidDateInTheSession_IsTheSameSaveFailure()
    {
        await SeedAsync(Bill(1, "Aluguel", due: Today));
        await _cache.SetJsonAsync(
            CacheKeys.NightlyCheckin(Number, Id),
            new CheckinSession
            {
                Items = new Dictionary<string, CheckinSessionItem> { ["1"] = Item(1, "amanhã") },
                CreatedAt = "2026-10-07",
                TotalItems = 1,
            },
            TimeSpan.FromHours(1));

        (await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None)).ShouldBe("❌ Erro ao salvar transações.");

        _requests.ShouldBeEmpty();
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Theory]
    [InlineData("não", "⏸️ Ok, te lembro amanhã no próximo check-in!")]
    [InlineData("talvez", "❓ Não entendi. Responda com:")]
    [InlineData("7", "Números inválidos: [7]. Use 1 a 1")]
    [Trait("AC", "012-404")]
    public async Task DeferAndInvalidReplies_EndTheSessionWithoutPosting(string message, string start)
    {
        await SeedAsync(Bill(1, "Aluguel", due: Today));

        (await Responder().RespondAsync(Number, message, Id, CancellationToken.None))!.ShouldStartWith(start);

        _requests.ShouldBeEmpty();
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task MissingSession_ClearsOnlyTheActiveFlag()
    {
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(Number), Id, TimeSpan.FromHours(1));

        (await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None)).ShouldBe(CheckinReplyTexts.Expired);

        (await _cache.ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeFalse();
    }

    private static CheckinSessionItem Item(int id, string due) => new()
    {
        Id = id, Description = "Aluguel", ExpectedAmount = 100d, Day = 7, AccountId = 1, SubcategoryId = 10, UserId = 7, AccountName = "Nubank",
        AccountType = "Conta Corrente", Category = "Moradia", MacroName = "Moradia", GroupName = "Despesa", Kind = "FIXO", ExecutedInstallments = null,
        TotalInstallments = null, DueDate = due, StatusText = "Vence hoje", DaysLate = 0,
    };

    private sealed class DelegateExecutor(CheckinResponderTests owner) : IScheduleExecutor
    {
        public Task<ScheduleExecutionOutcome> ExecuteAsync(ScheduleExecutionRequest request, CancellationToken cancellationToken)
        {
            owner._requests.Add(request);
            return Task.FromResult(owner._execute(request));
        }
    }
}
