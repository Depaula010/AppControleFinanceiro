using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Categories;
using MeuSecretario.Domain.Categories;
using MeuSecretario.Infrastructure.Categories;

namespace MeuSecretario.Api.Categories;

/// <summary>
/// Feature 003: <c>GET /api/categories</c> e o alias <c>GET /api/categorias</c> (envelope A, JWT).
/// Somente leitura: não existe criação, edição nem exclusão de categoria por API (RN-003-11).
/// </summary>
public sealed class CategoriesModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<CategoryRepository>();
        services.AddSingleton<ICategoryRepository>(sp => sp.GetRequiredService<CategoryRepository>());
        services.AddSingleton<ICategoryLookup>(sp => sp.GetRequiredService<CategoryRepository>());
        services.AddScoped<ListCategories>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", ListAsync).RequireUser();
        app.MapGet("/api/categorias", ListAsync).RequireUser();
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request,
        ListCategories useCase,
        ICurrentUser currentUser,
        ILogger<CategoriesModule> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            // Com `tipo` repetido vale o primeiro valor (request.args.get do Flask).
            var tipo = request.Query["tipo"].FirstOrDefault();
            var categories = await useCase.ExecuteAsync(currentUser.UserId, tipo, cancellationToken);
            return Envelopes.A.Success(categories.Select(Map).ToList());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca loga a exceção inteira nem devolve o texto dela (desvio 008).
            CategoriesLog.ListFailed(logger, currentUser.UserId, SafeError.Describe(ex));
            return Envelopes.A.Error(CategoriesMessages.LoadFailed, StatusCodes.Status500InternalServerError);
        }
    }

    private static Dictionary<string, object?> Map(MacroCategory macro) => new()
    {
        ["grupo"] = macro.Grupo,
        ["macro_id"] = macro.MacroId,
        ["macro_categoria"] = macro.MacroCategoria,
        ["subcategorias"] = macro.Subcategorias
            .Select(sub => new Dictionary<string, object?> { ["id"] = sub.Id, ["nome"] = sub.Nome })
            .ToList(),
    };
}

/// <summary>Textos exatos do Flask (spec 003 §3.4).</summary>
public static class CategoriesMessages
{
    public const string LoadFailed = "Erro ao carregar categorias";
}

/// <summary>Logs da feature 003. Só o tipo da exceção e o SqlState, nunca a mensagem.</summary>
internal static partial class CategoriesLog
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Erro ao listar categorias (usuário {UserId}): {Error}")]
    public static partial void ListFailed(ILogger logger, int userId, string error);
}
