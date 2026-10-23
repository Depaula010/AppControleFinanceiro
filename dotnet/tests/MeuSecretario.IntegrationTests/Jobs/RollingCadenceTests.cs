using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>
/// Desvio 255: Gasolina semanal e Terapia quinzenal (dados reais de 2026-10) vencem a cada 7 e 14 dias do último lançamento, no check-in,
/// no motor e na idempotência do executor.
/// </summary>
[Trait("Category", "Docker")]
public sealed class RollingCadenceTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly JobsEnv _env = new(postgres);
    private CheckinQueries _queries = default!;
    private ScheduleExecutionRepository _executor = default!;
    private int _user;
    private int _account;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _queries = new CheckinQueries(postgres.Connections);
        _executor = new ScheduleExecutionRepository(postgres.Connections);
        _user = await _env.UserAsync();
        _account = await _env.AccountAsync(_user);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<DateOnly?> OfferedDueAsync(int id, string today)
    {
        var snapshot = await _queries.GetSnapshotAsync(_user, JobsEnv.Day(today), CancellationToken.None);
        return snapshot.PendingBills.Concat(snapshot.OverdueBills).SingleOrDefault(b => b.Id == id)?.DueDate;
    }

    private Task<ScheduleExecutionOutcome> PostAsync(int id, string date, string? competence = null) => _executor.ExecuteAsync(
        new ScheduleExecutionRequest(id, JobsEnv.Day(date), AllowVariableReminder: true, Competence: competence is null ? null : JobsEnv.Day(competence)),
        CancellationToken.None);

    [Fact]
    [Trait("AC", "013-41")]
    public async Task WeeklyReminder_IsOfferedSevenDaysAfterTheLastLaunch_AndRollsForwardFromTheCompetence()
    {
        // Lançamento antigo (sem competência) de 19/10: o próximo vencimento é 26/10.
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Gasolina Semanal", 100m, "LEMBRETE_VARIAVEL", "SEMANAL", day: 5, startDate: "2025-01-01");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-19", "Gasolina Semanal", -100m, scheduleId: id);

        (await OfferedDueAsync(id, "2026-10-25")).ShouldBeNull();
        (await OfferedDueAsync(id, "2026-10-26")).ShouldBe(JobsEnv.Day("2026-10-26"));
        (await OfferedDueAsync(id, "2026-10-28")).ShouldBe(JobsEnv.Day("2026-10-26"));

        // Confirmado com atraso (lançado em 28/10, competência 26/10): a grade segue de 26/10, então o próximo é 02/11.
        (await PostAsync(id, "2026-10-28", competence: "2026-10-26")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();

        (await OfferedDueAsync(id, "2026-10-29")).ShouldBeNull();
        (await OfferedDueAsync(id, "2026-11-01")).ShouldBeNull();
        (await OfferedDueAsync(id, "2026-11-02")).ShouldBe(JobsEnv.Day("2026-11-02"));
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task OldLaunchOfTheSameMonth_DoesNotMarkARollingDueDateAsPaid()
    {
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Gasolina Semanal", 100m, "LEMBRETE_VARIAVEL", "SEMANAL", day: 5, startDate: "2025-01-01");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-19", "Gasolina Semanal", -100m, scheduleId: id);

        // Pela regra mensal, o lançamento de 19/10 "quitaria" outubro inteiro e o vencimento de 26/10 daria Skipped.
        (await PostAsync(id, "2026-10-26")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        (await PostAsync(id, "2026-10-26")).ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
    }

    [Fact]
    [Trait("AC", "013-41")]
    public async Task ScheduleNeverLaunched_StartsAtItsStartDate()
    {
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Terapia", 50m, "FIXO", "QUINZENAL", day: 20, startDate: "2026-10-10");

        (await OfferedDueAsync(id, "2026-10-09")).ShouldBeNull();
        (await OfferedDueAsync(id, "2026-10-12")).ShouldBe(JobsEnv.Day("2026-10-10"));
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task BiweeklyFixed_IsAMotorCandidateOnlyOnItsNextDue_NotOnTheDayOfTheMonth()
    {
        // Terapia: último lançamento em 20/10, dia_execucao 20. Próximo vencimento: 03/11 (e não 20/11).
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Terapia", 50m, "FIXO", "QUINZENAL", day: 20, startDate: "2026-02-20");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-20", "Terapia", -50m, scheduleId: id);

        async Task<bool> IsCandidateAsync(string date) =>
            (await _executor.ListMotorCandidatesAsync(JobsEnv.Day(date), CancellationToken.None)).Any(c => c.Id == id);

        (await IsCandidateAsync("2026-11-02")).ShouldBeFalse();
        (await IsCandidateAsync("2026-11-03")).ShouldBeTrue();
        (await IsCandidateAsync("2026-11-20")).ShouldBeFalse();

        // O motor lança no dia (competência = hoje) e o próximo cai 14 dias depois.
        (await PostAsync(id, "2026-11-03")).ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        (await IsCandidateAsync("2026-11-03")).ShouldBeFalse();
        (await IsCandidateAsync("2026-11-17")).ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task WeeklyVariableReminder_CarriesItsNextDueToTheMotor()
    {
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Gasolina Semanal", 100m, "LEMBRETE_VARIAVEL", "SEMANAL", day: 5, startDate: "2025-01-01");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-19", "Gasolina Semanal", -100m, scheduleId: id);

        var candidates = await _executor.ListMotorCandidatesAsync(JobsEnv.Day("2026-10-24"), CancellationToken.None);

        candidates.Single(c => c.Id == id).NextDue.ShouldBe(JobsEnv.Day("2026-10-26"));
    }
}
