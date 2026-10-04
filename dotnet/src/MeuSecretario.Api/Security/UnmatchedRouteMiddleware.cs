using MeuSecretario.Api.Http;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Fallback de 404 do Flask (spec 009 §6, RN-009-13): sem endpoint, responde o HTML padrão do Flask. É um middleware, não
/// <c>MapFallback</c>: o fallback <c>{*path}</c> casaria com qualquer método e roubaria o 405 do roteamento. Com
/// middleware, o 405 é um endpoint "método não suportado" (não nulo) e segue intacto, assim como o 404 de handler.
/// </summary>
public sealed class UnmatchedRouteMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) =>
        context.GetEndpoint() is null ? FlaskNotFound.WriteAsync(context) : next(context);
}
