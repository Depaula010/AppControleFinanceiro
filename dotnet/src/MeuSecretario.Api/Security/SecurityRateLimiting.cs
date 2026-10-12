using System.Globalization;
using System.Threading.RateLimiting;
using MeuSecretario.Api.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Rate limit por IP real (spec 009 §8, desvio 081): janela fixa de 1 hora em memória, <b>desligado por padrão</b>
/// (<c>RATE_LIMIT_ENABLED=false</c>: o limitador global é <c>NoLimiter</c>). Balde geral e balde de <c>/admin/*</c>;
/// isentos <c>GET /</c>, <c>GET /api/health</c> e chamadas internas. 429 em JSON envelope D com <c>Retry-After</c>.
/// </summary>
public static class SecurityRateLimiting
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public static IServiceCollection AddSecurityRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(static _ => { });
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<SecurityOptions>>((limiter, security) => Configure(limiter, security.Value));
        return services;
    }

    public static void Configure(RateLimiterOptions limiter, SecurityOptions security)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = static async (context, cancellationToken) =>
        {
            var response = context.HttpContext.Response;
            var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                : (int)Window.TotalSeconds;
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            await Envelopes.D.Error("Too many requests", "Rate limit exceeded. Try again later.", StatusCodes.Status429TooManyRequests)
                .ExecuteAsync(context.HttpContext);
        };

        limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => Partition(context, security));
    }

    private static RateLimitPartition<string> Partition(HttpContext context, SecurityOptions security)
    {
        if (!security.RateLimitEnabled)
        {
            return RateLimitPartition.GetNoLimiter("off");
        }

        var client = ClientIp.Resolve(context);
        if (client.Internal || IsExemptPath(context.Request))
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        var admin = context.Request.Path.StartsWithSegments("/admin", StringComparison.Ordinal);
        var limit = admin ? security.RateLimitAdminPerHour : security.RateLimitPerHour;
        return RateLimitPartition.GetFixedWindowLimiter(
            (admin ? "admin:" : "general:") + client.Ip,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = Window,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    }

    private static bool IsExemptPath(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) && request.Path.Value is "/" or "/api/health";
}
