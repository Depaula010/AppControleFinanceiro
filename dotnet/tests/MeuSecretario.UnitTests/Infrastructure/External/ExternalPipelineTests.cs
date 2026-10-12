using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.ApiKeys;
using MeuSecretario.Infrastructure.External;
using MeuSecretario.Infrastructure.External.Routes;
using MeuSecretario.Infrastructure.External.Weather;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Infrastructure.External;

public class ExternalPipelineTests
{
    private static readonly Dictionary<string, string?> Env = new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini-do-sistema",
        ["WEATHER_API_KEY"] = "clima-do-sistema",
        ["OPENROUTE_API_KEY"] = "rota-do-sistema",
        ["REDIS_URL"] = "redis://redis:6379",
    };

    private static ServiceProvider Build(string clientName, FakeHttpHandler handler)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(key => Env.GetValueOrDefault(key));
        services.AddLogging();
        services.AddApiKeysInfrastructure();
        services.AddHttpClient(clientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });
    }

    [Fact]
    [Trait("AC", "010-122")]
    public async Task Container_ResolvesThePortsWithoutKeyHeadersOnTheClients()
    {
        using var handler = new FakeHttpHandler();
        await using var provider = Build(ExternalHttpOptions.WeatherClient, handler);

        provider.GetRequiredService<IWeatherClient>().ShouldBeOfType<WeatherApiClient>();
        provider.GetRequiredService<IRouteClient>().ShouldBeOfType<OpenRouteClient>();
        provider.GetRequiredService<IProviderKeyValidator>().ShouldBeOfType<ProviderKeyValidator>();
        provider.GetRequiredService<ITravelRateLimiter>().ShouldBeOfType<TravelRateLimiter>();
        provider.GetRequiredService<ITravelCounterStore>().ShouldBeOfType<RedisTravelCounterStore>();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        foreach (var name in new[] { ExternalHttpOptions.WeatherClient, ExternalHttpOptions.RouteClient, ExternalHttpOptions.KeyValidationClient })
        {
            using var client = factory.CreateClient(name);
            client.DefaultRequestHeaders.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Limits_AreFiveSecondsForWeatherAndValidation_FifteenForOpenRoute_TwoAttempts()
    {
        ExternalHttpOptions.WeatherTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        ExternalHttpOptions.ValidationTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        ExternalHttpOptions.RouteTimeout.ShouldBe(TimeSpan.FromSeconds(15));
        ExternalHttpOptions.RouteMaxAttempts.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-112")]
    public async Task Weather_HangingRequest_IsCutAtFiveSeconds_AndReturnsNull()
    {
        using var handler = new FakeHttpHandler().EnqueueDelayed(TimeSpan.FromSeconds(60), HttpStatusCode.OK, "{}");
        await using var provider = Build(ExternalHttpOptions.WeatherClient, handler);

        var started = DateTime.UtcNow;
        var result = await provider.GetRequiredService<IWeatherClient>().GetWeatherAsync("Santos", "SP", null, default);

        result.ShouldBeNull();
        (DateTime.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(20));
    }

    [Fact]
    [Trait("AC", "010-119")]
    public async Task OpenRoute_ThroughTheRealPipeline_RetriesA500Once()
    {
        using var handler = new FakeHttpHandler()
            .Enqueue(HttpStatusCode.InternalServerError, "{}")
            .Enqueue(HttpStatusCode.OK, "{\"routes\":[{\"summary\":{\"duration\":600,\"distance\":5000}}]}");
        await using var provider = Build(ExternalHttpOptions.RouteClient, handler);

        var result = await provider.GetRequiredService<IRouteClient>().CalculateTravelTimeAsync(new Coordinates(-23.5, -46.6), new Coordinates(-23.6, -46.7), null, default);

        result.ShouldBe(new TravelResult(10, 5.0, null));
        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldAllBe(r => r.Headers["Authorization"] == "rota-do-sistema");
    }
}
