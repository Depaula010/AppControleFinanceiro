using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure;
using MeuSecretario.Infrastructure.ApiKeys;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Infrastructure.Llm;

public class GeminiPipelineTests
{
    private const string Ok = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"ok\"}]}}]}";

    private static readonly Dictionary<string, string?> Env = new()
    {
        ["DATABASE_URL"] = "postgresql://u:p@h:5432/db",
        ["API_SECRET_KEY"] = "0123456789abcdef0123456789abcdef",
        ["ENCRYPTION_KEY"] = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=",
        ["GEMINI_API_KEY"] = "gemini-do-sistema",
        ["REDIS_URL"] = "redis://redis:6379",
    };

    private static ServiceProvider Build(FakeHttpHandler handler, GeminiResilienceSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(key => Env.GetValueOrDefault(key));
        services.AddLogging();
        services.AddApiKeysInfrastructure();
        if (settings is not null)
        {
            services.AddSingleton(settings);
        }

        services.AddHttpClient(GeminiHttpOptions.ClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = false, ValidateScopes = true });
    }

    private static readonly GeminiResilienceSettings Fast = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10));

    private static Task<string> Generate(ServiceProvider provider) =>
        provider.GetRequiredService<IGeminiTransport>().GenerateAsync("p", "k", CancellationToken.None);

    [Fact]
    [Trait("AC", "010-49")]
    public void Defaults_Are45sPerAttempt_90sTotal_1sRetryDelay()
    {
        GeminiResilienceSettings.Default.ShouldBe(new GeminiResilienceSettings(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(1)));
        GeminiHttpOptions.MaxRetries.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-49")]
    public async Task Container_RegistersTheClientsAndNoKeyHeaderOnTheHttpClient()
    {
        await using var provider = Build(new FakeHttpHandler());

        provider.GetRequiredService<ILlmClient>().ShouldBeOfType<LlmClient>();
        provider.GetRequiredService<ILlmCacheAdmin>().ShouldBeOfType<LlmCacheAdmin>();
        provider.GetRequiredService<IGeminiTransport>().ShouldBeOfType<GeminiHttpTransport>();
        provider.GetRequiredService<ILlmClient>().ShouldBeSameAs(provider.GetRequiredService<ILlmClient>());
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GeminiHttpOptions.ClientName);
        client.DefaultRequestHeaders.ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-48")]
    public async Task Status503ThenOk_Succeeds_WithTwoRequests()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.ServiceUnavailable, "{}").Enqueue(HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, Fast);

        (await Generate(provider)).ShouldBe("ok");

        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldAllBe(r => r.Headers["x-goog-api-key"] == "k");
    }

    [Fact]
    [Trait("AC", "010-48")]
    public async Task Status504ThenOk_Succeeds()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.GatewayTimeout, "{}").Enqueue(HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, Fast);

        (await Generate(provider)).ShouldBe("ok");
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-48")]
    public async Task Status503Twice_FailsWithLlmException_AfterExactlyTwoRequests()
    {
        var handler = new FakeHttpHandler().Enqueue(HttpStatusCode.ServiceUnavailable, "{}");
        await using var provider = Build(handler, Fast);

        var ex = await Should.ThrowAsync<LlmException>(() => Generate(provider));

        ex.GetType().ShouldBe(typeof(LlmException));
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-48")]
    public async Task NetworkFailureThenOk_RetriesOnce()
    {
        var handler = new FakeHttpHandler().EnqueueException(new HttpRequestException("rede")).Enqueue(HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, Fast);

        (await Generate(provider)).ShouldBe("ok");
        handler.Requests.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [Trait("AC", "010-48")]
    public async Task NonTransientStatus_IsNeverRetried(int status)
    {
        var handler = new FakeHttpHandler().Enqueue((HttpStatusCode)status, "{}");
        await using var provider = Build(handler, Fast);

        await Should.ThrowAsync<LlmException>(() => Generate(provider));

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-49")]
    public async Task AttemptTimeout_CutsAHangingAttempt_AndRetriesOnce()
    {
        var handler = new FakeHttpHandler().EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, new GeminiResilienceSettings(TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10)));

        await Should.ThrowAsync<LlmException>(() => Generate(provider));

        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-49")]
    public async Task TotalTimeout_CapsTheWholeCall()
    {
        var handler = new FakeHttpHandler().EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, new GeminiResilienceSettings(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(10)));

        await Should.ThrowAsync<LlmException>(() => Generate(provider));

        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task CallerCancellation_IsNotWrapped()
    {
        var handler = new FakeHttpHandler().EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK, Ok);
        await using var provider = Build(handler, Fast);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(
            () => provider.GetRequiredService<IGeminiTransport>().GenerateAsync("p", "k", cts.Token));
    }
}
