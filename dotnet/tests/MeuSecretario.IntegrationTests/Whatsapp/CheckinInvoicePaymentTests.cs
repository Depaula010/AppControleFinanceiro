using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Jobs;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.Infrastructure.Categories;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.Infrastructure.Transactions;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.IntegrationTests.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>
/// Desvio 264 com o banco de verdade: a fatura vencida aparece numerada no check-in, o "ok" pergunta a conta e o pagamento grava o par
/// <c>Pagamento Fatura</c> e marca a fatura como <c>Paga</c> (o caso de 25/10/2026: "Cartão Inter - R$ 405,73 - Venceu em 20/10").
/// </summary>
[Trait("Category", "Docker")]
public sealed class CheckinInvoicePaymentTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Number = "5531999990001";
    private static readonly DateOnly Today = new(2026, 10, 25);

    private readonly PatternCache _cache = new();
    private JobsEnv _env = default!;
    private NightlyCheckinService _checkin = default!;
    private CheckinResponder _responder = default!;
    private int _user;
    private int _checking;
    private int _card;
    private int _invoice;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new JobsEnv(postgres);
        _user = await _env.UserAsync(Number);
        _checking = await _env.AccountAsync(_user, "Conta Inter");
        _card = await _env.CardAsync(_user, "Cartão Inter", closing: 13, due: 20);
        _invoice = await _env.InvoiceAsync(_card, "2026-10-20", "2026-10-13", "Fechada");
        await _env.TransactionAsync(_user, _card, "Despesa Essencial", "2026-10-05", "Compra", -405.73m, invoiceId: _invoice);

        var clock = new FixedClock(Today);
        var session = new CheckinSessionState(_cache);
        _checkin = new NightlyCheckinService(new CheckinQueries(postgres.Connections), _cache, clock);
        _responder = new CheckinResponder(
            new CheckinDialogStore(_cache, session),
            new ScheduleExecutor(new ScheduleExecutionRepository(postgres.Connections)),
            new CheckinInvoicePayer(
                new AutomationLookups(postgres.Connections),
                new TransactionPosting(new TransactionsUnitOfWork(postgres.Connections), new CategoryRepository(postgres.Connections)), clock),
            clock,
            new NoDiagnostics());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<string> StartCheckinAsync()
    {
        var snapshot = await _checkin.CollectSnapshotAsync(_user, Today, CancellationToken.None);
        var id = NightlyCheckinService.NewCheckinId();
        var message = _checkin.FormatConsolidatedMessage(snapshot, id)!;
        await _checkin.CreateSessionAsync(
            Number, [.. snapshot.PendingBills, .. snapshot.OverdueBills], CancellationToken.None, id, CheckinInvoiceItem.FromSnapshot(snapshot, _user));
        message.ShouldContain("🔴 *FATURAS VENCIDAS:*\n1. Cartão Inter - R$ 405,73 - Venceu em 20/10/2026");
        message.ShouldContain("🔹 *COMO RESPONDER:*");
        return id;
    }

    [Fact]
    public async Task Ok_AsksTheAccount_ThenPaysTheInvoice_WritesThePair_AndMarksItPaid()
    {
        var id = await StartCheckinAsync();

        var question = await _responder.RespondAsync(Number, "ok", id, CancellationToken.None);
        var reply = await _responder.RespondAsync(Number, "ok", id, CancellationToken.None);

        question.ShouldBe("💳 *Pagar a fatura Cartão Inter (R$ 405,73)?*\nResponda \"ok\" para pagar com Conta Inter ou \"pular\".");
        reply.ShouldBe("✅ Fatura Cartão Inter paga: R$ 405,73 (Conta Inter).");

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<string>("SELECT status FROM faturas WHERE id = @id", new { id = _invoice })).ShouldBe("Paga");
        var payments = (await connection.QueryAsync<(int Account, decimal Amount)>(
            "SELECT conta_id AS Account, valor AS Amount FROM transacoes WHERE tipo_transacao = 'Pagamento Fatura' ORDER BY valor")).ToList();
        payments.ShouldBe([(_checking, -405.73m), (_card, 405.73m)]);
    }

    [Fact]
    public async Task PaidInvoice_DoesNotAppearInTheNextCheckin()
    {
        var id = await StartCheckinAsync();
        await _responder.RespondAsync(Number, "ok", id, CancellationToken.None);
        await _responder.RespondAsync(Number, "ok", id, CancellationToken.None);

        var snapshot = await _checkin.CollectSnapshotAsync(_user, Today, CancellationToken.None);

        snapshot.OverdueInvoices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Skip_LeavesTheInvoiceOpen()
    {
        var id = await StartCheckinAsync();
        await _responder.RespondAsync(Number, "ok", id, CancellationToken.None);

        (await _responder.RespondAsync(Number, "pular", id, CancellationToken.None)).ShouldBe("⏭️ A fatura Cartão Inter continua em aberto.");

        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<string>("SELECT status FROM faturas WHERE id = @id", new { id = _invoice })).ShouldBe("Fechada");
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM transacoes WHERE tipo_transacao = 'Pagamento Fatura'")).ShouldBe(0);
    }

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateTimeOffset UtcNow => new(today.ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero);

        public DateTime Now => today.ToDateTime(new TimeOnly(12, 0));

        public DateOnly Today => today;

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;
    }

    private sealed class NoDiagnostics : ICheckinDiagnostics
    {
        public void ReplyFailed(string errorType)
        {
        }
    }
}
