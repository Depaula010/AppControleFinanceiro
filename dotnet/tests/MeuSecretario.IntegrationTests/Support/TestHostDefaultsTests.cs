using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Support;

/// <summary>Garante que os hosts dos testes não observam arquivos de configuração (senão o CI estoura o limite de inotify).</summary>
[Trait("Category", "Docker")]
public sealed class TestHostDefaultsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public void TestHosts_DoNotWatchConfigurationFiles()
    {
        var configuration = (IConfigurationRoot)postgres.Api.Services.GetRequiredService<IConfiguration>();

        var jsonSources = configuration.Providers.OfType<JsonConfigurationProvider>().ToList();

        jsonSources.ShouldNotBeEmpty();
        jsonSources.ShouldAllBe(provider => !provider.Source.ReloadOnChange);
    }
}
