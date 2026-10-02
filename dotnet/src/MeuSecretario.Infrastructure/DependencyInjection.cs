using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Auth;
using MeuSecretario.Infrastructure.Auth;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Database;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.Infrastructure.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace MeuSecretario.Infrastructure;

public static class DependencyInjection
{
    /// <param name="get">Leitor de configuração, avaliado quando <see cref="AppSettings"/> é resolvido pela primeira vez.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, Func<string, string?> get)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => AppSettings.Load(get));
        services.AddSingleton<IClock, BrazilClock>();

        // Banco
        services.AddSingleton(sp => NpgsqlDataSource.Create(sp.GetRequiredService<AppSettings>().ConnectionString));
        services.AddSingleton<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<IDatabaseHealth, NpgsqlDatabaseHealth>();

        services.AddSingleton<MigrationRunner>();

        // Auth (spec 001)
        services.AddSingleton<IUserRepository, UserRepository>();

        // Segurança (spec 001a)
        services.AddSingleton<IPasswordHasher>(sp =>
            new WerkzeugPasswordHasher(sp.GetRequiredService<AppSettings>().PasswordHashIterations));
        services.AddSingleton<IEncryptionService>(sp =>
            new FernetEncryptionService(sp.GetRequiredService<AppSettings>().EncryptionKey, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IJwtTokenService>(sp =>
            new JwtTokenService(sp.GetRequiredService<AppSettings>().ApiSecretKey, sp.GetRequiredService<TimeProvider>()));

        return services;
    }
}
