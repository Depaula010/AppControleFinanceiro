using Dapper;
using MeuSecretario.Infrastructure.Database;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MeuSecretario.IntegrationTests.Support;

/// <summary>
/// PostgreSQL 15 (mesma versão da produção) em container, com as migrations aplicadas.
/// Nunca aponta para o banco de produção nem de homologação (constitution.md §9).
/// Os testes que usam este fixture exigem Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public IDbConnectionFactory Connections { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        Connections = new NpgsqlConnectionFactory(DataSource);
        await new MigrationRunner(DataSource).RunAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    /// <summary>Apaga usuários (e, em cascata, contas e o resto) e reinicia as sequências entre testes.</summary>
    public async Task ResetAsync()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("TRUNCATE usuarios RESTART IDENTITY CASCADE");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
