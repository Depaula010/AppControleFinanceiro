using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Transactions;
using NSubstitute;
using static MeuSecretario.UnitTests.Application.Transactions.FakeTransactionsSession;

namespace MeuSecretario.UnitTests.Application.Transactions;

public class InvoiceServiceTests : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await _session.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private const int User = FakeTransactionsSession.UserId;

    private readonly FakeTransactionsSession _session = new();
    private readonly IInvoiceSchedulePopulator _populator = Substitute.For<IInvoiceSchedulePopulator>();
    private readonly ITransactionsDiagnostics _diagnostics = Substitute.For<ITransactionsDiagnostics>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly InvoiceService _service;

    public InvoiceServiceTests()
    {
        _clock.Today.Returns(Day("2026-03-20"));
        _service = new InvoiceService(new FakeUnitOfWork(_session), _populator, _clock, _diagnostics);
        _session.AddAccount(3, "Nubank", "Cartão de Crédito", 10, 15);
    }

    [Fact]
    [Trait("AC", "004-95")]
    public async Task CreatesTheOpenInvoiceOfTodaysCycle_AndPopulatesItAfterTheCommit()
    {
        var committedWhenPopulated = false;
        _populator.PopulateAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                committedWhenPopulated = _session.Committed;
                return Task.CompletedTask;
            });

        await _service.EnsureCurrentAsync(User, null, CancellationToken.None);

        var invoice = _session.Invoices.Single();
        invoice.Due.ShouldBe(Day("2026-04-15"));
        invoice.Closing.ShouldBe(Day("2026-04-10"));
        invoice.Status.ShouldBe("Aberta");
        await _populator.Received(1).PopulateAsync(invoice.Id, 3, Day("2026-04-15"), Arg.Any<CancellationToken>());
        committedWhenPopulated.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-95")]
    public async Task ExistingOpenInvoice_IsNotRecreatedNorPopulated()
    {
        _session.AddInvoice(3, "2026-04-15", "2026-04-10");

        await _service.EnsureCurrentAsync(User, 3, CancellationToken.None);

        _session.Invoices.Count.ShouldBe(1);
        await _populator.DidNotReceiveWithAnyArgs().PopulateAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "004-95")]
    public async Task AClosedInvoiceWithTheSameDueDate_IsNotReplaced()
    {
        _session.AddInvoice(3, "2026-04-15", "2026-04-10", "Fechada");

        await _service.EnsureCurrentAsync(User, null, CancellationToken.None);

        _session.Invoices.Single().Status.ShouldBe("Fechada");
        await _populator.DidNotReceiveWithAnyArgs().PopulateAsync(default, default, default, default);
    }

    [Fact]
    [Trait("AC", "004-95")]
    public async Task PopulatorFailure_IsSwallowed_AndOnlyTheTypeIsReported()
    {
        _populator.PopulateAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("segredo na mensagem"));

        await _service.EnsureCurrentAsync(User, null, CancellationToken.None);

        _diagnostics.Received(1).PopulateFailed(Arg.Any<int>(), "InvalidOperationException");
        _session.Committed.ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "004-141")]
    public async Task OtherUsersCard_AndCardsWithoutDays_AreIgnored()
    {
        _session.AddAccount(5, "Alheio", "Cartão de Crédito", 10, 15, userId: 77);
        _session.AddAccount(6, "Sem dias", "Cartão de Crédito");
        _session.Invoices.Clear();

        await _service.EnsureCurrentAsync(User, null, CancellationToken.None);

        _session.Invoices.Select(i => i.CardId).ShouldBe([3]);
    }

    [Fact]
    [Trait("AC", "004-141")]
    public async Task InformedCard_RestrictsTheScan()
    {
        _session.AddAccount(8, "Segundo", "Cartão de Crédito", 5, 20);

        await _service.EnsureCurrentAsync(User, 8, CancellationToken.None);

        _session.Invoices.Select(i => i.CardId).ShouldBe([8]);
    }

    [Fact]
    [Trait("AC", "004-95")]
    public async Task NullPopulator_DoesNothing()
    {
        await new NullInvoiceSchedulePopulator().PopulateAsync(1, 2, Day("2026-04-15"), CancellationToken.None);
    }
}
