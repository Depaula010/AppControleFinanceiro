using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class ScheduleExecutionRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JobsEnv _env = new(postgres);
    private ScheduleExecutionRepository _repository = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _repository = new ScheduleExecutionRepository(postgres.Connections);
        _user = await _env.UserAsync();
        _account = await _env.AccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<ScheduleExecutionOutcome> RunAsync(int scheduleId, string date, int? invoiceId = null, bool allowVariableReminder = false) =>
        _repository.ExecuteAsync(new ScheduleExecutionRequest(scheduleId, JobsEnv.Day(date), invoiceId, allowVariableReminder), CancellationToken.None);

    private sealed class Tx
    {
        public int Id { get; set; }

        public string Descricao { get; set; } = string.Empty;

        public decimal Valor { get; set; }

        public string Tipo { get; set; } = string.Empty;

        public int? AgendamentoId { get; set; }

        public int? FaturaId { get; set; }

        public DateTime Data { get; set; }
    }

    private Task<IReadOnlyList<Tx>> TransactionsAsync() => _env.QueryAsync<Tx>(
        "SELECT id AS Id, descricao AS Descricao, valor AS Valor, tipo_transacao AS Tipo, agendamento_id AS AgendamentoId, fatura_id AS FaturaId, data_transacao AS Data FROM transacoes ORDER BY id");

    [Fact]
    [Trait("AC", "013-61")]
    public async Task Fixed_Expense_IsNegative_LinkedToTheSchedule_OnTheGivenDate()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", 1500m, day: 18);

        var outcome = await RunAsync(id, "2026-03-18");

        outcome.ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        var tx = (await TransactionsAsync()).Single();
        (tx.Descricao, tx.Valor, tx.Tipo, tx.AgendamentoId, tx.FaturaId, tx.Data).ShouldBe(("Aluguel", -1500m, "Despesa", id, null, JobsEnv.D("2026-03-18")));
    }

    [Fact]
    [Trait("AC", "013-61")]
    public async Task Fixed_Income_IsPositive_AndNullAmountIsZero()
    {
        var income = await _env.ScheduleAsync(_user, _account, "Renda", "Salário", 5000m, day: 18);
        var empty = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Sem valor", null, day: 18);

        await RunAsync(income, "2026-03-18");
        await RunAsync(empty, "2026-03-18");

        (await TransactionsAsync()).Select(t => (t.Descricao, t.Valor, t.Tipo)).ShouldBe([("Salário", 5000m, "Renda"), ("Sem valor", 0m, "Despesa")]);
    }

    [Fact]
    [Trait("AC", "013-61")]
    public async Task Card_LinksTheInvoiceOfTheCycleOfTheDate_CreatingItWhenMissing()
    {
        var card = await _env.CardAsync(_user, closing: 10, due: 15);
        var id = await _env.ScheduleAsync(_user, card, "Despesa Essencial", "Streaming", 40m, day: 18);

        var outcome = (ScheduleExecutionOutcome.Posted)await RunAsync(id, "2026-03-18");

        outcome.InvoiceId.ShouldNotBeNull();
        (await _env.ScalarAsync<string>("SELECT to_char(data_vencimento, 'YYYY-MM-DD') || ' ' || status FROM faturas WHERE id = @id", new { id = outcome.InvoiceId }))
            .ShouldBe("2026-04-15 Aberta");
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-64")]
    public async Task SecondRunInTheSameMonth_IsSkipped_WithoutDuplicatingOrAdvancing()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Curso", 100m, "PARCELADO", day: 18, total: 5, executed: 1);

        var first = await RunAsync(id, "2026-03-18");
        var second = await RunAsync(id, "2026-03-18");

        first.ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        second.ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
        (await TransactionsAsync()).Count.ShouldBe(1);
        (await _env.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE id = @id", new { id })).ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "013-64")]
    public async Task ATransactionOfTheSameScheduleInTheMonth_BlocksEvenOnAnotherDay()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 18);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-05", "Aluguel", scheduleId: id);

        (await RunAsync(id, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
        (await RunAsync(id, "2026-04-18")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
    }

    [Fact]
    [Trait("AC", "013-64")]
    public async Task OldTransactionWithoutScheduleId_BlocksOnlyWithSameAccountDescriptionAndMonth()
    {
        var other = await _env.AccountAsync(_user, "Outra");
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 18);
        await _env.TransactionAsync(_user, other, "Despesa Essencial", "2026-03-10", "Aluguel");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-18", "Aluguel");
        (await RunAsync(id, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();

        var blocked = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Internet", day: 18);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-02", "Internet");

        (await RunAsync(blocked, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
    }

    [Fact]
    [Trait("AC", "013-62")]
    public async Task Installments_RunToTheEnd_DeactivateWithGreaterOrEqual_AndStop()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Curso", 200m, "PARCELADO", day: 18, total: 2, executed: 0);

        await RunAsync(id, "2026-03-18");
        await RunAsync(id, "2026-04-18");
        var third = await RunAsync(id, "2026-05-18");

        (await TransactionsAsync()).Select(t => t.Descricao).ShouldBe(["Curso (1/2)", "Curso (2/2)"]);
        (await _env.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @id", new { id })).ShouldBeFalse();
        third.ShouldBeOfType<ScheduleExecutionOutcome.NotEligible>();
    }

    [Fact]
    [Trait("AC", "013-62")]
    public async Task Installments_WithoutTotal_NeverDeactivate_AndOverrunDeactivates()
    {
        var open = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Sem fim", 10m, "PARCELADO", day: 18, total: null, executed: 3);
        var overrun = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Passou", 10m, "PARCELADO", day: 18, total: 3, executed: 2);

        await RunAsync(open, "2026-03-18");
        await RunAsync(overrun, "2026-03-18");

        (await _env.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @id", new { id = open })).ShouldBeTrue();
        (await _env.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE id = @id", new { id = open })).ShouldBe(4);
        (await _env.ScalarAsync<bool>("SELECT ativo FROM agendamentos WHERE id = @id", new { id = overrun })).ShouldBeFalse();
    }

    [Fact]
    [Trait("AC", "013-54")]
    public async Task GivenInvoice_IsUsed_AndBlocksADuplicateOfTheSameInvoice()
    {
        var card = await _env.CardAsync(_user);
        var invoice = await _env.InvoiceAsync(card, "2026-04-15", "2026-04-10");
        var id = await _env.ScheduleAsync(_user, card, "Despesa Essencial", "Streaming", 40m, day: 5);

        var first = (ScheduleExecutionOutcome.Posted)await RunAsync(id, "2026-04-05", invoice);
        var again = await RunAsync(id, "2026-04-05", invoice);

        first.InvoiceId.ShouldBe(invoice);
        again.ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM faturas")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-65")]
    public async Task NotEligible_ForInactiveReminderOrMissing()
    {
        var inactive = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", active: false);
        var reminder = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", kind: "LEMBRETE_VARIAVEL");

        (await RunAsync(inactive, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.NotEligible>();
        (await RunAsync(reminder, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.NotEligible>();
        (await RunAsync(9999, "2026-03-18")).ShouldBeOfType<ScheduleExecutionOutcome.NotEligible>();
        (await TransactionsAsync()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-65")]
    public async Task VariableReminder_ConfirmedByTheUser_PostsTheExpectedAmount_OnlyOncePerMonth()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Conta de Água", 44.27m, "LEMBRETE_VARIAVEL", day: 27);

        (await RunAsync(id, "2026-10-19", allowVariableReminder: true)).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        (await RunAsync(id, "2026-10-19", allowVariableReminder: true)).ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();

        var tx = (await TransactionsAsync()).Single();
        (tx.Descricao, tx.Valor, tx.AgendamentoId).ShouldBe(("Conta de Água", -44.27m, id));
    }

    [Fact]
    [Trait("AC", "013-66")]
    public async Task ParallelRuns_PostExactlyOnce()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Curso", 200m, "PARCELADO", day: 18, total: 9, executed: 0);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => RunAsync(id, "2026-03-18"))));

        results.Count(r => r is ScheduleExecutionOutcome.Posted).ShouldBe(1);
        (await TransactionsAsync()).Count.ShouldBe(1);
        (await _env.ScalarAsync<int>("SELECT parcelas_executadas FROM agendamentos WHERE id = @id", new { id })).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Candidates_AreFixedAndInstallmentsOfTheDay_AnnualOnlyInTheMonth_PlusEveryActiveReminder()
    {
        var fixedToday = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "A", day: 18);
        var installmentToday = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "B", kind: "PARCELADO", day: 18, total: 3);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Outro dia", day: 19);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Inativo", day: 18, active: false);
        var annualRight = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual certo", periodicity: "ANUAL", month: 3, day: 18);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual errado", periodicity: "ANUAL", month: 4, day: 18);
        var reminder = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Lembrete", kind: "LEMBRETE_VARIAVEL", day: 25);

        var candidates = await _repository.ListMotorCandidatesAsync(JobsEnv.Day("2026-03-18"), CancellationToken.None);

        candidates.Select(c => c.Id).ShouldBe([fixedToday, installmentToday, annualRight, reminder]);
        candidates[^1].WhatsappNumber.ShouldBe("5511900000001");
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Candidates_OnTheLastDayOfAShortMonth_IncludeDay29To31_ButNotBefore()
    {
        var day31 = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Dia 31", day: 31);
        var day30 = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Dia 30", kind: "PARCELADO", day: 30, total: 3);
        var day29Annual = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual 29", periodicity: "ANUAL", month: 4, day: 29);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual de outro mês", periodicity: "ANUAL", month: 5, day: 31);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Dia 10", day: 10);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Inativo 31", day: 31, active: false);

        var lastDay = await _repository.ListMotorCandidatesAsync(JobsEnv.Day("2026-04-30"), CancellationToken.None);
        var dayBefore = await _repository.ListMotorCandidatesAsync(JobsEnv.Day("2026-04-29"), CancellationToken.None);

        lastDay.Select(c => c.Id).ShouldBe([day31, day30]);
        dayBefore.Select(c => c.Id).ShouldBe([day29Annual]);
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Day31Schedule_PostedAtTheEndOfAprilAndNotAgainTheSameMonth()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Condomínio", 800m, day: 31);

        (await RunAsync(id, "2026-04-30")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        (await RunAsync(id, "2026-04-30")).ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();

        (await TransactionsAsync()).Count.ShouldBe(1);
    }
}
