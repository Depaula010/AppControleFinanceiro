using MeuSecretario.Domain.Categories;

namespace MeuSecretario.UnitTests.Domain.Categories;

public class CategoryTypeFilterParserTests
{
    [Theory]
    [Trait("AC", "003-03")]
    [InlineData("Receita", CategoryTypeFilter.Receita)]
    [InlineData("Despesa", CategoryTypeFilter.Despesa)]
    public void Parse_ExactValues_Filter(string raw, CategoryTypeFilter expected)
    {
        CategoryTypeFilterParser.Parse(raw).ShouldBe(expected);
    }

    [Theory]
    [Trait("AC", "003-05")]
    [InlineData("receita")]
    [InlineData("RECEITA")]
    [InlineData("despesa")]
    [InlineData("Renda")]
    [InlineData("")]
    [InlineData(" Receita")]
    [InlineData("Receita ")]
    [InlineData("xyz")]
    [InlineData(null)]
    public void Parse_AnythingElse_IsIgnored(string? raw)
    {
        CategoryTypeFilterParser.Parse(raw).ShouldBe(CategoryTypeFilter.None);
    }

    [Theory]
    [Trait("AC", "003-31")]
    [InlineData("' OR 1=1 --")]
    [InlineData("Receita\0")]
    [InlineData("Receita\n")]
    [InlineData("Ｒeceita")]
    public void Parse_HostileValues_AreIgnoredWithoutThrowing(string raw)
    {
        CategoryTypeFilterParser.Parse(raw).ShouldBe(CategoryTypeFilter.None);
        CategoryTypeFilterParser.Parse(new string('a', 10_000)).ShouldBe(CategoryTypeFilter.None);
    }
}

public class CategoryTreeTests
{
    private static CategoryRow Row(string grupo, int macroId, string macro, int subId, string sub) =>
        new(grupo, macroId, macro, subId, sub);

    [Fact]
    [Trait("AC", "003-09")]
    public void Build_NoRows_IsAnEmptyList()
    {
        CategoryTree.Build([]).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "003-06")]
    public void Build_GroupsRowsByMacro_KeepingTheFieldsAndTheOrder()
    {
        var tree = CategoryTree.Build(
        [
            Row("Renda", 1, "Renda Principal", 43, "Outros"),
            Row("Renda", 1, "Renda Principal", 2, "Remuneração"),
            Row("Renda", 2, "Renda Extra", 44, "Outros"),
        ]);

        tree.Count.ShouldBe(2);
        (tree[0].Grupo, tree[0].MacroId, tree[0].MacroCategoria).ShouldBe(("Renda", 1, "Renda Principal"));
        tree[0].Subcategorias.ShouldBe([new SubCategory(43, "Outros"), new SubCategory(2, "Remuneração")]);
        tree[1].Subcategorias.Single().ShouldBe(new SubCategory(44, "Outros"));
    }

    [Fact]
    [Trait("AC", "003-08")]
    public void Build_EachMacroAppearsOnce_AtTheFirstAppearanceOrder()
    {
        // Linhas intercaladas (não deveriam ocorrer com o ORDER BY, mas a regra é "ordem da primeira aparição").
        var tree = CategoryTree.Build(
        [
            Row("A", 2, "Dois", 1, "x"),
            Row("A", 1, "Um", 2, "y"),
            Row("A", 2, "Dois", 3, "z"),
        ]);

        tree.Select(m => m.MacroId).ShouldBe([2, 1]);
        tree[0].Subcategorias.Select(s => s.Id).ShouldBe([1, 3]);
    }

    [Fact]
    [Trait("AC", "003-32")]
    public void Build_DuplicatedNamesInTheSameMacro_KeepBothInTheReceivedOrder()
    {
        var tree = CategoryTree.Build(
        [
            Row("Geral", 17, "Despesas Gerais", 59, "Outros"),
            Row("Geral", 17, "Despesas Gerais", 61, "Outros"),
        ]);

        tree.Single().Subcategorias.Select(s => s.Id).ShouldBe([59, 61]);
    }
}
