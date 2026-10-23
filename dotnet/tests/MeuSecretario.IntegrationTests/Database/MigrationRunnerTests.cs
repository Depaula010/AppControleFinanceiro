using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;
using Npgsql;

namespace MeuSecretario.IntegrationTests.Database;

[Trait("Category", "Docker")]
public class MigrationRunnerTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
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

        versions.ShouldBe(
        [
            "0000_baseline", "0001_usuarios_api_key_varchar255", "0020_categorias_globais", "0050_transacoes_observacoes_origem",
            "0070_notificationconfigs_dias_antes_vencimento", "0080_transacoes_competencia",
            "0090_transacoes_competencia_unica",
        ]);
    }

    [Fact]
    public async Task ExistingProductionDatabase_AdoptsTheBaseline_AndInsertsNoReferenceData()
    {
        // Simula a produção: schema completo já existente (criado "à mão"), api_key_automate ainda varchar(100),
        // categorias já semeadas (com o "Outros" duplicado da macro 17 = 61 subcategorias) e um usuário real.
        await using (var admin = new NpgsqlConnection(postgres.ConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync("CREATE DATABASE adotado");
        }

        var adoptedConnectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { Database = "adotado" }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(adoptedConnectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await connection.ExecuteAsync(MigrationRunner.ReadScript(MigrationRunner.BaselineVersion));
            await connection.ExecuteAsync("ALTER TABLE usuarios ALTER COLUMN api_key_automate TYPE varchar(100)");
            await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));
            await connection.ExecuteAsync("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (17, 'Outros')"); // a duplicata real
            await connection.ExecuteAsync("INSERT INTO usuarios (nome, numero_whatsapp, api_key_automate) VALUES ('Real', '5511888888888', 'chave-existente-em-texto-puro')");
            await connection.ExecuteAsync("DROP TABLE IF EXISTS dotnet_migrations");
        }

        // Se o baseline fosse executado, falharia com "relation already exists": o sucesso prova que foi só adotado.
        var processed = await new MigrationRunner(dataSource).RunAsync();

        processed.ShouldBe(
        [
            "0000_baseline", "0001_usuarios_api_key_varchar255", "0020_categorias_globais", "0050_transacoes_observacoes_origem",
            "0070_notificationconfigs_dias_antes_vencimento", "0080_transacoes_competencia",
            "0090_transacoes_competencia_unica",
        ]);
        await using var check = await dataSource.OpenConnectionAsync();
        (await check.ExecuteScalarAsync<int>("SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL")).ShouldBe(61);
        (await check.ExecuteScalarAsync<int>("SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL")).ShouldBe(18);
        (await check.ExecuteScalarAsync<int>("SELECT count(*) FROM grupocategoria")).ShouldBe(5);
        (await check.ExecuteScalarAsync<int>(
            "SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'usuarios' AND column_name = 'api_key_automate'"))
            .ShouldBe(255);
        (await check.ExecuteScalarAsync<string>("SELECT api_key_automate FROM usuarios WHERE nome = 'Real'")).ShouldBe("chave-existente-em-texto-puro");
    }
}
