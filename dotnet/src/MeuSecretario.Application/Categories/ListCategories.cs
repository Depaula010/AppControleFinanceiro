using MeuSecretario.Domain.Categories;

namespace MeuSecretario.Application.Categories;

/// <summary>Listagem de categorias do usuário (<c>api.py:1702-1788</c>). Exceções sobem para o endpoint.</summary>
public sealed class ListCategories(ICategoryRepository repository)
{
    public async Task<IReadOnlyList<MacroCategory>> ExecuteAsync(int userId, string? rawTipo, CancellationToken cancellationToken)
    {
        var filter = CategoryTypeFilterParser.Parse(rawTipo);
        var rows = await repository.ListVisibleAsync(userId, filter, cancellationToken);
        return CategoryTree.Build(rows);
    }
}
