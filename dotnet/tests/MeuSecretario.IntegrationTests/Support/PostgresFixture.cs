using Dapper;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
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
    // Cada teste de ponta a ponta sobe a sua própria fábrica da API (e portanto o seu pool do Npgsql, que não é descartado
    // entre testes); o limite padrão de 100 conexões do Postgres estoura com a suíte inteira ("53300: too many clients").
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:15-alpine")
        .WithCommand("-c", "max_connections=1000")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public IDbConnectionFactory Connections { get; private set; } = default!;

    /// <summary>DATABASE_URL no formato do Flask (<c>postgresql://usuario:senha@host:porta/banco</c>) para o container.</summary>
    public string DatabaseUrl
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(ConnectionString);
            return $"postgresql://{Uri.EscapeDataString(b.Username!)}:{Uri.EscapeDataString(b.Password!)}@{b.Host}:{b.Port}/{b.Database}";
        }
    }

    /// <summary>
    /// A API real em memória apontando para o container. A <c>API_SECRET_KEY</c> é a das fixtures, para que os tokens
    /// emitidos pelo PyJWT do Flask sejam aceitos. Hash com 1000 iterações só para os testes ficarem rápidos.
    /// </summary>
    public WebApplicationFactory<Program> Api => _api ??= new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("DATABASE_URL", DatabaseUrl);
        builder.UseSetting("API_SECRET_KEY", AuthFixtures.JwtSecret);
        builder.UseSetting("ENCRYPTION_KEY", AuthFixtures.FernetKey);
        builder.UseSetting("GEMINI_API_KEY", "gemini-de-teste");
        builder.UseSetting("REDIS_URL", "redis://localhost:6379");
        builder.UseSetting("PASSWORD_HASH_ITERATIONS", "1000");
        builder.UseSetting("LOG_TO_FILE", "false");
    });

    private WebApplicationFactory<Program>? _api;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        Connections = new NpgsqlConnectionFactory(DataSource);
        await new MigrationRunner(DataSource).RunAsync();
    }

    public async Task DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Apaga usuários (e, em cascata, contas, categorias e o resto) e reinicia as sequências entre testes,
    /// e em seguida reaplica o seed das categorias globais (migration 0020), que o CASCADE também apaga.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var connection = await DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("TRUNCATE usuarios RESTART IDENTITY CASCADE");
        await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresTestGroup : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
