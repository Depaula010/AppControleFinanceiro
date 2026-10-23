using MeuSecretario.Application.Jobs;
using MeuSecretario.Infrastructure.Jobs;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>
/// O check-in e o executor decidem com a mesma regra (<c>SchedulePaymentSql</c>, desvio 253): o que a consulta oferece o executor aceita, e
/// o que o executor lança some da consulta. Os bugs de 15 a 20/10/2026 eram essas duas regras em divergência.
/// </summary>
[Trait("Category", "Docker")]
public sealed class CheckinExecutorAgreementTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    // Dia 11 vence exatamente 7 dias antes de "hoje": é pendente, e o vencimento de fevereiro já saiu da janela de 30 dias.
    private const string Today = "2026-03-18";

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

    private async Task<bool> OfferedAsync(int id)
    {
        var snapshot = await _queries.GetSnapshotAsync(_user, JobsEnv.Day(Today), CancellationToken.None);
        return snapshot.PendingBills.Concat(snapshot.OverdueBills).Any(b => b.Id == id);
    }

    [Theory]
    [Trait("AC", "013-65")]
    [InlineData("FIXO", true, null, 0)]
    [InlineData("FIXO", false, null, 0)]
    [InlineData("LEMBRETE_VARIAVEL", true, null, 0)]
    [InlineData("LEMBRETE_VARIAVEL", false, null, 0)]
    [InlineData("PARCELADO", true, 3, 0)]
    [InlineData("PARCELADO", true, 3, 2)]
    [InlineData("PARCELADO", true, 3, 3)]
    [InlineData("PARCELADO", true, null, 5)]
    [InlineData("PARCELADO", false, 3, 0)]
    public async Task WhatTheCheckinOffers_IsExactlyWhatTheExecutorAccepts(string kind, bool active, int? total, int executed)
    {
        var id = await _env.ScheduleAsync(
            _user, _account, "Despesa Essencial", "Conta", 100m, kind, day: 11, total: total, executed: executed, active: active);

        var offered = await OfferedAsync(id);
        var outcome = await _executor.ExecuteAsync(
            new ScheduleExecutionRequest(id, JobsEnv.Day(Today), AllowVariableReminder: true), CancellationToken.None);

        offered.ShouldBe(outcome is not ScheduleExecutionOutcome.NotEligible, $"{kind} ativo={active} {executed}/{total}");
    }

    [Theory]
    [Trait("AC", "013-65")]
    [InlineData("FIXO")]
    [InlineData("LEMBRETE_VARIAVEL")]
    [InlineData("PARCELADO")]
    public async Task WhatTheExecutorPosts_StopsBeingOffered_AndIsNotPostedTwice(string kind)
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Conta", 100m, kind, day: 11, total: kind == "PARCELADO" ? 5 : null);
        (await OfferedAsync(id)).ShouldBeTrue();
        var due = new DateOnly(2026, 3, 11);

        var first = await _executor.ExecuteAsync(
            new ScheduleExecutionRequest(id, JobsEnv.Day(Today), AllowVariableReminder: true, Competence: due), CancellationToken.None);
        first.ShouldBeOfType<ScheduleExecutionOutcome.Posted>();

        (await OfferedAsync(id)).ShouldBeFalse();
        var second = await _executor.ExecuteAsync(
            new ScheduleExecutionRequest(id, JobsEnv.Day(Today), AllowVariableReminder: true, Competence: due), CancellationToken.None);
        second.ShouldBeOfType<ScheduleExecutionOutcome.Skipped>();
    }
}
