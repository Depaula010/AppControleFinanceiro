using System.Diagnostics;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.ApiKeys;
using MeuSecretario.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Infrastructure.Caching;

public class CachingRegistrationTests
{
    private static Dictionary<string, string?> Env(string redisUrl) => new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini-do-sistema",
        ["REDIS_URL"] = redisUrl,
    };

    private static ServiceProvider Build(string redisUrl, bool apiKeysFirst)
    {
        var env = Env(redisUrl);
        var services = new ServiceCollection();
        services.AddLogging();
        if (apiKeysFirst)
        {
            services.AddApiKeysInfrastructure();
        }

        services.AddInfrastructure(key => env.GetValueOrDefault(key));
        if (!apiKeysFirst)
        {
            services.AddApiKeysInfrastructure(); // como o módulo da 008 na API real: roda depois de AddInfrastructure
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [Trait("AC", "010-11")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddRedisCache_RegistersSingletonWithoutConnectingAtBoot(bool apiKeysFirst)
    {
        await using var provider = Build("redis://127.0.0.1:1", apiKeysFirst);
        var watch = Stopwatch.StartNew();

        var first = provider.GetRequiredService<ICache>();
        var second = provider.GetRequiredService<ICache>();

        first.ShouldBeOfType<RedisCache>();
        second.ShouldBeSameAs(first);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1)); // não tentou conectar (o timeout seria de 2 s)
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealUserCacheCleaner_PrevailsOverTheNullOneOfTheApiKeysFeature(bool apiKeysFirst)
    {
        await using var provider = Build("redis://127.0.0.1:1", apiKeysFirst);

        provider.GetRequiredService<IUserCacheCleaner>().ShouldBeOfType<UserCacheCleaner>();
    }

    [Fact]
    [Trait("AC", "010-09")]
    public async Task InvalidRedisUrl_FailsWhenResolvingTheCache_WithoutEchoingTheValue()
    {
        await using var provider = Build("http://:topsecret@host", apiKeysFirst: false);

        var ex = Should.Throw<FormatException>(() => provider.GetRequiredService<ICache>());

        ex.Message.ShouldNotContain("topsecret");
    }
}
