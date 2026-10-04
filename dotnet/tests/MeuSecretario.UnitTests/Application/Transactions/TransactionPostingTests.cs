using MeuSecretario.Application.Categories;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Transactions;
using NSubstitute;
using static MeuSecretario.UnitTests.Application.Transactions.FakeTransactionsSession;

namespace MeuSecretario.UnitTests.Application.Transactions;

public class TransactionPostingTests : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private const int User = FakeTransactionsSession.UserId;
    private static readonly DateOnly Date = new(2026, 3, 20);

    private readonly FakeTransactionsSession _session = new();
    private readonly ICategoryLookup _categories = Substitute.For<ICategoryLookup>();
    private readonly FakeUnitOfWork _unitOfWork;
    private readonly TransactionPosting _posting;

    public TransactionPostingTests()
    {
        _unitOfWork = new FakeUnitOfWork(_session);
        _posting = new TransactionPosting(_unitOfWork, _categories);
        _categories.FindShortTermInvestmentAsync(User, Arg.Any<CancellationToken>()).Returns(11);
        _categories.FindLoanPrincipalAsync(User, Arg.Any<CancellationToken>()).Returns(12);
        _session.AddAccount(1, "Carteira", "Dinheiro").AddAccount(2, "Conta Corrente").AddAccount(3, "Nubank", AccountCard, 10, 15);
    }

    private const string AccountCard = "Cartão de Crédito";

    private static PostSimpleTransaction Simple(
        TransactionKind kind = TransactionKind.Expense, decimal amount = 50m, int account = 2, int subcategory = 7,
        string? notes = null, bool consolidated = true, string? origin = null, int? schedule = null, DateOnly? date = null) =>
        new(User, account, subcategory, kind, amount, "Mercado", date ?? Date, notes, consolidated, origin, schedule);

    private static T Ok<T>(PostingResult<T> result) => result.ShouldBeOfType<PostingResult<T>.Ok>().Value;

    private static PostingError Failure<T>(PostingResult<T> result) => result.ShouldBeOfType<PostingResult<T>.Failed>().Error;

    private void NothingWritten()
    {
        _session.Inserted.ShouldBeEmpty();
        _session.Invoices.ShouldBeEmpty();
        _session.Links.ShouldBeEmpty();
        _session.Committed.ShouldBeFalse();
    }

    // ---------- simples ----------

    [Fact]
    [Trait("AC", "004-110")]
    public async Task Expense_IsStoredNegative_AsDespesa_WithoutInvoice_AndCommitted()
    {
        var posted = Ok(await _posting.PostSimpleAsync(Simple(origin: "bot"), CancellationToken.None));

        var row = _session.Inserted.ShouldHaveSingleItem();
        row.Amount.ShouldBe(-50m);
        row.DbType.ShouldBe("Despesa");
        row.InvoiceId.ShouldBeNull();
        row.Origin.ShouldBe("bot");
        row.Consolidated.ShouldBeTrue();
        row.UserId.ShouldBe(User);
        posted.StoredAmount.ShouldBe(-50m);
        posted.InvoiceId.ShouldBeNull();
        _session.Committed.ShouldBeTrue();
        _session.Disposed.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-111")]
    public async Task Income_IsStoredPositive_AsRenda()
    {
        Ok(await _posting.PostSimpleAsync(Simple(TransactionKind.Income, 3000m), CancellationToken.None));

        _session.Inserted.Single().Amount.ShouldBe(3000m);
        _session.Inserted.Single().DbType.ShouldBe("Renda");
    }

    [Fact]
    [Trait("AC", "004-112")]
    public async Task ExpenseOnCard_LinksOrCreatesTheCycleInvoice_IncomeDoesNot()
    {
        var posted = Ok(await _posting.PostSimpleAsync(Simple(account: 3), CancellationToken.None));

        posted.InvoiceDueDate.ShouldBe(Day("2026-04-15"));
        posted.InvoiceId.ShouldBe(_session.Invoices.Single().Id);
        _session.Invoices.Single().Closing.ShouldBe(Day("2026-04-10"));
        _session.Inserted.Single().InvoiceId.ShouldBe(posted.InvoiceId);

        var income = Ok(await _posting.PostSimpleAsync(Simple(TransactionKind.Income, account: 3), CancellationToken.None));
        income.InvoiceId.ShouldBeNull();
        _session.Invoices.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-112")]
    public async Task SecondPurchaseInTheSameCycle_ReusesTheInvoice()
    {
        var first = Ok(await _posting.PostSimpleAsync(Simple(account: 3), CancellationToken.None));
        var second = Ok(await _posting.PostSimpleAsync(Simple(account: 3, date: new DateOnly(2026, 3, 25)), CancellationToken.None));

        second.InvoiceId.ShouldBe(first.InvoiceId);
        _session.Invoices.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-24")]
    public async Task ExpenseOnCardWithoutDays_AndOnNonCard_HaveNoInvoice()
    {
        _session.AddAccount(4, "Cartão sem dias", AccountCard);

        Ok(await _posting.PostSimpleAsync(Simple(account: 4), CancellationToken.None)).InvoiceId.ShouldBeNull();
        Ok(await _posting.PostSimpleAsync(Simple(account: 1), CancellationToken.None)).InvoiceId.ShouldBeNull();
        _session.Invoices.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task InvalidAmount_FailsBeforeOpeningTheSession()
    {
        Failure(await _posting.PostSimpleAsync(Simple(amount: -0.01m), CancellationToken.None)).ShouldBe(PostingError.AmountInvalid);

        _unitOfWork.Begins.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task UnknownAccount_FailsFirst_ThenSubcategory_AndNothingIsWritten()
    {
        Failure(await _posting.PostSimpleAsync(Simple(account: 99, subcategory: 999), CancellationToken.None))
            .ShouldBe(PostingError.AccountNotFound);
        _session.Calls.ShouldNotContain(call => call.StartsWith("subcategory"));
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task OtherUsersAccount_IsNotFound()
    {
        _session.AddAccount(8, "Alheia", userId: 77);

        Failure(await _posting.PostSimpleAsync(Simple(account: 8), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-113")]
    public async Task InvalidSubcategory_FailsBeforeTheInvoiceIsCreated()
    {
        Failure(await _posting.PostSimpleAsync(Simple(account: 3, subcategory: 999), CancellationToken.None))
            .ShouldBe(PostingError.SubcategoryNotFound);

        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-114")]
    public async Task NotesConsolidatedAndSchedule_ArePassedThrough()
    {
        Ok(await _posting.PostSimpleAsync(Simple(notes: "obs", consolidated: false, schedule: 5), CancellationToken.None));

        var row = _session.Inserted.Single();
        row.Notes.ShouldBe("obs");
        row.Consolidated.ShouldBeFalse();
        row.ScheduleId.ShouldBe(5);
    }

    [Fact]
    [Trait("AC", "004-25")]
    public async Task FailureAfterTheInvoiceWasCreated_NeverCommits()
    {
        _session.FailOnWrite = 2;   // 1 = fatura, 2 = transação

        await Should.ThrowAsync<InvalidOperationException>(() => _posting.PostSimpleAsync(Simple(account: 3), CancellationToken.None));

        _session.Committed.ShouldBeFalse();
        _session.Disposed.ShouldBeTrue();
    }

    // ---------- transferência ----------

    private static PostTransfer Transfer(int from = 1, int to = 2, decimal amount = 100m) => new(User, from, to, amount, Date, "bot");

    [Fact]
    [Trait("AC", "004-115")]
    public async Task Transfer_WritesTheTwoLinkedLegs()
    {
        var posted = Ok(await _posting.PostTransferAsync(Transfer(), CancellationToken.None));

        _session.Inserted.Count.ShouldBe(2);
        var (outgoing, incoming) = (_session.Inserted[0], _session.Inserted[1]);
        outgoing.AccountId.ShouldBe(1);
        outgoing.Amount.ShouldBe(-100m);
        outgoing.Description.ShouldBe("Transferência para Conta Corrente");
        incoming.AccountId.ShouldBe(2);
        incoming.Amount.ShouldBe(100m);
        incoming.Description.ShouldBe("Transferência de Carteira");
        new[] { outgoing, incoming }.ShouldAllBe(r => r.DbType == "Transferência" && r.SubcategoryId == 11 && r.InvoiceId == null && r.Consolidated && r.Origin == "bot");
        _session.Links.ShouldBe([(500, 501)]);
        posted.ShouldBe(new PostedTransfer(500, 501, "Carteira", "Conta Corrente"));
        _session.Committed.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-116")]
    public async Task Transfer_Errors()
    {
        Failure(await _posting.PostTransferAsync(Transfer(1, 1), CancellationToken.None)).ShouldBe(PostingError.SameAccount);
        Failure(await _posting.PostTransferAsync(Transfer(99, 2), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        Failure(await _posting.PostTransferAsync(Transfer(1, 99), CancellationToken.None)).ShouldBe(PostingError.DestinationAccountNotFound);
        Failure(await _posting.PostTransferAsync(Transfer(amount: -1m), CancellationToken.None)).ShouldBe(PostingError.AmountInvalid);
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-116")]
    public async Task Transfer_WithoutTheCategory_FailsWithItsName()
    {
        _categories.FindShortTermInvestmentAsync(User, Arg.Any<CancellationToken>()).Returns((int?)null);

        var failed = (await _posting.PostTransferAsync(Transfer(), CancellationToken.None)).ShouldBeOfType<PostingResult<PostedTransfer>.Failed>();

        failed.Error.ShouldBe(PostingError.RequiredCategoryMissing);
        failed.Detail.ShouldBe("Investimentos de Curto Prazo");
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-117")]
    public async Task Transfer_WhenLinkingFails_NeverCommits()
    {
        _session.FailOnWrite = 3;   // 1 e 2 = pernas, 3 = ligação

        await Should.ThrowAsync<InvalidOperationException>(() => _posting.PostTransferAsync(Transfer(), CancellationToken.None));

        _session.Committed.ShouldBeFalse();
    }

    // ---------- pagamento de fatura ----------

    private static PostInvoicePayment Payment(int from = 2, int card = 3, decimal amount = 800m, string date = "2026-03-20") =>
        new(User, from, card, amount, Day(date));

    [Fact]
    [Trait("AC", "004-118")]
    public async Task Payment_PaysTheOldestClosedInvoice_NotTheOpenOne()
    {
        var older = _session.AddInvoice(3, "2026-02-10", "2026-02-05", "Fechada");
        _session.AddInvoice(3, "2026-03-10", "2026-03-05", "Fechada");
        var open = _session.AddInvoice(3, "2026-04-10", "2026-04-05");

        var posted = Ok(await _posting.PostInvoicePaymentAsync(Payment(), CancellationToken.None));

        posted.InvoiceId.ShouldBe(older.Id);
        posted.InvoiceDueDate.ShouldBe(Day("2026-02-10"));
        posted.InvoiceMarkedPaid.ShouldBeTrue();
        older.Status.ShouldBe("Paga");
        open.Status.ShouldBe("Aberta");
        _session.Inserted.ShouldAllBe(r => r.InvoiceId == older.Id);
    }

    [Fact]
    [Trait("AC", "004-119")]
    public async Task Payment_WithoutClosedInvoice_UsesTheCycleInvoice_CreatingIt()
    {
        var posted = Ok(await _posting.PostInvoicePaymentAsync(Payment(), CancellationToken.None));

        var invoice = _session.Invoices.Single();
        posted.InvoiceId.ShouldBe(invoice.Id);
        invoice.Status.ShouldBe("Paga");
        invoice.Due.ShouldBe(Day("2026-04-15"));
    }

    [Fact]
    [Trait("AC", "004-121")]
    public async Task Payment_WritesTheTwoLegsWithTheLiteralDescriptions()
    {
        Ok(await _posting.PostInvoicePaymentAsync(Payment(), CancellationToken.None));

        var (outgoing, incoming) = (_session.Inserted[0], _session.Inserted[1]);
        outgoing.AccountId.ShouldBe(2);
        outgoing.Amount.ShouldBe(-800m);
        outgoing.Description.ShouldBe("Pagamento Fatura Nubank");
        incoming.AccountId.ShouldBe(3);
        incoming.Amount.ShouldBe(800m);
        incoming.Description.ShouldBe("Pagamento Recebido (de Conta Corrente)");
        new[] { outgoing, incoming }.ShouldAllBe(r => r.DbType == "Pagamento Fatura" && r.SubcategoryId == 12 && r.Consolidated);
        _session.Links.ShouldBe([(500, 501)]);
    }

    [Fact]
    [Trait("AC", "004-120")]
    public async Task Payment_OfAnyAmount_MarksTheInvoicePaid()
    {
        var invoice = _session.AddInvoice(3, "2026-03-10", "2026-03-05", "Fechada");

        Ok(await _posting.PostInvoicePaymentAsync(Payment(amount: 0.01m), CancellationToken.None));

        invoice.Status.ShouldBe("Paga");
    }

    [Fact]
    [Trait("AC", "004-123")]
    public async Task Payment_ForACardWithoutDays_AndWithoutClosedInvoice_HasNoInvoice()
    {
        _session.AddAccount(4, "Sem dias", AccountCard);

        var posted = Ok(await _posting.PostInvoicePaymentAsync(Payment(card: 4), CancellationToken.None));

        posted.InvoiceId.ShouldBeNull();
        posted.InvoiceMarkedPaid.ShouldBeFalse();
        _session.Inserted.ShouldAllBe(r => r.InvoiceId == null);
        _session.PaidInvoices.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "004-122")]
    public async Task Payment_Errors()
    {
        Failure(await _posting.PostInvoicePaymentAsync(Payment(3, 3), CancellationToken.None)).ShouldBe(PostingError.SameAccount);
        Failure(await _posting.PostInvoicePaymentAsync(Payment(99, 3), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        Failure(await _posting.PostInvoicePaymentAsync(Payment(2, 99), CancellationToken.None)).ShouldBe(PostingError.DestinationAccountNotFound);
        Failure(await _posting.PostInvoicePaymentAsync(Payment(2, 1), CancellationToken.None)).ShouldBe(PostingError.NotACreditCard);
        Failure(await _posting.PostInvoicePaymentAsync(Payment(amount: -1m), CancellationToken.None)).ShouldBe(PostingError.AmountInvalid);
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-122")]
    public async Task Payment_WithoutTheCategory_FailsAndMarksNothing()
    {
        _categories.FindLoanPrincipalAsync(User, Arg.Any<CancellationToken>()).Returns((int?)null);
        var closed = _session.AddInvoice(3, "2026-03-10", "2026-03-05", "Fechada");

        var failed = (await _posting.PostInvoicePaymentAsync(Payment(), CancellationToken.None)).ShouldBeOfType<PostingResult<PostedInvoicePayment>.Failed>();

        failed.Error.ShouldBe(PostingError.RequiredCategoryMissing);
        failed.Detail.ShouldBe("Quitação de Empréstimos (Principal)");
        closed.Status.ShouldBe("Fechada");
        _session.Committed.ShouldBeFalse();
    }

    // ---------- compra parcelada ----------

    private static PostInstallmentPurchase Installments(int total = 3, int account = 3, int subcategory = 7, string date = "2026-01-31") =>
        new(User, account, subcategory, 33.33m, total, "Geladeira", Day(date), "bot");

    [Fact]
    [Trait("AC", "004-130")]
    public async Task Installments_PostTheFirstAndScheduleTheRest()
    {
        var posted = Ok(await _posting.PostInstallmentPurchaseAsync(Installments(), CancellationToken.None));

        var row = _session.Inserted.Single();
        row.Amount.ShouldBe(-33.33m);
        row.Description.ShouldBe("Geladeira");
        row.Date.ShouldBe(Day("2026-01-31"));
        row.ScheduleId.ShouldBeNull();
        row.InvoiceId.ShouldBe(_session.Invoices.Single().Id);

        var schedule = _session.Schedules.Single();
        schedule.Amount.ShouldBe(33.33m);
        schedule.StartDate.ShouldBe(Day("2026-02-28"));
        schedule.DayOfMonth.ShouldBe(31);
        schedule.Total.ShouldBe(3);
        schedule.AccountId.ShouldBe(3);
        schedule.SubcategoryId.ShouldBe(7);
        posted.ShouldBe(new PostedInstallmentPurchase(500, 901, row.InvoiceId));
        _session.Committed.ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "004-131")]
    [InlineData(0)]
    [InlineData(1)]
    public async Task FewerThanTwoInstallments_IsInvalid(int total)
    {
        Failure(await _posting.PostInstallmentPurchaseAsync(Installments(total), CancellationToken.None)).ShouldBe(PostingError.InstallmentsInvalid);

        NothingWritten();
        _session.Schedules.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "004-131")]
    public async Task Installments_UseTheSimpleErrors()
    {
        Failure(await _posting.PostInstallmentPurchaseAsync(Installments(account: 99), CancellationToken.None)).ShouldBe(PostingError.AccountNotFound);
        Failure(await _posting.PostInstallmentPurchaseAsync(Installments(subcategory: 999), CancellationToken.None)).ShouldBe(PostingError.SubcategoryNotFound);
        NothingWritten();
    }

    [Fact]
    [Trait("AC", "004-132")]
    public async Task Installments_WhenTheScheduleFails_NeverCommits()
    {
        _session.FailOnWrite = 3;   // fatura, transação, agendamento

        await Should.ThrowAsync<InvalidOperationException>(() => _posting.PostInstallmentPurchaseAsync(Installments(), CancellationToken.None));

        _session.Committed.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "004-133")]
    public async Task Installments_OnANonCard_HaveNoInvoice_ButTheScheduleIsCreated()
    {
        Ok(await _posting.PostInstallmentPurchaseAsync(Installments(account: 2), CancellationToken.None));

        _session.Inserted.Single().InvoiceId.ShouldBeNull();
        _session.Schedules.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "004-134")]
    public async Task Installments_StartOnTheNextMonth()
    {
        Ok(await _posting.PostInstallmentPurchaseAsync(Installments(date: "2026-03-15"), CancellationToken.None));

        _session.Schedules.Single().StartDate.ShouldBe(Day("2026-04-15"));
        _session.Schedules.Single().DayOfMonth.ShouldBe(15);
    }
}
