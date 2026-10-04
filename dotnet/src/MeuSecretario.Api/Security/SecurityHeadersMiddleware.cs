using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Headers do Talisman do Flask (spec 009 §7, RN-009-11) em todas as respostas, inclusive os 403/404 do filtro. Conjunto da
/// spec: o conjunto exato que o Flask emite em produção ainda precisa ser capturado com <c>curl -I</c> (Q-009-2). O nonce
/// do CSP muda a cada resposta; as páginas HTML da 010d não usam <c>&lt;script&gt;</c> inline.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    public static string ContentSecurityPolicy(string nonce) =>
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: https:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        if (options.Value.SecurityHeadersEnabled)
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers["X-Frame-Options"] = "DENY";
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Strict-Transport-Security"] = StrictTransportSecurity;
                headers["Content-Security-Policy"] = ContentSecurityPolicy(Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)));
                return Task.CompletedTask;
            }, context);
        }

        return next(context);
    }
}
