using Dapper;
using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Categories;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Categories;

/// <summary>
/// Leitura de <c>grupocategoria</c>, <c>macrocategoria</c> e <c>subcategoria</c> com Dapper (spec 003 §3.5).
/// O fragmento de filtro vem de um <c>switch</c> no enum: nenhum texto do cliente entra no SQL.
/// </summary>
public sealed class CategoryRepository(IDbConnectionFactory connections) : ICategoryRepository
{
    private const string SelectVisible =
        """
        SELECT g.nome_grupo AS Grupo, m.id AS MacroId, m.nome_macro AS MacroCategoria,
               s.id AS SubcategoriaId, s.nome_sub AS SubcategoriaNome
        FROM subcategoria s
        JOIN macrocategoria m ON s.macro_id = m.id
        JOIN grupocategoria g ON m.grupo_id = g.id
        WHERE (s.usuario_id IS NULL OR s.usuario_id = @userId)
          AND (m.usuario_id IS NULL OR m.usuario_id = @userId)
        """;

    private const string OrderBy = " ORDER BY g.nome_grupo, m.ordem_macro, m.nome_macro, s.nome_sub, s.id";

    // Desvio 040: Receita = grupo Renda + macro Receitas Gerais (grupo Geral); Despesa = o resto.
    private const string ReceitaFilter =
        " AND (g.nome_grupo = 'Renda' OR (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais'))";

    private const string DespesaFilter =
        " AND g.nome_grupo != 'Renda' AND NOT (g.nome_grupo = 'Geral' AND m.nome_macro = 'Receitas Gerais')";

    public async Task<IReadOnlyList<CategoryRow>> ListVisibleAsync(
        int userId, CategoryTypeFilter filter, CancellationToken cancellationToken)
    {
        var sql = SelectVisible + filter switch
        {
            CategoryTypeFilter.Receita => ReceitaFilter,
            CategoryTypeFilter.Despesa => DespesaFilter,
            _ => string.Empty,
        } + OrderBy;

        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CategoryRow>(new CommandDefinition(
            sql, new { userId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
