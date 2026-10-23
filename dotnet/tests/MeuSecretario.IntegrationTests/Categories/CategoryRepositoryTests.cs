using Dapper;
using MeuSecretario.Domain.Categories;
using MeuSecretario.Infrastructure.Categories;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Categories;

/// <summary>Spec 003 §3.5: visibilidade, filtro por tipo e ordem da consulta, contra o PostgreSQL 15 em container.</summary>
[Trait("Category", "Docker")]
public class CategoryRepositoryTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly CategoryRepository _repository = new(postgres.Connections);

    public async Task InitializeAsync() => await postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<IReadOnlyList<CategoryRow>> List(int userId, CategoryTypeFilter filter = CategoryTypeFilter.None) =>
        await _repository.ListVisibleAsync(userId, filter, CancellationToken.None);

    private async Task Sql(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    private async Task<int> Scalar(string sql)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(sql);
    }

    private async Task TwoUsers() =>
        await Sql("INSERT INTO usuarios (nome, numero_whatsapp) VALUES ('Ana', '5511111111111'), ('Bia', '5522222222222')");

    // ------------------------------- listagem e ordem -------------------------------

    [Fact]
    [Trait("AC", "003-01")]
    public async Task NoFilter_ReturnsTheWholeSeed_InTheGroupOrderOfTheSpec()
    {
        var rows = await List(1);

        rows.Count.ShouldBe(60);
        rows.Select(r => r.MacroId).Distinct().Count().ShouldBe(18);
        rows.Select(r => r.Grupo).Distinct().ShouldBe(
            ["Despesa Discricionária", "Despesa Essencial", "Geral", "Meta Financeira", "Renda"]);
    }

    [Fact]
    [Trait("AC", "003-03")]
    public async Task Receita_ReturnsRendaPlusReceitasGerais_WithOutrosFirstInRendaPrincipal()
    {
        var rows = await List(1, CategoryTypeFilter.Receita);

        rows.Count.ShouldBe(13);
        rows.Select(r => r.MacroCategoria).Distinct().ShouldBe(
            ["Receitas Gerais", "Renda Principal", "Renda Extra e Passiva", "Investimentos e Reembolsos", "Outras Entradas"]);
        rows.Where(r => r.MacroCategoria == "Renda Principal").Select(r => (r.SubcategoriaId, r.SubcategoriaNome)).ShouldBe(
            [(43, "Outros"), (2, "Remuneração Variável / Comissões"), (1, "Salário Fixo / Pró-labore")]);
    }

    [Fact]
    [Trait("AC", "003-04")]
    public async Task Despesa_ExcludesRendaAndReceitasGerais()
    {
        var rows = await List(1, CategoryTypeFilter.Despesa);

        rows.Count.ShouldBe(47);
        rows.Select(r => r.MacroId).Distinct().Count().ShouldBe(13);
        rows.ShouldNotContain(r => r.Grupo == "Renda");
        rows.ShouldNotContain(r => r.MacroCategoria == "Receitas Gerais");
        rows.ShouldContain(r => r.MacroCategoria == "Despesas Gerais");
    }

    [Fact]
    [Trait("AC", "003-07")]
    public async Task Order_MacrosFollowOrdemMacroWithNullLast_AndSubcategoriesFollowTheByteOrderOfTheName()
    {
        var rows = await List(1);

        rows.Where(r => r.Grupo == "Geral").Select(r => r.MacroCategoria).Distinct().ShouldBe(["Despesas Gerais", "Receitas Gerais"]);
        rows.Where(r => r.Grupo == "Despesa Essencial").Select(r => r.MacroCategoria).Distinct().ShouldBe(
            ["Moradia e Contas", "Alimentação Essencial", "Saúde e Bem-Estar Básico", "Educação Essencial", "Transporte Essencial"]);
        rows.Where(r => r.MacroCategoria == "Renda Extra e Passiva").Select(r => r.SubcategoriaNome).ShouldBe(
            ["Aluguéis Recebidos", "Freelances / Projetos Avulsos", "Outros"]);
    }

    [Fact]
    [Trait("AC", "003-08")]
    public async Task MacroWithoutAnyVisibleSubcategory_DoesNotAppear()
    {
        await Sql("INSERT INTO macrocategoria (grupo_id, nome_macro, ordem_macro) VALUES (5, 'Vazia', 9)");

        (await List(1)).ShouldNotContain(r => r.MacroCategoria == "Vazia");
    }

    [Fact]
    [Trait("AC", "003-09")]
    public async Task NoCategories_ReturnsNothing()
    {
        await Sql("DELETE FROM subcategoria");

        (await List(1)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "003-32")]
    public async Task DuplicatedGlobalOutros_ShowsBoth_LowestIdFirst()
    {
        await Sql("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (17, 'Outros')");

        var rows = (await List(1)).Where(r => r.MacroId == 17).ToList();

        rows.Select(r => r.SubcategoriaId).ShouldBe([59, 61]);
    }

    // ------------------------------- isolamento -------------------------------

    [Fact]
    [Trait("AC", "003-10")]
    public async Task UserSubcategoryInAGlobalMacro_ShowsOnlyForItsOwner()
    {
        await TwoUsers();
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (13, 1, 'Minha sub')");

        (await List(1)).ShouldContain(r => r.SubcategoriaNome == "Minha sub" && r.MacroId == 13);
        (await List(2)).ShouldNotContain(r => r.SubcategoriaNome == "Minha sub");
    }

    [Fact]
    [Trait("AC", "003-11")]
    public async Task UserMacroWithUserSubcategory_ShowsOnlyForItsOwner()
    {
        await TwoUsers();
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (4, 1, 'Minha macro', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (19, 1, 'Minha sub')");

        var ana = await List(1);
        ana.ShouldContain(r => r.MacroCategoria == "Minha macro" && r.SubcategoriaNome == "Minha sub");
        (await List(2)).ShouldNotContain(r => r.MacroCategoria == "Minha macro");
    }

    [Fact]
    [Trait("AC", "003-12")]
    public async Task GlobalSubcategoryInsideAUserMacro_ShowsOnlyForTheMacroOwner()
    {
        await TwoUsers();
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (4, 1, 'Minha macro', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, nome_sub) VALUES (19, 'Global dentro')");

        (await List(1)).ShouldContain(r => r.SubcategoriaNome == "Global dentro");
        (await List(2)).ShouldNotContain(r => r.SubcategoriaNome == "Global dentro");
    }

    [Fact]
    [Trait("AC", "003-13")]
    public async Task SubcategoryOfBInsideAMacroOfA_ShowsForNeither()
    {
        await TwoUsers();
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (4, 1, 'Macro de Ana', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (19, 2, 'Sub de Bia')");

        (await List(1)).ShouldNotContain(r => r.SubcategoriaNome == "Sub de Bia");
        (await List(2)).ShouldNotContain(r => r.SubcategoriaNome == "Sub de Bia");
    }

    [Theory]
    [Trait("AC", "003-14")]
    [InlineData(CategoryTypeFilter.None)]
    [InlineData(CategoryTypeFilter.Receita)]
    [InlineData(CategoryTypeFilter.Despesa)]
    public async Task OwnIdsOfOneUser_NeverAppearForTheOther(CategoryTypeFilter filter)
    {
        await TwoUsers();
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (1, 1, 'Renda de Ana', 9), (4, 2, 'Gasto de Bia', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (19, 1, 'a1'), (20, 2, 'b1'), (1, 1, 'a2'), (13, 2, 'b2')");
        var anaIds = await Ids("usuario_id = 1");
        var biaIds = await Ids("usuario_id = 2");

        var forAna = (await List(1, filter)).Select(r => r.SubcategoriaId).ToHashSet();
        var forBia = (await List(2, filter)).Select(r => r.SubcategoriaId).ToHashSet();

        forAna.Overlaps(biaIds).ShouldBeFalse();
        forBia.Overlaps(anaIds).ShouldBeFalse();
    }

    private async Task<List<int>> Ids(string where)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<int>($"SELECT id FROM subcategoria WHERE {where}")).ToList();
    }

    [Fact]
    [Trait("AC", "003-15")]
    public async Task UnknownUserId_GetsOnlyTheGlobals()
    {
        (await List(999)).Count.ShouldBe(60);
    }

    [Fact]
    public async Task UserMacroInAnotherGroupNamedReceitasGerais_DoesNotLeakIntoReceita()
    {
        await TwoUsers();
        await Sql("INSERT INTO macrocategoria (grupo_id, usuario_id, nome_macro, ordem_macro) VALUES (2, 1, 'Receitas Gerais', 9)");
        await Sql("INSERT INTO subcategoria (macro_id, usuario_id, nome_sub) VALUES (19, 1, 'Propria')");

        (await List(1, CategoryTypeFilter.Receita)).ShouldNotContain(r => r.SubcategoriaNome == "Propria");
        (await List(1, CategoryTypeFilter.Despesa)).ShouldContain(r => r.SubcategoriaNome == "Propria");
        (await Scalar("SELECT count(*) FROM macrocategoria")).ShouldBe(19);
    }
}
