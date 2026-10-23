using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Jobs;

/// <summary>
/// Desvio 258: o banco recusa dois lançamentos do mesmo vencimento (<c>competencia</c>) do mesmo agendamento, e a migration 0090 não
/// derruba o deploy se já houver duplicata.
/// </summary>
[Trait("Category", "Docker")]
public sealed class CompetenceUniqueIndexTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Index = "ux_transacoes_agendamento_competencia";

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

    private Task<int> LaunchAsync(int scheduleId, string date, string? competence) =>
        _env.TransactionAsync(_user, _account, "Despesa Essencial", date, "Conta", -10m, scheduleId: scheduleId, competence: competence);

    [Fact]
    [Trait("AC", "013-66")]
    public async Task TwoLaunchesOfTheSameDueDate_AreRefusedByTheDatabase()
    {
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 5);
        await LaunchAsync(id, "2026-10-05", "2026-10-05");

        var ex = await Should.ThrowAsync<PostgresException>(() => LaunchAsync(id, "2026-10-06", "2026-10-05"));

        (ex.SqlState, ex.ConstraintName).ShouldBe((PostgresErrorCodes.UniqueViolation, Index));
    }

    [Fact]
    [Trait("AC", "013-66")]
    public async Task WhatTheIndexMustAllow_IsAllowed()
    {
        var a = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 5);
        var b = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Luz", day: 6);

        await LaunchAsync(a, "2026-10-05", "2026-10-05");
        await LaunchAsync(a, "2026-11-05", "2026-11-05");   // outro vencimento do mesmo agendamento
        await LaunchAsync(b, "2026-10-05", "2026-10-05");   // mesmo vencimento, outro agendamento
        await LaunchAsync(a, "2026-10-07", null);           // lançamento antigo, sem competência
        await LaunchAsync(a, "2026-10-08", null);
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-05", "Avulso", -5m);   // sem agendamento
        await _env.TransactionAsync(_user, _account, "Despesa Essencial", "2026-10-05", "Avulso", -5m);

        (await _env.ScalarAsync<int>("SELECT count(*) FROM transacoes")).ShouldBe(7);
    }

    [Fact]
    [Trait("AC", "013-66")]
    public async Task Migration_WithExistingDuplicates_WarnsAndDoesNotFail_ThenCreatesTheIndexOnceCleaned()
    {
        var script = MigrationRunner.ReadScript("0090_transacoes_competencia_unica");
        var id = await _env.ScheduleAsync(_user, _account, "Despesa Essencial", "Aluguel", day: 5);
        try
        {
            await _env.ExecuteAsync($"DROP INDEX {Index}");
            await LaunchAsync(id, "2026-10-05", "2026-10-05");
            var duplicate = await LaunchAsync(id, "2026-10-06", "2026-10-05");

            await _env.ExecuteAsync(script);   // não lança: só avisa

            (await IndexExistsAsync()).ShouldBeFalse();

            await _env.ExecuteAsync("DELETE FROM transacoes WHERE id = @duplicate", new { duplicate });
            await _env.ExecuteAsync(script);
            await _env.ExecuteAsync(script);   // idempotente

            (await IndexExistsAsync()).ShouldBeTrue();
        }
        finally
        {
            await _env.ExecuteAsync("DELETE FROM transacoes WHERE agendamento_id = @id", new { id });
            await _env.ExecuteAsync(script);   // deixa o banco compartilhado com o índice, aconteça o que acontecer
        }
    }

    private async Task<bool> IndexExistsAsync() =>
        await _env.ScalarAsync<int>("SELECT count(*) FROM pg_indexes WHERE indexname = @name", new { name = Index }) == 1;
}
