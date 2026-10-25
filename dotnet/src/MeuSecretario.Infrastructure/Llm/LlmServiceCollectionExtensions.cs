using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>Limites de tempo do pipeline do Gemini (padrão: <see cref="GeminiHttpOptions"/>); substituíveis em teste.</summary>
public sealed record GeminiResilienceSettings(TimeSpan AttemptTimeout, TimeSpan TotalTimeout, TimeSpan RetryDelay)
{
    public static GeminiResilienceSettings Default { get; } =
        new(GeminiHttpOptions.AttemptTimeout, GeminiHttpOptions.TotalTimeout, GeminiHttpOptions.RetryDelay);
}

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <c>ILlmClient</c>, o transporte REST, o coletor de métricas e a administração do cache (010c).
    /// Pipeline do <c>HttpClient</c> "gemini": teto total de 90 s, 1 retry (atraso de 1 s) só em 503, 504, timeout ou
    /// falha de rede, e 45 s por tentativa. Nenhum header de chave em <c>DefaultRequestHeaders</c>.
    /// </summary>
    public static IServiceCollection AddGeminiClient(this IServiceCollection services)
    {
        services.TryAddSingleton(GeminiResilienceSettings.Default);

        services.AddHttpClient(GeminiHttpOptions.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .AddResilienceHandler("gemini", (builder, context) =>
            {
                var settings = context.ServiceProvider.GetRequiredService<GeminiResilienceSettings>();
                builder.AddTimeout(settings.TotalTimeout);
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = GeminiHttpOptions.MaxRetries,
                    Delay = settings.RetryDelay,
                    BackoffType = DelayBackoffType.Constant,
                    UseJitter = false,
                    ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                });
                builder.AddTimeout(settings.AttemptTimeout);
            });

        services.AddSingleton<IGeminiEndpointProvider, DbGeminiEndpointProvider>();
        services.AddSingleton<IGeminiTransport, GeminiHttpTransport>();
        services.AddSingleton<LlmCacheMetricsCollector>();
        services.AddSingleton<ILlmClient, LlmClient>();
        services.AddSingleton<ILlmCacheAdmin, LlmCacheAdmin>();
        return services;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Exception is HttpRequestException or TimeoutRejectedException
        || outcome.Result?.StatusCode is System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout;
}
