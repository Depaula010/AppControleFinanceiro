using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.Infrastructure;

public static class DependencyInjection
{
    /// <param name="get">Leitor de configuração, avaliado quando <see cref="AppSettings"/> é resolvido pela primeira vez.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<string, string?> get)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => AppSettings.Load(get));
        services.AddSingleton<IClock, BrazilClock>();
        services.AddSingleton<IDatabaseHealth, NpgsqlDatabaseHealth>();
        return services;
    }
}
