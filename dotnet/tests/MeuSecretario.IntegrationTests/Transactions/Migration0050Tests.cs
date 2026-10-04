using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Transactions;

/// <summary>Migration 0050 (desvio 027): <c>observacoes</c> e <c>origem</c> em <c>transacoes</c>.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class Migration0050Tests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly TransactionsTestData _data = new(postgres.DataSource);

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "004-21")]
    public async Task Columns_Exist_WithTheExpectedTypes()
    {
        var columns = await _data.QueryAsync<(string Name, string Type, int? Length, string Nullable)>(
            """
            SELECT column_name, data_type, character_maximum_length, is_nullable FROM information_schema.columns
            WHERE table_name = 'transacoes' AND column_name IN ('observacoes', 'origem') ORDER BY column_name
            """);

        columns.ShouldBe([("observacoes", "text", null, "YES"), ("origem", "character varying", 20, "YES")]);
    }

    [Fact]
    [Trait("AC", "004-21")]
    public async Task RunningTheScriptAgain_DoesNotFail()
    {
        await _data.ExecuteAsync(MigrationRunner.ReadScript("0050_transacoes_observacoes_origem"));
        await _data.ExecuteAsync(MigrationRunner.ReadScript("0050_transacoes_observacoes_origem"));
    }

    [Fact]
    [Trait("AC", "004-10")]
    public async Task RowsInsertedWithoutTheColumns_ReadNull()
    {
        var user = await _data.InsertUserAsync();
        var account = await _data.InsertAccountAsync(user, "Corrente");
        var id = await _data.InsertTransactionAsync(user, account);

        var row = (await _data.QueryAsync<(string? Notes, string? Origin)>(
            "SELECT observacoes, origem FROM transacoes WHERE id = @id", new { id })).Single();

        row.ShouldBe((null, null));
    }

    [Fact]
    public async Task TheMigrationIsRegistered()
    {
        (await _data.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM dotnet_migrations WHERE versao = '0050_transacoes_observacoes_origem')"))
            .ShouldBeTrue();
    }
}
