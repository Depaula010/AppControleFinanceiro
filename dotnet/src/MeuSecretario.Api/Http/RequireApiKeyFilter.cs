using MeuSecretario.Api.Contracts;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Api.Modules;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.Api.Http;

/// <summary>
/// Equivale ao <c>x-api-key</c> do Flask (<c>api_keys.py:18-31</c>): compara o header <c>X-API-KEY</c> (primeiro valor)
/// com a <c>API_SECRET_KEY</c> em tempo constante. Se falhar, devolve o 401 no envelope escolhido pela rota
/// (<paramref name="unauthorized"/>) e <b>não</b> chama o endpoint, logo o corpo nem é lido.
/// </summary>
public sealed class RequireApiKeyFilter(IApiKeyValidator validator, Func<HttpContext, IResult> unauthorized) : IEndpointFilter
{
    public const string HeaderName = "X-API-KEY";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        string? presented = http.Request.Headers[HeaderName].FirstOrDefault();

        return validator.IsValid(presented)
            ? next(context)
            : ValueTask.FromResult<object?>(unauthorized(http));
    }
}

/// <summary>401 prontos por envelope. 009 e 012 acrescentam os seus (B e E) sem alterar a classe do filtro.</summary>
public static class ApiKeyUnauthorized
{
    /// <summary>Envelope C (<c>/api-keys/*</c>).</summary>
    public static IResult EnvelopeC(HttpContext _) =>
        Envelopes.C.Error("Não autorizado", "API key inválida ou ausente", StatusCodes.Status401Unauthorized);
}

public static class RequireApiKeyExtensions
{
    /// <summary>
    /// Protege o endpoint (ou o grupo) com <c>X-API-KEY</c>. <paramref name="unauthorized"/> monta o 401 no
    /// envelope da rota, por exemplo <c>RequireApiKey(ApiKeyUnauthorized.EnvelopeC)</c>.
    /// O <see cref="IApiKeyValidator"/> é resolvido do contêiner de serviços da aplicação.
    /// </summary>
    public static TBuilder RequireApiKey<TBuilder>(this TBuilder builder, Func<HttpContext, IResult> unauthorized)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(unauthorized);

        return builder.AddEndpointFilterFactory((factory, next) =>
        {
            var filter = new RequireApiKeyFilter(factory.ApplicationServices.GetRequiredService<IApiKeyValidator>(), unauthorized);
            return context => filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Módulo sem rotas: registra o <see cref="IApiKeyValidator"/> para qualquer feature usar o filtro sem editar
/// <c>DependencyInjection.cs</c> (<c>TryAdd</c>: quem registrar antes vence).
/// </summary>
public sealed class ApiKeyAuthModule : IFeatureModule
{
    public void AddServices(IServiceCollection services) =>
        services.TryAddSingleton<IApiKeyValidator>(sp =>
            new ApiSecretKeyValidator(sp.GetRequiredService<AppSettings>().ApiSecretKey));

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}
