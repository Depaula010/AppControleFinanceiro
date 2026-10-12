using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Health;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DATABASE_URL", "postgresql://u:p@localhost:5432/db");
            builder.UseSetting("API_SECRET_KEY", "0123456789abcdef0123456789abcdef");
            builder.UseSetting("ENCRYPTION_KEY", "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=");
            builder.UseSetting("GEMINI_API_KEY", "gemini-de-teste");
            builder.UseSetting("REDIS_URL", "redis://localhost:6379");
            builder.UseSetting("LOG_TO_FILE", "false");
        });
    }

    private HttpClient ClientWithDatabase(bool connected)
    {
        var status = connected ? new DatabaseStatus(true) : new DatabaseStatus(false, "NpgsqlException");
        return _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<IDatabaseHealth>(new FakeDatabaseHealth(status))))
            .CreateClient();
    }

    private sealed class FakeDatabaseHealth(DatabaseStatus status) : IDatabaseHealth
    {
        public Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(status);
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Root_WhenDatabaseIsUp_ReportsConnected()
    {
        var response = await ClientWithDatabase(true).GetAsync("/");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("status").GetString().ShouldBe("online");
        body.GetProperty("service").GetString().ShouldBe("Meu Secretário API");
        body.GetProperty("database").GetString().ShouldBe("✅ Conectado");
        body.GetProperty("timestamp").GetString()!.ShouldMatch(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}$");
    }

    [Fact]
    public async Task Root_WhenDatabaseIsDown_StillReturns200WithErrorLabel()
    {
        var response = await ClientWithDatabase(false).GetAsync("/");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await ReadJson(response)).GetProperty("database").GetString().ShouldBe("❌ Erro: NpgsqlException");
    }

    [Fact]
    public async Task ApiHealth_ReturnsEnvelopeA()
    {
        var response = await ClientWithDatabase(true).GetAsync("/api/health");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        var body = await ReadJson(response);
        body.GetProperty("status").GetString().ShouldBe("success");
        body.GetProperty("message").GetString().ShouldBe("API está funcionando");
        body.GetProperty("version").GetString().ShouldBe("1.0.0");
    }

    [Fact]
    public async Task TrailingSlash_Returns404LikeFlask()
    {
        var response = await ClientWithDatabase(true).GetAsync("/api/health/");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await ClientWithDatabase(true).GetAsync("/nao-existe");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
    }
}
