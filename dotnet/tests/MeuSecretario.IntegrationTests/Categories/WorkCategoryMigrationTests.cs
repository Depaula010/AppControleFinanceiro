using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>Desvio 286: a migration 0140 cria a macro global "Trabalho" uma única vez.</summary>
[Trait("Category", "Docker")]
public class WorkCategoryMigrationTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_CreatesTheTrabalhoMacroWithItsSubcategories_AndIsIdempotent()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var script = MigrationRunner.ReadScript("0140_categoria_trabalho");

        await connection.ExecuteAsync(script);
        await connection.ExecuteAsync(script);

        (await connection.ExecuteScalarAsync<string>(
            """
            SELECT g.nome_grupo FROM macrocategoria m JOIN grupocategoria g ON g.id = m.grupo_id
            WHERE m.nome_macro = 'Trabalho' AND m.usuario_id IS NULL
            """)).ShouldBe("Despesa Essencial");
        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM macrocategoria WHERE nome_macro = 'Trabalho'")).ShouldBe(1);

        var subs = (await connection.QueryAsync<string>(
            """
            SELECT s.nome_sub FROM subcategoria s JOIN macrocategoria m ON m.id = s.macro_id
            WHERE m.nome_macro = 'Trabalho' ORDER BY s.nome_sub
            """)).ToList();
        subs.Count.ShouldBe(5);
        subs.ShouldContain("Alimentação no Trabalho (Marmita)");
        subs.ShouldContain("Combustível e Deslocamento (Trabalho)");
        subs.ShouldContain("Ferramentas e Assinaturas (Claude Code, etc.)");
    }
}
