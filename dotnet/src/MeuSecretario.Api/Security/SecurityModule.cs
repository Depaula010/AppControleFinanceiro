using System.Net;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Security;
using MeuSecretario.Infrastructure.Security;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Feature 009-0: filtro de segurança, IP real, headers, rate limit e fallback 404. Sem rotas. O pipeline (ordem do
/// <c>Program.cs</c>, a única edição autorizada) e o <c>security.log</c> são montados lá.
/// </summary>
public sealed class SecurityModule : IFeatureModule
{
    /// <summary>Rede privada do nginx/compose, igual ao <c>IpClassifier.IsTrusted</c> (spec §5.1).</summary>
    private static readonly (string Network, int Prefix)[] TrustedNetworks =
    [
        ("127.0.0.0", 8),
        ("::1", 128),
        ("10.0.0.0", 8),
        ("172.16.0.0", 12),
        ("192.168.0.0", 16),
        ("169.254.0.0", 16),
        ("fc00::", 7),
        ("fe80::", 10),
    ];

    public void AddServices(IServiceCollection services)
    {
        services.AddOptions<SecurityOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                var loaded = SecurityOptions.Load(key => configuration[key]);
                options.BotProtectionEnabled = loaded.BotProtectionEnabled;
                options.SecurityHeadersEnabled = loaded.SecurityHeadersEnabled;
                options.LogFile = loaded.LogFile;
                options.LogLevel = loaded.LogLevel;
                options.RateLimitEnabled = loaded.RateLimitEnabled;
                options.RateLimitPerHour = loaded.RateLimitPerHour;
                options.RateLimitAdminPerHour = loaded.RateLimitAdminPerHour;
            });

        services.TryAddSingleton<ISecurityStore, RedisSecurityStore>();
        services.TryAddSingleton<SecurityFilter>();

        // O X-Forwarded-For é lido da direita para a esquerda e só vale vindo da rede privada (desvio 082).
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var (network, prefix) in TrustedNetworks)
            {
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse(network), prefix));
            }

            options.ForwardLimit = null;
        });

        services.AddSecurityRateLimiting();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
    }
}
