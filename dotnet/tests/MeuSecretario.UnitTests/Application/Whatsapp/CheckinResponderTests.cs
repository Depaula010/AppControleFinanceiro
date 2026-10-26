using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Caching;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using NSubstitute;
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

    private readonly IAutomationLookups _lookups = Substitute.For<IAutomationLookups>();
    private readonly ITransactionPosting _posting = Substitute.For<ITransactionPosting>();

    private CheckinResponder InvoiceResponder() =>
        new(new CheckinDialogStore(_cache, new CheckinSessionState(_cache)), new DelegateExecutor(this),
            new CheckinInvoicePayer(_lookups, _posting, new FinanceClock(Today)), new FinanceClock(Today), _diagnostics);

    private static CheckinInvoiceItem Invoice(int id = 9, string card = "Cartão Inter", double amount = 405.73) => new()
    {
        InvoiceId = id, CardAccountId = 3, UserId = 7, Card = card, Amount = amount, DueDate = "2026-10-20",
    };

    private async Task SeedInvoicesAsync(CheckinBill[] bills, params CheckinInvoiceItem[] invoices)
    {
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckin(Number, Id), CheckinSession.Build(bills, Today, invoices), TimeSpan.FromHours(1));
        await _cache.SetJsonAsync(CacheKeys.NightlyCheckinActive(Number), Id, TimeSpan.FromHours(1));
    }

    private void Accounts(params AccountRef[] accounts) =>
        _lookups.ListActiveAccountsAsync(7, Arg.Any<CancellationToken>()).Returns(accounts);

    private void PaymentSucceeds() =>
        _posting.PostInvoicePaymentAsync(Arg.Any<PostInvoicePayment>(), Arg.Any<CancellationToken>())
            .Returns(new PostingResult<PostedInvoicePayment>.Ok(new PostedInvoicePayment(1, 2, "Conta Inter", "Cartão Inter", 9, null, true)));

    [Fact]
    [Trait("AC", "012-404")]
    public async Task Ok_WithOnlyAnInvoice_AsksWhichAccountPaid_ListingOnlyNonCardAccounts_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"), new AccountRef(3, "Cartão Inter", "Cartão de Crédito"), new AccountRef(4, "Carteira", "Carteira"));

        var reply = await InvoiceResponder().RespondAsync(Number, "ok", Id, CancellationToken.None);

        reply.ShouldBe("💳 *Pagar a fatura Cartão Inter (R$ 405,73)?*\nCom qual conta você pagou?\n1. Conta Inter\n2. Carteira\nResponda o número ou \"pular\".");
        _posting.ReceivedCalls().ShouldBeEmpty();
        (await SessionEndedAsync()).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task AnsweringTheAccount_PaysTheInvoiceWithTheChosenAccount_AndEndsTheDialog_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"), new AccountRef(4, "Carteira", "Carteira"));
        PaymentSucceeds();
        var responder = InvoiceResponder();
        await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        var reply = await responder.RespondAsync(Number, "2", Id, CancellationToken.None);

        reply.ShouldBe("✅ Fatura Cartão Inter paga: R$ 405,73 (Carteira).");
        await _posting.Received(1).PostInvoicePaymentAsync(
            Arg.Is<PostInvoicePayment>(p => p.UserId == 7 && p.FromAccountId == 4 && p.CardAccountId == 3 && p.Amount == 405.73m && p.Date == Today),
            Arg.Any<CancellationToken>());
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task SingleAccount_OkPaysWithIt_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"));
        PaymentSucceeds();
        var responder = InvoiceResponder();

        (await responder.RespondAsync(Number, "ok", Id, CancellationToken.None))!
            .ShouldBe("💳 *Pagar a fatura Cartão Inter (R$ 405,73)?*\nResponda \"ok\" para pagar com Conta Inter ou \"pular\".");
        (await responder.RespondAsync(Number, "ok", Id, CancellationToken.None)).ShouldBe("✅ Fatura Cartão Inter paga: R$ 405,73 (Conta Inter).");
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task Skip_LeavesTheInvoiceOpen_WithoutPosting_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"), new AccountRef(4, "Carteira", "Carteira"));
        var responder = InvoiceResponder();
        await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        (await responder.RespondAsync(Number, "pular", Id, CancellationToken.None)).ShouldBe("⏭️ A fatura Cartão Inter continua em aberto.");

        _posting.ReceivedCalls().ShouldBeEmpty();
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task InvalidAnswer_RepeatsTheQuestion_AndKeepsTheDialog_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"), new AccountRef(4, "Carteira", "Carteira"));
        var responder = InvoiceResponder();
        await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        var reply = await responder.RespondAsync(Number, "talvez", Id, CancellationToken.None);

        reply!.ShouldStartWith("❓ Não entendi. Responda o número da conta que pagou a fatura ou \"pular\".\n\n💳 *Pagar a fatura Cartão Inter");
        (await SessionEndedAsync()).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task RefusedPayment_TellsTheUserHowToDoItByHand_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"));
        _posting.PostInvoicePaymentAsync(Arg.Any<PostInvoicePayment>(), Arg.Any<CancellationToken>())
            .Returns(new PostingResult<PostedInvoicePayment>.Failed(PostingError.RequiredCategoryMissing));
        var responder = InvoiceResponder();
        await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        (await responder.RespondAsync(Number, "ok", Id, CancellationToken.None))!.ShouldStartWith("❌ Não consegui registrar o pagamento da fatura Cartão Inter.");
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task BillsAreConfirmedFirst_ThenTheInvoicesAreAskedOneByOne_Deviation264()
    {
        await SeedInvoicesAsync([Bill(1, "Aluguel", due: Today)], Invoice(9, "Cartão Inter", 405.73), Invoice(10, "Nubank", 99.9));
        Accounts(new AccountRef(1, "Conta Inter", "Conta Corrente"));
        PaymentSucceeds();
        var responder = InvoiceResponder();

        var first = await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        first!.ShouldStartWith("✅ *Todas as 1 contas confirmadas!*\n\n• Aluguel");
        first.ShouldContain("💳 *Pagar a fatura Cartão Inter (R$ 405,73)?* (1/2)");
        var second = await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);
        second!.ShouldStartWith("✅ Fatura Cartão Inter paga: R$ 405,73 (Conta Inter).");
        second.ShouldContain("💳 *Pagar a fatura Nubank (R$ 99,90)?* (2/2)");
        (await responder.RespondAsync(Number, "pular", Id, CancellationToken.None)).ShouldBe("⏭️ A fatura Nubank continua em aberto.");
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task ChoosingOnlyTheBillNumber_DoesNotTouchTheInvoice_Deviation264()
    {
        await SeedInvoicesAsync([Bill(1, "Aluguel", due: Today)], Invoice());

        var reply = await InvoiceResponder().RespondAsync(Number, "1", Id, CancellationToken.None);

        reply.ShouldBe("✅ *Todas as 1 contas confirmadas!*\n\n• Aluguel\n\n💚 Transações registradas com sucesso!");
        _posting.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task NoPayingAccount_SaysSo_AndOnlySkipWorks_Deviation264()
    {
        await SeedInvoicesAsync([], Invoice());
        Accounts(new AccountRef(3, "Cartão Inter", "Cartão de Crédito"));

        (await InvoiceResponder().RespondAsync(Number, "ok", Id, CancellationToken.None))!
            .ShouldBe("💳 *Pagar a fatura Cartão Inter (R$ 405,73)?*\nNão encontrei uma conta ativa (que não seja cartão) para pagar. Responda \"pular\".");
    }

    private CheckinResponder Responder() =>
        new(new CheckinDialogStore(_cache, new CheckinSessionState(_cache)), new DelegateExecutor(this), InvoicePayer.None(new FinanceClock(Today)), new FinanceClock(Today), _diagnostics);

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

    [Fact]
    [Trait("AC", "012-404")]
    public async Task Ok_SendsTheSessionDueDate_AsTheCompetence_Deviation253()
    {
        await SeedAsync(Bill(1, "Água", due: Today.AddDays(-20)), Bill(2, "Sem data", day: 6));

        await Responder().RespondAsync(Number, "ok", Id, CancellationToken.None);

        _requests.Select(r => (r.ScheduleId, r.Competence)).ShouldBe([(1, (DateOnly?)Today.AddDays(-20)), (2, null)]);
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task VariableReminder_AfterOk_AsksTheValue_AndPostsTheAnsweredAmount_Deviation252()
    {
        await SeedAsync(Bill(1, "Aluguel", due: Today), Bill(2, "Conta de Água", due: Today, kind: "LEMBRETE_VARIAVEL"));
        var responder = Responder();

        var first = await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        first.ShouldBe(
            "✅ *1 conta(s) confirmada(s):*\n\n• Aluguel\n\n💚 Transações registradas com sucesso!\n\n"
            + "💬 *Qual o valor de Conta de Água?*\nResponda o valor (ex: 150,50), \"ok\" para usar o previsto (R$ 100,00) ou \"pular\".");
        _requests.Select(r => r.ScheduleId).ShouldBe([1]);
        (await _cache.ExistsAsync(CacheKeys.NightlyCheckinActive(Number))).ShouldBeTrue();

        var second = await responder.RespondAsync(Number, "R$ 44,27", Id, CancellationToken.None);

        second.ShouldBe("✅ Conta de Água lançada: R$ 44,27");
        _requests.Select(r => (r.ScheduleId, r.Amount)).ShouldBe([(1, (decimal?)null), (2, 44.27m)]);
        _requests[1].AllowVariableReminder.ShouldBeTrue();
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task VariableReminders_AreAskedOneByOne_OkUsesTheExpectedAmount_AndSkipPostsNothing()
    {
        await SeedAsync(
            Bill(1, "Água", due: Today, kind: "LEMBRETE_VARIAVEL"), Bill(2, "Luz", due: Today, kind: "LEMBRETE_VARIAVEL"));
        var responder = Responder();

        (await responder.RespondAsync(Number, "ok", Id, CancellationToken.None))!.ShouldContain("*Qual o valor de Água?* (1/2)");
        _requests.ShouldBeEmpty();

        var afterFirst = await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);
        afterFirst.ShouldBe(
            "✅ Água lançada: R$ 100,00\n\n💬 *Qual o valor de Luz?* (2/2)\nResponda o valor (ex: 150,50), \"ok\" para usar o previsto (R$ 100,00) ou \"pular\".");

        (await responder.RespondAsync(Number, "pular", Id, CancellationToken.None)).ShouldBe("⏭️ Luz não foi lançada.");

        _requests.Select(r => (r.ScheduleId, r.Amount)).ShouldBe([(1, (decimal?)100m)]);
        (await SessionEndedAsync()).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "012-404")]
    public async Task VariableReminder_UnrecognizedValue_RepeatsTheQuestionWithoutPosting()
    {
        await SeedAsync(Bill(1, "Água", due: Today, kind: "LEMBRETE_VARIAVEL"));
        var responder = Responder();
        await responder.RespondAsync(Number, "ok", Id, CancellationToken.None);

        var reply = await responder.RespondAsync(Number, "sei lá", Id, CancellationToken.None);

        reply!.ShouldStartWith(CheckinReplyTexts.ValueInvalid);
        reply.ShouldContain("*Qual o valor de Água?*");
        _requests.ShouldBeEmpty();
        (await SessionEndedAsync()).ShouldBeFalse();
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
