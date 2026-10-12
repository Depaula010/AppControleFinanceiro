using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Database;

/// <summary>Migration 0020 (seed das categorias globais): idempotente e inofensiva em produção (ADR 0001, adendo).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class ReferenceDataMigrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> Count(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    [Fact]
    public async Task EmptyDatabase_GetsTheFullSeedWithStableIds()
    {
        (await Count("SELECT count(*) FROM grupocategoria")).ShouldBe(5);
        (await Count("SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL")).ShouldBe(18);
        (await Count("SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL")).ShouldBe(60); // 42 + um "Outros" por macro

        // Ids 1 a 18 nas macros, na ordem do Flask (macro 17 = Despesas Gerais).
        (await Count("SELECT min(id) FROM macrocategoria")).ShouldBe(1);
        (await Count("SELECT max(id) FROM macrocategoria")).ShouldBe(18);
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        (await connection.ExecuteScalarAsync<string>("SELECT nome_macro FROM macrocategoria WHERE id = 17")).ShouldBe("Despesas Gerais");
        (await connection.ExecuteScalarAsync<string>("SELECT nome_macro FROM macrocategoria WHERE id = 18")).ShouldBe("Receitas Gerais");
    }

    [Theory]
    [InlineData("Investimentos de Curto Prazo")]
    [InlineData("Quitação de Empréstimos (Principal)")]
    [InlineData("Outros")]
    public async Task TheSubcategoriesThatOtherFeaturesRequire_Exist(string name)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL AND nome_sub = @name", new { name })).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task RunningTheScriptAgain_InsertsNothing()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));
        await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));

        (await Count("SELECT count(*) FROM macrocategoria")).ShouldBe(18);
        (await Count("SELECT count(*) FROM subcategoria")).ShouldBe(60);
    }

    [Fact]
    public async Task ProductionLikeState_WithTheDuplicatedOutros_InsertsNothingAndNeverTouchesUserRows()
    {
        // Estado real de produção: 61 subcategorias (o "Outros" da macro 17 duplicado) e categorias de usuário.
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (17, 'Outros')");
        await connection.ExecuteAsync("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Ana', '5511999999999')");
        await connection.ExecuteAsync("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro) VALUES (5, 1, 'Minha macro')");

        await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));

        (await Count("SELECT count(*) FROM subcategoria WHERE usuario_id IS NULL")).ShouldBe(61);
        (await Count("SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL")).ShouldBe(18);
        (await Count("SELECT count(*) FROM macrocategoria WHERE usuario_id = 1")).ShouldBe(1);
        (await Count("SELECT count(*) FROM grupocategoria")).ShouldBe(5);
    }

    [Fact]
    public async Task ResetAsync_KeepsTheSeed_ForTheNextTest()
    {
        await postgres.ResetAsync();
        await postgres.ResetAsync();

        (await Count("SELECT count(*) FROM macrocategoria WHERE usuario_id IS NULL")).ShouldBe(18);
    }
}
