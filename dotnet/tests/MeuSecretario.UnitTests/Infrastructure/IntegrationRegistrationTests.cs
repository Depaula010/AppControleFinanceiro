using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.ApiKeys;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Infrastructure;

public class IntegrationRegistrationTests
{
    private static readonly Dictionary<string, string?> Env = new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini-do-sistema",
        ["REDIS_URL"] = "redis://redis:6379",
        ["GEMINI_MODEL"] = "gemini-teste",
    };

    [Fact]
    public async Task Container_ResolvesIntegrationSettingsAndProviderKeySelector()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(key => Env.GetValueOrDefault(key));
        services.AddLogging();
        services.AddApiKeysInfrastructure(); // o módulo da 008 faz isto na API real
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });

        var settings = provider.GetRequiredService<IIntegrationSettings>();
        settings.GeminiModel.ShouldBe("gemini-teste");
        settings.GeminiApiKey.ShouldBe("gemini-do-sistema");
        settings.BotSessionName.ShouldBe("sessao-principal");

        var selector = provider.GetRequiredService<IProviderKeySelector>();
        var result = await selector.SelectAsync(null, "gemini", CancellationToken.None);
        result.Key.ShouldBe("gemini-do-sistema");
    }
}
