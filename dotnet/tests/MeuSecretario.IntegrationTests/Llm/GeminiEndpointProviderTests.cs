using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.IntegrationTests.Llm;

/// <summary>Desvio 263: modelo e versão da API do Gemini vêm da tabela <c>configuracoes</c>, com cache de 60 s e padrão seguro.</summary>
[Trait("Category", "Docker")]
public sealed class GeminiEndpointProviderTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private readonly FakeTimeProvider _clock = new();

    public async Task InitializeAsync()
    {
        // Volta ao que a migration 0100 semeia, para um teste não depender do outro.
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "INSERT INTO configuracoes (chave, valor) VALUES ('gemini.modelo', 'gemini-2.5-flash-lite'), ('gemini.versao_api', 'v1beta') " +
            "ON CONFLICT (chave) DO UPDATE SET valor = EXCLUDED.valor");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DbGeminiEndpointProvider Provider(string? environmentModel = null) =>
        new(postgres.Connections, Settings(environmentModel), _clock, NullLogger<DbGeminiEndpointProvider>.Instance);

    private static AppSettingsIntegrationSettings Settings(string? environmentModel) =>
        new(AppSettings.Load(key => key switch
        {
            "DATABASE_URL" => "postgresql://u:p@h:5432/db",
            "API_SECRET_KEY" => "0123456789abcdef0123456789abcdef",
            "ENCRYPTION_KEY" => "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
            "GEMINI_API_KEY" => "g",
            "REDIS_URL" => "redis://localhost:6379",
            "GEMINI_MODEL" => environmentModel,
            _ => null,
        }));

    private async Task SetAsync(string key, string? value)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        if (value is null)
        {
            await connection.ExecuteAsync("DELETE FROM configuracoes WHERE chave = @key", new { key });
            return;
        }

        await connection.ExecuteAsync("UPDATE configuracoes SET valor = @value WHERE chave = @key", new { key, value });
    }

    [Fact]
    public async Task Seeded_ReturnsModelAndVersionFromTheTable()
    {
        var endpoint = await Provider().GetAsync(CancellationToken.None);

        endpoint.ShouldBe(new GeminiEndpoint("gemini-2.5-flash-lite", "v1beta"));
    }

    [Fact]
    public async Task ChangeInTheTable_AppliesOnlyAfterTheCacheExpires()
    {
        var provider = Provider();
        await provider.GetAsync(CancellationToken.None);

        await SetAsync("gemini.modelo", "gemini-2.5-flash");
        await SetAsync("gemini.versao_api", "v1");
        (await provider.GetAsync(CancellationToken.None)).Model.ShouldBe("gemini-2.5-flash-lite");

        _clock.Advance(DbGeminiEndpointProvider.CacheTtl + TimeSpan.FromSeconds(1));

        (await provider.GetAsync(CancellationToken.None)).ShouldBe(new GeminiEndpoint("gemini-2.5-flash", "v1"));
    }

    [Fact]
    public async Task MissingRows_FallBackToTheEnvironmentModelAndDefaultVersion()
    {
        await SetAsync("gemini.modelo", null);
        await SetAsync("gemini.versao_api", null);

        (await Provider("gemini-do-ambiente").GetAsync(CancellationToken.None)).ShouldBe(new GeminiEndpoint("gemini-do-ambiente", "v1beta"));
    }

    [Theory]
    [InlineData("gemini/../../x")]
    [InlineData("modelo com espaço")]
    [InlineData("a?key=1")]
    [InlineData("")]
    public async Task InvalidModel_IsIgnored_SoItNeverReachesTheUrl(string model)
    {
        await SetAsync("gemini.modelo", model);

        (await Provider().GetAsync(CancellationToken.None)).Model.ShouldBe("gemini-2.5-flash-lite");
    }

    [Theory]
    [InlineData("v1/../x")]
    [InlineData("1beta")]
    [InlineData("../v1")]
    public async Task InvalidVersion_IsIgnored(string version)
    {
        await SetAsync("gemini.versao_api", version);

        (await Provider().GetAsync(CancellationToken.None)).ApiVersion.ShouldBe("v1beta");
    }
}
