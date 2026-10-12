namespace MeuSecretario.Domain.Categories;

/// <summary>Filtro <c>?tipo=</c> do contrato. O nome HTTP é <c>Receita</c>; no banco o grupo se chama <c>Renda</c> (desvio 001).</summary>
public enum CategoryTypeFilter
{
    None,
    Receita,
    Despesa,
}

public static class CategoryTypeFilterParser
{
    /// <summary>Só <c>Receita</c> e <c>Despesa</c> exatos (ordinal, sem trim) filtram; o resto é ignorado (RN-003-03).</summary>
    public static CategoryTypeFilter Parse(string? raw) => raw switch
    {
        "Receita" => CategoryTypeFilter.Receita,
        "Despesa" => CategoryTypeFilter.Despesa,
        _ => CategoryTypeFilter.None,
    };
}

/// <summary>Uma linha da consulta: subcategoria com a sua macro e o seu grupo.</summary>
public sealed record CategoryRow(string Grupo, int MacroId, string MacroCategoria, int SubcategoriaId, string SubcategoriaNome);

public sealed record SubCategory(int Id, string Nome);

public sealed record MacroCategory(string Grupo, int MacroId, string MacroCategoria, IReadOnlyList<SubCategory> Subcategorias);

public static class CategoryTree
{
    /// <summary>Agrupa por <c>(grupo, macro_id, macro_categoria)</c> na ordem da primeira aparição (RN-003-09).</summary>
    public static IReadOnlyList<MacroCategory> Build(IEnumerable<CategoryRow> rows)
    {
        var order = new List<(string Grupo, int MacroId, string Macro)>();
        var subs = new Dictionary<(string Grupo, int MacroId, string Macro), List<SubCategory>>();

        foreach (var row in rows)
        {
            var key = (row.Grupo, row.MacroId, row.MacroCategoria);
            if (!subs.TryGetValue(key, out var list))
            {
                list = [];
                subs[key] = list;
                order.Add(key);
            }
            list.Add(new SubCategory(row.SubcategoriaId, row.SubcategoriaNome));
        }

        return order.Select(key => new MacroCategory(key.Grupo, key.MacroId, key.Macro, subs[key])).ToList();
    }
}

/// <summary>Nomes de categorias exigidos por outras features (spec 003 §4.3).</summary>
public static class SpecialCategories
{
    public const string ShortTermInvestment = "Investimentos de Curto Prazo";
    public const string LoanPrincipal = "Quitação de Empréstimos (Principal)";
    public const string Fallback = "Outros";
    public const string MetaGroup = "Meta Financeira";
    public const string ExpenseFallbackMacro = "Despesas Gerais";
    public const string IncomeFallbackMacro = "Receitas Gerais";
}
