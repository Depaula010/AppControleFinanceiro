using Dapper;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>
/// Critérios do seed (spec 003 §4 / AC-003-40 a 45) que o <c>ReferenceDataMigrationTests</c> da G-2 não verifica:
/// conteúdo exato, ids do seed, sequences, ausência de linhas de usuário e o registro da migration.
/// </summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class CategorySeedGapsTests(PostgresFixture postgres) : IAsyncLifetime
{
    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly (int Id, string Grupo, string Macro, int? Ordem)[] ExpectedMacros =
    [
        (1, "Renda", "Renda Principal", 1),
        (2, "Renda", "Renda Extra e Passiva", 2),
        (3, "Renda", "Investimentos e Reembolsos", 3),
        (4, "Renda", "Outras Entradas", 4),
        (5, "Despesa Essencial", "Moradia e Contas", 1),
        (6, "Despesa Essencial", "Alimentação Essencial", 2),
        (7, "Despesa Essencial", "Saúde e Bem-Estar Básico", 3),
        (8, "Despesa Essencial", "Educação Essencial", 4),
        (9, "Despesa Essencial", "Transporte Essencial", 5),
        (10, "Meta Financeira", "Construção de Riqueza", 1),
        (11, "Meta Financeira", "Gerenciamento de Dívidas", 2),
        (12, "Meta Financeira", "Educação de Alto Custo", 3),
        (13, "Despesa Discricionária", "Lazer e Entretenimento", 1),
        (14, "Despesa Discricionária", "Assinaturas e Serviços", 2),
        (15, "Despesa Discricionária", "Cuidados Pessoais", 3),
        (16, "Despesa Discricionária", "Compras Pessoais", 4),
        (17, "Geral", "Despesas Gerais", null),
        (18, "Geral", "Receitas Gerais", null),
    ];

    [Fact]
    [Trait("AC", "003-40")]
    public async Task Seed_HasTheExactMacrosGroupsAndOrderOfTheSpec()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        var actual = (await connection.QueryAsync<(int Id, string Grupo, string Macro, int? Ordem)>(
            """
            SELECT m.id, g.nome_grupo, m.nome_macro, m.ordem_macro
            FROM macrocategoria m JOIN grupocategoria g ON g.id = m.grupo_id
            ORDER BY m.id
            """)).ToList();

        actual.ShouldBe(ExpectedMacros);
    }

    [Fact]
    [Trait("AC", "003-40")]
    public async Task Seed_GivesTheOutrosOfEachMacroTheIds43To60_AndKeepsTheRequiredNames()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        var outros = (await connection.QueryAsync<(int Id, int MacroId)>(
            "SELECT id, macro_id FROM subcategoria WHERE nome_sub = 'Outros' ORDER BY id")).ToList();
        outros.ShouldBe(Enumerable.Range(1, 18).Select(i => (42 + i, i)).ToList());

        (await connection.ExecuteScalarAsync<int>("SELECT id FROM subcategoria WHERE nome_sub = 'Investimentos de Curto Prazo'")).ShouldBe(28);
        (await connection.ExecuteScalarAsync<int>("SELECT id FROM subcategoria WHERE nome_sub = 'Quitação de Empréstimos (Principal)'")).ShouldBe(29);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM subcategoria WHERE nome_sub <> 'Outros'")).ShouldBe(42);
    }

    [Fact]
    [Trait("AC", "003-41")]
    public async Task RunningTheScriptAgain_LeavesTheRowsAndTheSequencesUntouched()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        var before = await Snapshot(connection);

        await connection.ExecuteAsync(MigrationRunner.ReadScript("0020_categorias_globais"));

        (await Snapshot(connection)).ShouldBe(before);
    }

    [Fact]
    [Trait("AC", "003-44")]
    public async Task Seed_CreatesNoUserRows()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM macrocategoria WHERE usuario_id IS NOT NULL")).ShouldBe(0);
        (await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM subcategoria WHERE usuario_id IS NOT NULL")).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "003-45")]
    public async Task TheRunnerRegistersTheMigration()
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();

        (await connection.ExecuteScalarAsync<int>(
            "SELECT count(*) FROM dotnet_migrations WHERE versao = '0020_categorias_globais'")).ShouldBe(1);
    }

    private static async Task<string> Snapshot(Npgsql.NpgsqlConnection connection)
    {
        var rows = await connection.QueryAsync<string>(
            """
            SELECT 'g' || id || nome_grupo FROM grupocategoria
            UNION ALL SELECT 'm' || id || nome_macro FROM macrocategoria
            UNION ALL SELECT 's' || id || nome_sub || macro_id FROM subcategoria
            """);
        var sequences = await connection.QueryAsync<string>(
            """
            SELECT sequencename || ':' || coalesce(last_value, 0)
            FROM pg_sequences
            WHERE sequencename IN ('grupocategoria_id_seq', 'macrocategoria_id_seq', 'subcategoria_id_seq')
            """);
        return string.Join("|", rows.Order(StringComparer.Ordinal).Concat(sequences.Order(StringComparer.Ordinal)));
    }
}
