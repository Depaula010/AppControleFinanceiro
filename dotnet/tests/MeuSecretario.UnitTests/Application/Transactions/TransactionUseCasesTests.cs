using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.UnitTests.Domain.Accounts;
using MeuSecretario.UnitTests.Domain.Transactions;
using NSubstitute;
using static MeuSecretario.UnitTests.Application.Transactions.FakeTransactionsSession;

namespace MeuSecretario.UnitTests.Application.Transactions;

public class ListTransactionsTests
{
    [Fact]
    [Trait("AC", "004-01")]
    public async Task DelegatesToTheRepository_WithTheTokenUser()
    {
        var repository = Substitute.For<ITransactionRepository>();
        var filter = new ListFilter(20, 0, null, null, null);
        var page = new TransactionPage(0, []);
        repository.ListAsync(42, filter, Arg.Any<CancellationToken>()).Returns(page);

        (await new ListTransactions(repository).ExecuteAsync(42, filter, CancellationToken.None)).ShouldBe(page);
    }
}

public class CreateTransactionTests
{
    private readonly ITransactionPosting _posting = Substitute.For<ITransactionPosting>();
    private readonly CreateTransaction _useCase;

    public CreateTransactionTests()
    {
        _useCase = new CreateTransaction(_posting);
    }

    private void PostingReturns(PostingResult<PostedTransaction> result) =>
        _posting.PostSimpleAsync(Arg.Any<PostSimpleTransaction>(), Arg.Any<CancellationToken>()).Returns(result);

    [Fact]
    [Trait("AC", "004-10")]
    public async Task ValidBody_PostsASimpleTransaction_WithOriginApiAndTheTokenUser()
    {
        PostingReturns(new PostingResult<PostedTransaction>.Ok(new PostedTransaction(9, -150.5m, null, null)));
        var body = Tx.Valid(("usuario_id", Raw.N(99)), ("observacoes", Raw.T(" nota ")), ("consolidada", Tx.B(false)));

        var result = await _useCase.ExecuteAsync(42, body, CancellationToken.None);

        var created = result.ShouldBeOfType<CreateTransactionResult.Created>();
        created.Posted.TransactionId.ShouldBe(9);
        created.Input.DateText.ShouldBe("2025-12-11");
        await _posting.Received(1).PostSimpleAsync(
            new PostSimpleTransaction(42, 3, 7, TransactionKind.Expense, 150.5m, "Mercado", new DateOnly(2025, 12, 11), "nota", false, "api", null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "004-13")]
    public async Task InvalidBody_NeverReachesThePostingService()
    {
        var result = await _useCase.ExecuteAsync(42, null, CancellationToken.None);

        result.ShouldBeOfType<CreateTransactionResult.Invalid>().Reason.ShouldBe(TransactionError.DescriptionRequired);
        await _posting.DidNotReceiveWithAnyArgs().PostSimpleAsync(default!, default);
    }

    [Fact]
    [Trait("AC", "004-20")]
    public async Task AccountAndSubcategoryNotFound_AreMapped()
    {
        PostingReturns(new PostingResult<PostedTransaction>.Failed(PostingError.AccountNotFound));
        (await _useCase.ExecuteAsync(42, Tx.Valid(), CancellationToken.None)).ShouldBeOfType<CreateTransactionResult.AccountNotFound>();

        PostingReturns(new PostingResult<PostedTransaction>.Failed(PostingError.SubcategoryNotFound));
        (await _useCase.ExecuteAsync(42, Tx.Valid(), CancellationToken.None)).ShouldBeOfType<CreateTransactionResult.SubcategoryNotFound>();
    }

    [Fact]
    public async Task UnexpectedPostingError_Throws()
    {
        PostingReturns(new PostingResult<PostedTransaction>.Failed(PostingError.SameAccount));

        await Should.ThrowAsync<InvalidOperationException>(() => _useCase.ExecuteAsync(42, Tx.Valid(), CancellationToken.None));
    }
}

public class UpdateTransactionTests : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private const int User = 42;
    private const int Id = 9;

    private readonly ITransactionRepository _repository = Substitute.For<ITransactionRepository>();
    private readonly FakeTransactionsSession _session = new();
    private readonly UpdateTransaction _useCase;

