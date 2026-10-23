using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Settings;

/// <summary>Migration 0070 (desvio 250): coluna própria de <c>daysBeforeDue</c>, idempotente, sem copiar o valor antigo.</summary>
[Trait("Category", "Docker")]
public class Migration0070Tests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private const string Version = "0070_notificationconfigs_dias_antes_vencimento";

    public Task InitializeAsync() => postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("AC", "007-48")]
    public async Task Script_IsIdempotent_AndExistingRowsGetTheDefault_WithoutCopyingThePotThreshold()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("ALTER TABLE notificationconfigs DROP COLUMN dias_antes_vencimento");
        var user = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Mig', '5511900007000') RETURNING id");
        await connection.ExecuteAsync(
            "INSERT INTO notificationconfigs (usuario_id, alerta_potes_threshold) VALUES (@user, 1)", new { user });

        await connection.ExecuteAsync(MigrationRunner.ReadScript(Version));
        await connection.ExecuteAsync(MigrationRunner.ReadScript(Version));

        (await connection.ExecuteScalarAsync<int>("SELECT dias_antes_vencimento FROM notificationconfigs WHERE usuario_id = @user", new { user })).ShouldBe(3);
        (await connection.ExecuteScalarAsync<int>("SELECT alerta_potes_threshold FROM notificationconfigs WHERE usuario_id = @user", new { user })).ShouldBe(1);
        (await connection.ExecuteScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'notificationconfigs' AND column_name = 'dias_antes_vencimento'")).ShouldBe("NO");
    }
}
