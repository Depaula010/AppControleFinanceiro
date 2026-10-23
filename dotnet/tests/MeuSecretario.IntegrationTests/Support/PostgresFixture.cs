using Dapper;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using MeuSecretario.Infrastructure.Database;
using Npgsql;
using Testcontainers.PostgreSql;

namespace MeuSecretario.IntegrationTests.Support;

/// <summary>
/// O container do PostgreSQL 15 (mesma versão da produção) da execução inteira: sobe UMA vez, com as migrations aplicadas num banco-modelo
/// (<c>template_app</c>); cada classe de teste clona o modelo num banco próprio (<c>CREATE DATABASE ... TEMPLATE</c>, uma fração de segundo).
/// Nunca aponta para o banco de produção nem de homologação (constitution.md §9).
/// </summary>
internal static class SharedPostgres
{
    private const string TemplateName = "template_app";

    private static readonly Lazy<Task<PostgreSqlContainer>> Container = new(StartAsync);

    // O Postgres só deixa UM clone por vez a partir do mesmo modelo ("source database is being accessed by other users").
    private static readonly SemaphoreSlim CloneGate = new(1, 1);

    public static async Task<NpgsqlConnectionStringBuilder> CloneAsync(string name)
    {
        var container = await Container.Value;
        var server = new NpgsqlConnectionStringBuilder(container.GetConnectionString());

        await CloneGate.WaitAsync();
        try
        {
            await using var admin = new NpgsqlConnection(server.ConnectionString);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE {TemplateName}");
        }
        finally
        {
            CloneGate.Release();
        }

        return new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = name };
    }

    public static async Task DropAsync(string name)
    {
        var container = await Container.Value;
        await using var admin = new NpgsqlConnection(container.GetConnectionString());
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    private static async Task<PostgreSqlContainer> StartAsync()
    {
        // Cada teste de ponta a ponta sobe a sua própria fábrica da API (e portanto o seu pool do Npgsql, que não é descartado
        // entre testes); o limite padrão de 100 conexões do Postgres estoura com a suíte inteira ("53300: too many clients").
        var container = new PostgreSqlBuilder("postgres:15-alpine")
            .WithCommand("-c", "max_connections=2000")
            .Build();
        await container.StartAsync();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => container.DisposeAsync().AsTask().GetAwaiter().GetResult();

        var server = new NpgsqlConnectionStringBuilder(container.GetConnectionString());
        await using (var admin = new NpgsqlConnection(server.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE {TemplateName}");
        }

        var template = new NpgsqlConnectionStringBuilder(server.ConnectionString) { Database = TemplateName };
        await using (var dataSource = NpgsqlDataSource.Create(template.ConnectionString))
        {
            await new MigrationRunner(dataSource).RunAsync();
        }

        // Sem conexão aberta no modelo, ou o CREATE DATABASE ... TEMPLATE recusa.
        NpgsqlConnection.ClearAllPools();
        return container;
    }
}

/// <summary>
/// Um banco PostgreSQL só desta classe de teste (clonado do modelo migrado), no container compartilhado da execução. Cada classe de teste é uma
/// coleção própria do xUnit e roda em paralelo com as outras; dentro da classe os testes seguem em série, e <see cref="ResetAsync"/> limpa
/// o banco entre eles. Os testes que usam este fixture exigem Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly string _database = $"t_{Guid.NewGuid():N}";

    private NpgsqlConnectionStringBuilder _connection = default!;

    public string ConnectionString => _connection.ConnectionString;

    public NpgsqlDataSource DataSource { get; private set; } = default!;

    public IDbConnectionFactory Connections { get; private set; } = default!;

    /// <summary>DATABASE_URL no formato do Flask (<c>postgresql://usuario:senha@host:porta/banco</c>) para o banco desta classe.</summary>
    public string DatabaseUrl
    {
        get
        {
            var b = new NpgsqlConnectionStringBuilder(ConnectionString);
            return $"postgresql://{Uri.EscapeDataString(b.Username!)}:{Uri.EscapeDataString(b.Password!)}@{b.Host}:{b.Port}/{b.Database}";
        }
    }

    /// <summary>
    /// A API real em memória apontando para o banco desta classe. A <c>API_SECRET_KEY</c> é a das fixtures, para que os tokens
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
        _connection = await SharedPostgres.CloneAsync(_database);
        DataSource = NpgsqlDataSource.Create(ConnectionString);
        Connections = new NpgsqlConnectionFactory(DataSource);
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

        // Só o pool desta classe: as outras rodam em paralelo e não podem perder as conexões delas.
        await using (var probe = new NpgsqlConnection(ConnectionString))
        {
            NpgsqlConnection.ClearPool(probe);
        }

        await SharedPostgres.DropAsync(_database);
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