    public UpdateTransactionTests()
    {
        _useCase = new UpdateTransaction(_repository, new FakeUnitOfWork(_session));
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>())
            .Returns(new CurrentTransaction(Id, "Despesa", -50m, 2, Day("2026-03-10")));
        _repository.SubcategoryVisibleAsync(User, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.ActiveAccountOwnedAsync(User, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.UpdateAsync(User, Id, Arg.Any<TransactionChanges>(), Arg.Any<CancellationToken>())
            .Returns(new UpdatedTransaction(Id, "Novo", -50m, "Despesa", Day("2026-03-10")));
        _session.AddAccount(2, "Corrente").AddAccount(3, "Nubank", "Cartão de Crédito", 10, 15);
    }

    private Task<UpdateTransactionResult> Run(RawBody? body) => _useCase.ExecuteAsync(User, Id, body, CancellationToken.None);

    private static TransactionError InvalidReason(UpdateTransactionResult result) =>
        result.ShouldBeOfType<UpdateTransactionResult.Invalid>().Reason;

    [Fact]
    [Trait("AC", "004-40")]
    public async Task EmptyBody_Is400_BeforeTheLookup()
    {
        InvalidReason(await Run(null)).ShouldBe(TransactionError.NoDataProvided);
        InvalidReason(await Run(Raw.Body())).ShouldBe(TransactionError.NoDataProvided);

        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }

    [Fact]
    [Trait("AC", "004-41")]
    public async Task UnknownTransaction_IsNotFound()
    {
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>()).Returns((CurrentTransaction?)null);

        (await Run(Raw.Body(("descricao", Raw.T("x"))))).ShouldBeOfType<UpdateTransactionResult.NotFound>();
    }

    [Theory]
    [Trait("AC", "004-85")]
    [InlineData("Transferência")]
    [InlineData("Pagamento Fatura")]
    public async Task TransferAndPayment_AreNotEditable_AfterTheNotFound(string type)
    {
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>()).Returns(new CurrentTransaction(Id, type, -50m, 2, Day("2026-03-10")));

