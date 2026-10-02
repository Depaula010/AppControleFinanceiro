using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Database;

[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class MigrationRunnerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EmptyDatabase_GetsTheWholeProductionSchema()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        var tables = (await connection.QueryAsync<string>(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'")).ToList();

        tables.Count.ShouldBe(25); // 24 do Flask + dotnet_migrations
        tables.ShouldContain("usuarios");
        tables.ShouldContain("transacoes");
        tables.ShouldContain("dotnet_migrations");

        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM information_schema.triggers WHERE trigger_schema = 'public'")).ShouldBe(9);
        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM pg_constraint WHERE contype = 'f' AND connamespace = 'public'::regnamespace")).ShouldBe(33);
    }

    [Fact]
    public async Task Migration0001_WidensTheApiKeyColumnToVarchar255()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        var length = await connection.ExecuteScalarAsync<int>(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'usuarios' AND column_name = 'api_key_automate'");

        length.ShouldBe(255);
    }

    [Fact]
    public async Task RunningAgain_AppliesNothing()
    {
        var processed = await new MigrationRunner(postgres.DataSource).RunAsync();

        processed.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registry_ListsEveryVersionOnce()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        var versions = (await connection.QueryAsync<string>("SELECT versao FROM dotnet_migrations ORDER BY versao")).ToList();

        versions.ShouldBe(["0000_baseline", "0001_usuarios_api_key_varchar255"]);
    }

    [Fact]
    public async Task ExistingProductionDatabase_AdoptsTheBaselineWithoutExecutingIt()
    {
        // Simula a produção: o banco já tem "usuarios" (e nada mais do baseline).
        var adminConnectionString = postgres.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync("CREATE DATABASE adotado");
        }

        var adoptedConnectionString = new NpgsqlConnectionStringBuilder(adminConnectionString) { Database = "adotado" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(adoptedConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync("CREATE TABLE usuarios (id int primary key, api_key_automate varchar(100))");
            await connection.ExecuteAsync("INSERT INTO usuarios VALUES (1, 'chave-existente-em-texto-puro')");
        }

        var processed = await new MigrationRunner(dataSource).RunAsync();

        processed.ShouldBe(["0000_baseline", "0001_usuarios_api_key_varchar255"]);
        await using var check = await dataSource.OpenConnectionAsync();
        // O baseline NÃO foi executado: nenhuma outra tabela do Flask apareceu...
        (await check.ExecuteScalarAsync<bool>("SELECT to_regclass('public.contas') IS NULL")).ShouldBeTrue();
        // ...mas a 0001 foi aplicada e os dados existentes continuam intactos.
        (await check.ExecuteScalarAsync<int>(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'usuarios' AND column_name = 'api_key_automate'"))
            .ShouldBe(255);
        (await check.ExecuteScalarAsync<string>("SELECT api_key_automate FROM usuarios WHERE id = 1")).ShouldBe("chave-existente-em-texto-puro");
    }
}
