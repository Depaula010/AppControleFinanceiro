namespace MeuSecretario.Api.Http;

/// <summary>
/// O Flask não tem barra final nas rotas: <c>/api/bills/</c> responde 404. O ASP.NET tolera
/// a barra, então este middleware reproduz o 404 (api-contract.md §4).
/// </summary>
public sealed class StrictRoutingMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is { Length: > 1 } && path[^1] == '/')
        {
            return FlaskNotFound.WriteAsync(context);
        }
        return next(context);
    }
}

/// <summary>Corpo do 404 padrão do Flask (HTML).</summary>
public static class FlaskNotFound
{
    private const string Html =
        "<!doctype html>\n<html lang=en>\n<title>404 Not Found</title>\n<h1>Not Found</h1>\n" +
        "<p>The requested URL was not found on the server. If you entered the URL manually please check your spelling and try again.</p>\n";

    public static Task WriteAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/html; charset=utf-8";
        return context.Response.WriteAsync(Html);
    }
}