        InvalidReason(await Run(Raw.Body(("descricao", Raw.T("x"))))).ShouldBe(TransactionError.NotEditable);
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "004-53")]
    public async Task WithTwoErrors_TheFirstOfTheOrderWins_AndNothingIsWritten()
    {
        _repository.ActiveAccountOwnedAsync(User, 5, Arg.Any<CancellationToken>()).Returns(false);

        InvalidReason(await Run(Raw.Body(("valor", Raw.T("abc")), ("conta_id", Raw.N(5))))).ShouldBe(TransactionError.ValueInvalid);

        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "004-49")]
    public async Task SubcategoryIsCheckedBeforeTheAccount()
    {
        _repository.SubcategoryVisibleAsync(User, 8, Arg.Any<CancellationToken>()).Returns(false);
        _repository.ActiveAccountOwnedAsync(User, 5, Arg.Any<CancellationToken>()).Returns(false);

        (await Run(Raw.Body(("subcategoria_id", Raw.N(8)), ("conta_id", Raw.N(5))))).ShouldBeOfType<UpdateTransactionResult.SubcategoryNotFound>();
        await _repository.DidNotReceiveWithAnyArgs().ActiveAccountOwnedAsync(default, default, default);

        _repository.SubcategoryVisibleAsync(User, 8, Arg.Any<CancellationToken>()).Returns(true);
        (await Run(Raw.Body(("subcategoria_id", Raw.N(8)), ("conta_id", Raw.N(5))))).ShouldBeOfType<UpdateTransactionResult.AccountNotFound>();
    }

    [Fact]
    [Trait("AC", "004-52")]
    public async Task NoValidField_IsRejected_AfterTheChecks()
    {
        InvalidReason(await Run(Raw.Body(("foo", Raw.N(1))))).ShouldBe(TransactionError.NoValidField);
        InvalidReason(await Run(Raw.Body(("descricao", Raw.T(""))))).ShouldBe(TransactionError.NoValidField);
    }

    [Fact]
    [Trait("AC", "004-50")]
    public async Task TailErrors_ComeAfterTheReferenceChecks()
    {
        _repository.ActiveAccountOwnedAsync(User, 5, Arg.Any<CancellationToken>()).Returns(false);

        (await Run(Raw.Body(("consolidada", Raw.T("x")), ("conta_id", Raw.N(5))))).ShouldBeOfType<UpdateTransactionResult.AccountNotFound>();
        InvalidReason(await Run(Raw.Body(("consolidada", Raw.T("x"))))).ShouldBe(TransactionError.ConsolidatedInvalid);
    }

    [Fact]
    [Trait("AC", "004-42")]
    public async Task PlainEdit_GoesThroughTheRepository_WithoutOpeningASession()
    {
        var result = await Run(Raw.Body(("descricao", Raw.T("Novo")), ("valor", Raw.N(180))));

        result.ShouldBeOfType<UpdateTransactionResult.Updated>().Transaction.Description.ShouldBe("Novo");
        await _repository.Received(1).UpdateAsync(
            User, Id, Arg.Is<TransactionChanges>(c => c.Description.Value == "Novo" && c.Amount.Value == -180m), Arg.Any<CancellationToken>());
        _session.Updated.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task ChangingTheAccountToACard_RelinksTheInvoice_InOneSession()
    {
        var result = await Run(Raw.Body(("conta_id", Raw.N(3))));

        result.ShouldBeOfType<UpdateTransactionResult.Updated>();
        var update = _session.Updated.ShouldNotBeNull();
        update.Invoice.IsSet.ShouldBeTrue();
        update.Invoice.Value.ShouldBe(_session.Invoices.Single().Id);
        _session.Invoices.Single().Due.ShouldBe(Day("2026-03-15"));   // D = 10/03 ≤ fechamento 10
        _session.Committed.ShouldBeTrue();
        await _repository.DidNotReceiveWithAnyArgs().UpdateAsync(default, default, default!, default);
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task MovingToANonCard_OrChangingToIncome_ClearsTheInvoice()
    {
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>()).Returns(new CurrentTransaction(Id, "Despesa", -50m, 3, Day("2026-03-10")));

        await Run(Raw.Body(("conta_id", Raw.N(2))));
        _session.Updated!.Value.Invoice.ShouldBe(Change.Set<int?>(null));

        await Run(Raw.Body(("tipo", Raw.T("Receita"))));
        _session.Updated!.Value.Invoice.ShouldBe(Change.Set<int?>(null));
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task ChangingTheDateOnACard_PicksTheNewCycle()
    {
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>()).Returns(new CurrentTransaction(Id, "Despesa", -50m, 3, Day("2026-03-10")));

        await Run(Raw.Body(("data", Raw.T("2026-03-11"))));

        _session.Invoices.Single().Due.ShouldBe(Day("2026-04-15"));
    }

    [Fact]
    [Trait("AC", "004-56")]
    public async Task InactiveCurrentAccount_KeepsTheInvoice_WhenOnlyTheDateChanges()
    {
        _repository.GetAsync(User, Id, Arg.Any<CancellationToken>()).Returns(new CurrentTransaction(Id, "Despesa", -50m, 77, Day("2026-03-10")));

        await Run(Raw.Body(("data", Raw.T("2026-03-11"))));

        _session.Updated!.Value.Invoice.IsSet.ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "004-41")]
    public async Task RowVanishedBeforeTheUpdate_IsNotFound_AndNothingIsCommitted()
    {
        _repository.UpdateAsync(User, Id, Arg.Any<TransactionChanges>(), Arg.Any<CancellationToken>()).Returns((UpdatedTransaction?)null);
        (await Run(Raw.Body(("descricao", Raw.T("x"))))).ShouldBeOfType<UpdateTransactionResult.NotFound>();

        _session.UpdateResult = null;
        (await Run(Raw.Body(("conta_id", Raw.N(3))))).ShouldBeOfType<UpdateTransactionResult.NotFound>();
        _session.Committed.ShouldBeFalse();
    }
}

public class DeleteTransactionTests
{
    [Fact]
    [Trait("AC", "004-70")]
    public async Task PassesTodayInSaoPauloToTheRepository()
    {
        var repository = Substitute.For<ITransactionRepository>();
        var clock = Substitute.For<IClock>();
        clock.Today.Returns(Day("2026-10-04"));
        repository.DeleteWithPairAsync(42, 9, Day("2026-10-04"), Arg.Any<CancellationToken>()).Returns(true);

        (await new DeleteTransaction(repository, clock).ExecuteAsync(42, 9, CancellationToken.None)).ShouldBeTrue();
        (await new DeleteTransaction(repository, clock).ExecuteAsync(42, 10, CancellationToken.None)).ShouldBeFalse();
    }
}
