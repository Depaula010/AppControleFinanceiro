using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>
/// Desvio 262: o motor recupera, de forma idempotente, o que venceu nos últimos dias e não foi lançado (app fora do ar, job que falhou).
/// </summary>
[Trait("Category", "Docker")]
public sealed class MotorCatchUpTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
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

    private async Task<IReadOnlyList<int>> CandidatesAsync(string day) =>
        [.. (await _repository.ListCatchUpCandidatesAsync(JobsEnv.Day(day), CancellationToken.None)).Select(c => c.Id)];

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Candidates_AreWhatMotorWouldHaveRunThatDay_FixedAndInstallments_NotReminders_NotInactive_NotNotYetStarted()
    {
        var fixedDue = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 10);
        var installment = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Curso", 200m, "PARCELADO", day: 10, total: 5, executed: 1);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Lembrete", kind: "LEMBRETE_VARIAVEL", day: 10);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Inativo", day: 10, active: false);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Outro dia", day: 11);
        await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Ainda não existia", day: 10, startDate: "2026-03-15");

        (await CandidatesAsync("2026-03-10")).ShouldBe([fixedDue, installment]);
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task Candidates_IncludeTheLastDayRule_AndTheRollingNextDue()
    {
        var monthEnd = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Fim de mês", day: 31);
        var biweekly = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Terapia", 50m, "FIXO", "QUINZENAL", day: 20, startDate: "2026-02-20");
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-20", "Terapia", -50m, scheduleId: biweekly);

        (await CandidatesAsync("2026-04-30")).ShouldContain(monthEnd);   // 30/04 é o último dia de abril
        (await CandidatesAsync("2026-04-29")).ShouldNotContain(monthEnd);
        (await CandidatesAsync("2026-11-03")).ShouldContain(biweekly);    // 20/10 + 14 dias
        (await CandidatesAsync("2026-11-20")).ShouldNotContain(biweekly);
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task ARunThatMissedTheDay_PostsItOnTheDueDate_AndRepeatingTheRecoveryPostsNothingMore()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", 1500m, day: 10);
        var due = JobsEnv.Day("2026-03-10");

        var first = await _repository.ExecuteAsync(new ScheduleExecutionRequest(id, due), CancellationToken.None);
        var second = await _repository.ExecuteAsync(new ScheduleExecutionRequest(id, due), CancellationToken.None);

        first.ShouldBeOfType<ScheduleExecutionOutcome.Posted>();
        second.ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
        var rows = await _env.QueryAsync<DateTime>("SELECT data_transacao FROM transacoes WHERE agendamento_id = @id", new { id });
        rows.ShouldBe([JobsEnv.D("2026-03-10")]);
    }

    [Fact]
    [Trait("AC", "013-60")]
    public async Task WhatTheUserAlreadyRegisteredByHand_IsNotPostedAgain()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", 1500m, day: 10);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-03-11", "Aluguel", -1500m);   // avulso, mesma conta e descrição

        var outcome = await _repository.ExecuteAsync(new ScheduleExecutionRequest(id, JobsEnv.Day("2026-03-10")), CancellationToken.None);

        outcome.ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
    }
}
