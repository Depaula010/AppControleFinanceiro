using MeuSecretario.Application.Jobs;
using MeuSecretario.Domain.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class CheckinQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JobsEnv _env = new(postgres);
    private CheckinQueries _queries = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _queries = new CheckinQueries(postgres.Connections);
        _user = await _env.UserAsync();
        _account = await _env.AccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<CheckinSnapshot> SnapshotAsync(string today) => _queries.GetSnapshotAsync(_user, JobsEnv.Day(today), CancellationToken.None);

    [Theory]
    [Trait("AC", "013-41")]
    [InlineData(18, true)]
    [InlineData(17, true)]
    [InlineData(11, true)]
    [InlineData(10, false)]
    [InlineData(19, false)]
    public async Task Pending_IsDueTodayOrUpToSevenDaysLate(int day, bool pending)
    {
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Conta", day: day);

        var snapshot = await SnapshotAsync("2026-03-18");

        snapshot.PendingBills.Count.ShouldBe(pending ? 1 : 0);
        if (pending)
        {
            snapshot.PendingBills[0].DueDate.ShouldBe(JobsEnv.Day($"2026-03-{day}"));
        }
        else if (day < 18)
        {
            snapshot.OverdueBills.Single().DueDate.ShouldBe(JobsEnv.Day($"2026-03-{day:00}"));
        }
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task PaidInTheSameMonthByScheduleId_Disappears()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Conta", day: 17);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-01", "qualquer", scheduleId: id);

        var snapshot = await SnapshotAsync("2026-03-18");

        snapshot.PendingBills.ShouldBeEmpty();
        snapshot.OverdueBills.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task LatePaymentUpToTwentyDaysAfterTheDueDate_CountsAsPaid_AcrossMonths()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Kotas", day: 28);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-02", "Kotas", scheduleId: id);

        (await SnapshotAsync("2026-03-03")).PendingBills.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task PaymentOfAnEarlierMonth_DoesNotBlockTheCurrentOne()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Conta", day: 15);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-02-10", "Conta", scheduleId: id);

        (await SnapshotAsync("2026-03-18")).PendingBills.Single().Id.ShouldBe(id);
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task PreviousMonthDay31InShortMonth_FallsOnTheLastDay()
    {
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Fim do mês", day: 31);

        var bill = (await SnapshotAsync("2026-03-01")).PendingBills.Single();

        bill.DueDate.ShouldBe(JobsEnv.Day("2026-02-28"));
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task Annual_OnlyAppearsInItsMonth_AndNeverFromThePreviousMonth()
    {
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "IPTU", periodicity: "ANUAL", month: 3, day: 17);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "IPVA", periodicity: "ANUAL", month: 4, day: 17);

        (await SnapshotAsync("2026-03-18")).PendingBills.Select(b => b.Description).ShouldBe(["IPTU"]);
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task Overdue_ComesFromBeyondSevenDays_AndIsDeduplicatedAgainstPending()
    {
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Velha", day: 3);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Recente", day: 17);

        var snapshot = await SnapshotAsync("2026-03-18");

        snapshot.PendingBills.Select(b => b.Description).ShouldBe(["Recente"]);
        snapshot.OverdueBills.Select(b => b.Description).ShouldBe(["Velha"]);
        snapshot.OverdueBills[0].DueDate.ShouldBe(JobsEnv.Day("2026-03-03"));
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task Overdue_ExcludesFixedCardExpenseOfGroupDespesaWithBillingDays()
    {
        var card = await _env.CardAsync(_user);
        await _env.ScheduleAsync(_user, card, "Despesa", "Streaming", day: 3);
        await _env.ScheduleAsync(_user, card, "Renda", "Cashback", day: 3);

        var snapshot = await SnapshotAsync("2026-03-18");

        snapshot.OverdueBills.Select(b => b.Description).ShouldBe(["Cashback"]);
    }

    [Fact]
    [Trait("AC", "013-42")]
    public async Task OldTransactionWithoutScheduleId_PaysOnlyWithSameAccountAndDescription_Deviation012()
    {
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 17);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-17", "Aluguel");
        var other = await _env.AccountAsync(_user, "Outra");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Internet", day: 17);
        await _env.TransactionAsync(_user, other, "Despesa Essencial", "2026-03-17", "Internet");

        (await SnapshotAsync("2026-03-18")).PendingBills.Select(b => b.Description).ShouldBe(["Internet"]);
    }

    [Fact]
    [Trait("AC", "013-43")]
    public async Task Invoices_OverdueAndDueToday_OnlyOpenOnes_WithTheAbsoluteExpenseTotal()
    {
        var card = await _env.CardAsync(_user);
        var overdue = await _env.InvoiceAsync(card, "2026-03-10", "2026-03-01");
        await _env.TransactionAsync(_user, card, "Despesa Essencial", "2026-02-20", "compra", -100m, invoiceId: overdue);
        await _env.TransactionAsync(_user, card, "Despesa Essencial", "2026-02-21", "estorno", 30m, invoiceId: overdue);
        await _env.InvoiceAsync(card, "2026-03-18", "2026-03-08");
        await _env.InvoiceAsync(card, "2026-03-05", "2026-02-25", "Fechada");
        await _env.InvoiceAsync(card, "2026-01-01", "2025-12-20");

        var snapshot = await SnapshotAsync("2026-03-18");

        snapshot.OverdueInvoices.Select(i => (i.Card, i.DueDate, i.Total)).ShouldBe([("Nubank", JobsEnv.Day("2026-03-10"), 100m)]);
        snapshot.InvoicesDueToday.Select(i => (i.AccountName, i.Total)).ShouldBe([("Nubank", 0m)]);
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task OtherUsersData_IsNeverReturned()
    {
        var stranger = await _env.UserAsync("5511900000002", "Bia");
        var account = await _env.AccountAsync(stranger, "Conta B");
        await _env.ScheduleAsync(stranger, account, "Despesa Essencial", "Dela", day: 18);

        (await SnapshotAsync("2026-03-18")).IsEmpty.ShouldBeTrue();
    }
}

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class NotificationQueriesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JobsEnv _env = new(postgres);
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _user = await _env.UserAsync();
        _account = await _env.AccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "013-20")]
    public async Task MorningUsers_MatchTheExactHour_WithSummaryOrAlertsActive()
    {
        var repository = new JobNotificationConfigRepository(postgres.Connections);
        await _env.NotificationConfigAsync(_user, summary: true, summaryHour: "07:00", alerts: false);
        var onlyAlerts = await _env.UserAsync("5511900000002", "Bia");
        await _env.NotificationConfigAsync(onlyAlerts, summary: false, summaryHour: "07:00", alerts: true);
        var neither = await _env.UserAsync("5511900000003", "Caio");
        await _env.NotificationConfigAsync(neither, summary: false, summaryHour: "07:00", alerts: false);
        var half = await _env.UserAsync("5511900000004", "Duda");
        await _env.NotificationConfigAsync(half, summary: true, summaryHour: "07:30");

        var users = await repository.ListMorningUsersAsync(new TimeOnly(7, 0), CancellationToken.None);

        users.Select(u => u.UserId).ShouldBe([_user, onlyAlerts]);
        (await repository.ListMorningUsersAsync(new TimeOnly(7, 30), CancellationToken.None)).Select(u => u.UserId).ShouldBe([half]);
        (await repository.ListMorningUsersAsync(new TimeOnly(8, 0), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-40")]
    public async Task CheckinUsers_MatchTheExactHourAndActiveFlag()
    {
        var repository = new JobNotificationConfigRepository(postgres.Connections);
        await _env.NotificationConfigAsync(_user, checkin: true, checkinHour: "20:00");
        var off = await _env.UserAsync("5511900000002", "Bia");
        await _env.NotificationConfigAsync(off, checkin: false, checkinHour: "20:00");

        (await repository.ListCheckinUsersAsync(new TimeOnly(20, 0), CancellationToken.None)).Select(u => u.UserId).ShouldBe([_user]);
        (await repository.ListCheckinUsersAsync(new TimeOnly(21, 0), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-32")]
    public async Task BillsReminderUsers_UseTheFiveMinuteWindowOverTheSummaryHour()
    {
        var repository = new JobNotificationConfigRepository(postgres.Connections);
        await _env.NotificationConfigAsync(_user, summaryHour: "07:03", alerts: true);
        var outside = await _env.UserAsync("5511900000002", "Bia");
        await _env.NotificationConfigAsync(outside, summaryHour: "07:10", alerts: true);

        var users = await repository.ListBillsReminderUsersAsync(new TimeOnly(6, 58), new TimeOnly(7, 8), CancellationToken.None);

        users.Select(u => (u.UserId, u.Name)).ShouldBe([(_user, "Ana")]);
        (await repository.ListBillsReminderUsersAsync(new TimeOnly(23, 58), new TimeOnly(0, 3), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-20")]
    public async Task GetOrCreate_InsertsTheDefaults_AndIsIdempotent()
    {
        var repository = new JobNotificationConfigRepository(postgres.Connections);

        var first = await repository.GetOrCreateAsync(_user, CancellationToken.None);
        var second = await repository.GetOrCreateAsync(_user, CancellationToken.None);

        first.ShouldBe(new UserNotificationConfig(true, true));
        second.ShouldBe(first);
        (await _env.ScalarAsync<int>("SELECT count(*)::int FROM notificationconfigs")).ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "013-30")]
    public async Task FinancialAlerts_SplitTodayAndTomorrowAcrossTheMonthBoundary()
    {
        var repository = new FinancialAlertsRepository(postgres.Connections);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Fim", 100m, day: 31);
        await _env.ScheduleAsync(_user, _account, "Renda", "Salário", 5000m, day: 1);

        var alerts = await repository.GetUpcomingAsync(_user, JobsEnv.Day("2026-12-31"), CancellationToken.None);

        alerts.BillsToday.Select(b => (b.Description, b.Type, b.Value)).ShouldBe([("Fim", "Despesa", 100d)]);
        alerts.BillsTomorrow.Select(b => (b.Description, b.Type)).ShouldBe([("Salário", "Receita")]);
    }

    [Fact]
    [Trait("AC", "013-30")]
    public async Task FinancialAlerts_SkipLaunchedByScheduleIdOrByDescriptionWithoutId_AndFilterAnnualByStartDate()
    {
        var repository = new FinancialAlertsRepository(postgres.Connections);
        var linked = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Vinculada", day: 18);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-02", "outro nome", scheduleId: linked);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Antiga", day: 18);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-03", "Antiga");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aberta", day: 18);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual certa", periodicity: "ANUAL", month: 3, day: 18, startDate: "2025-03-18");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Anual errada", periodicity: "ANUAL", month: 5, day: 18, startDate: "2025-05-18");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Parcelada", kind: "PARCELADO", total: 3, day: 18);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Inativa", day: 18, active: false);

        var alerts = await repository.GetUpcomingAsync(_user, JobsEnv.Day("2026-03-18"), CancellationToken.None);

        alerts.BillsToday.Select(b => b.Description).ShouldBe(["Aberta", "Anual certa"]);
    }

    [Fact]
    [Trait("AC", "013-30")]
    public async Task FinancialAlerts_InvoicesOfTodayAndTomorrow_OnlyOpen_WithTheExpenseTotal()
    {
        var repository = new FinancialAlertsRepository(postgres.Connections);
        var card = await _env.CardAsync(_user);
        var today = await _env.InvoiceAsync(card, "2026-03-18", "2026-03-08");
        await _env.TransactionAsync(_user, card, "Despesa Essencial", "2026-03-01", "compra", -250.5m, invoiceId: today);
        await _env.InvoiceAsync(card, "2026-03-19", "2026-03-09");
        var other = await _env.CardAsync(_user, "Inter");
        await _env.InvoiceAsync(other, "2026-03-18", "2026-03-08", "Fechada");

        var alerts = await repository.GetUpcomingAsync(_user, JobsEnv.Day("2026-03-18"), CancellationToken.None);

        alerts.InvoicesToday.Select(i => (i.Card, i.Value)).ShouldBe([("Nubank", 250.5)]);
        alerts.InvoicesTomorrow.Select(i => i.Card).ShouldBe(["Nubank"]);
    }

    [Fact]
    [Trait("AC", "013-32")]
    public async Task BillsReminder_ListsTomorrowsBillsWithoutALaunchThisMonth()
    {
        var repository = new BillsReminderRepository(postgres.Connections);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", 1500m, day: 19);
        await _env.ScheduleAsync(_user, _account, "Renda", "Salário", 5000m, day: 19);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Lançada", 10m, day: 19);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-05", "Lançada");
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Outro dia", 10m, day: 20);

        var bills = await repository.ListAsync(_user, JobsEnv.Day("2026-03-19"), JobsEnv.Day("2026-03-18"), CancellationToken.None);

        bills.Select(b => (b.Description, b.Value, b.GroupName)).ShouldBe([("Aluguel", 1500d, "Despesa Essencial"), ("Salário", 5000d, "Renda")]);
    }
}

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class InvoiceJobRepositoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly JobsEnv _env = new(postgres);
    private InvoiceJobRepository _repository = default!;
    private int _user;
    private int _card;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _repository = new InvoiceJobRepository(postgres.Connections);
        _user = await _env.UserAsync();
        _card = await _env.CardAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "013-50")]
    public async Task Close_ClosesOnlyOpenInvoicesWhoseClosingDatePassed_AndReturnsTheTotals()
    {
        var expired = await _env.InvoiceAsync(_card, "2026-03-15", "2026-03-10");
        await _env.TransactionAsync(_user, _card, "Despesa Essencial", "2026-03-02", "compra", -80m, invoiceId: expired);
        await _env.TransactionAsync(_user, _card, "Despesa Essencial", "2026-03-03", "estorno", 20m, invoiceId: expired);
        var closesToday = await _env.InvoiceAsync(_card, "2026-04-15", "2026-03-18");
        var already = await _env.InvoiceAsync(_card, "2026-02-15", "2026-02-10", "Fechada");
        await _env.ExecuteAsync("UPDATE faturas SET updated_at = '2020-01-01' WHERE id = @expired", new { expired });

        var closed = await _repository.CloseExpiredAsync(JobsEnv.Day("2026-03-18"), CancellationToken.None);

        closed.Select(c => (c.Id, c.AccountName, c.Total, c.UserId, c.WhatsappNumber)).ShouldBe([(expired, "Nubank", 80d, _user, "5511900000001")]);
        (await _env.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @expired", new { expired })).ShouldBe("Fechada");
        (await _env.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @closesToday", new { closesToday })).ShouldBe("Aberta");
        (await _env.ScalarAsync<string>("SELECT status FROM faturas WHERE id = @already", new { already })).ShouldBe("Fechada");
        (await _env.ScalarAsync<bool>("SELECT updated_at > now() - interval '1 minute' FROM faturas WHERE id = @expired", new { expired })).ShouldBeTrue();
        (await _repository.CloseExpiredAsync(JobsEnv.Day("2026-03-18"), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "013-55")]
    public async Task ListOpen_ReturnsEveryOpenInvoiceOrderedByDueDate()
    {
        var late = await _env.InvoiceAsync(_card, "2026-05-15", "2026-05-10");
        var soon = await _env.InvoiceAsync(_card, "2026-04-15", "2026-04-10");
        await _env.InvoiceAsync(_card, "2026-03-15", "2026-03-10", "Paga");

        (await _repository.ListOpenAsync(CancellationToken.None)).Select(i => i.Id).ShouldBe([soon, late]);
    }

    [Fact]
    [Trait("AC", "013-53")]
    public async Task ListDueSoon_OnlyClosedInvoicesDueOnTheTargetDate()
    {
        var target = await _env.InvoiceAsync(_card, "2026-03-21", "2026-03-10", "Fechada");
        await _env.InvoiceAsync(_card, "2026-03-22", "2026-03-11", "Fechada");
        var other = await _env.CardAsync(_user, "Inter");
        await _env.InvoiceAsync(other, "2026-03-21", "2026-03-10", "Aberta");

        var due = await _repository.ListDueSoonAsync(JobsEnv.Day("2026-03-21"), 3, CancellationToken.None);

        due.Select(i => (i.Id, i.DaysUntilDue, i.AccountName)).ShouldBe([(target, 3, "Nubank")]);
    }
}
