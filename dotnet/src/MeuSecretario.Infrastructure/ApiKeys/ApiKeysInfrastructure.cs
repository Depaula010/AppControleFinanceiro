using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.ApiKeys;

/// <summary>Chaves do sistema vindas de <see cref="AppSettings"/>; vazia significa indisponível.</summary>
public sealed class SystemApiKeys(AppSettings settings) : ISystemApiKeys
{
    public string? KeyFor(string provider) => provider switch
    {
        ApiProviders.Gemini => settings.GeminiApiKey,
        ApiProviders.Weather => settings.WeatherApiKey,
        ApiProviders.OpenRoute => settings.OpenRouteApiKey,
        _ => null,
    };
}

/// <summary>Registra as falhas engolidas pela porta. Só operação e descrição segura (nunca chave, corpo ou texto do banco).</summary>
public sealed partial class ApiKeyDiagnostics(ILogger<ApiKeyDiagnostics> logger) : IApiKeyDiagnostics
{
    public void Swallowed(string operation, string description) => LogSwallowed(logger, operation, description);

    [LoggerMessage(EventId = 8101, Level = LogLevel.Warning, Message = "Falha ignorada no gerenciador de chaves ({Operation}): {Description}")]
    private static partial void LogSwallowed(ILogger logger, string operation, string description);
}

public static class ApiKeysServiceCollectionExtensions
{
    /// <summary>
    /// Repositórios, chaves do sistema e a porta <see cref="IApiKeyProvider"/> (008). Chamado pelo
    /// <c>ApiKeysModule.AddServices</c>; <c>TryAdd</c> para uma feature poder trocar uma peça antes.
    /// </summary>
    public static IServiceCollection AddApiKeysInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IApiKeyRepository, ApiKeyRepository>();
        services.TryAddSingleton<IPreferenceRepository, PreferenceRepository>();
        services.TryAddSingleton<IUsageRepository, UsageRepository>();
        services.TryAddSingleton<IAccessLogRepository, AccessLogRepository>();
        services.TryAddSingleton<IPlanRepository, PlanRepository>();
        services.TryAddSingleton<ISystemApiKeys, SystemApiKeys>();
        services.TryAddSingleton<IApiKeyDiagnostics, ApiKeyDiagnostics>();
        services.TryAddSingleton<IApiKeyProvider, ApiKeyProvider>();
        return services;
    }
}
