using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Integrations;
using MeuSecretario.Infrastructure.Bot;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Infrastructure.Llm;

public class LlmClientTests
{
    private static readonly LlmCachePolicy Intent = new("intent", TimeSpan.FromSeconds(604800), false);
    private static readonly LlmCachePolicy Category = new("category", TimeSpan.FromSeconds(2592000), true);

    private readonly List<string> _order = [];
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 2, 15, 30, 45, 123, TimeSpan.Zero));
    private readonly IApiKeyProvider _apiKeys = Substitute.For<IApiKeyProvider>();
    private readonly IGeminiTransport _transport = Substitute.For<IGeminiTransport>();
    private readonly FakeIntegrationSettings _settings = new() { GeminiApiKey = "chave-do-sistema" };
    private readonly ListLogger<LlmClient> _log = new();

    public LlmClientTests()
    {
        Cache = new InMemoryCache(_time);
        Metrics = new LlmCacheMetricsCollector();
        _apiKeys.ResolveAsync(Arg.Any<int>(), "gemini", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _order.Add("key");
            return Task.FromResult<ApiKeyResolution>(new ApiKeyResolution.Resolved("chave-do-usuario", "own"));
        });
        _transport.GenerateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _order.Add("http");
            return Task.FromResult("resposta");
        });
        Client = new LlmClient(new ProviderKeySelector(_apiKeys, _settings), _transport, Cache, Metrics, new BrazilClock(_time), _log);
    }

    private InMemoryCache Cache { get; }

    private LlmCacheMetricsCollector Metrics { get; }

    private LlmClient Client { get; }

    private Task<string> Call(string prompt = "abc", int? userId = null, LlmCachePolicy? cache = null) =>
        Client.GenerateTextAsync(new LlmRequest(prompt, userId, cache), CancellationToken.None);

    private int HttpCalls => _transport.ReceivedCalls().Count();

    [Fact]
    [Trait("AC", "010-43")]
    public async Task NullUser_UsesTheSystemKey_WithoutAskingTheApiKeyProvider()
    {
        (await Call()).ShouldBe("resposta");

        await _transport.Received(1).GenerateAsync("abc", "chave-do-sistema", Arg.Any<CancellationToken>());
        await _apiKeys.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default);
    }

    [Fact]
    [Trait("AC", "010-43")]
    public async Task User_UsesTheResolvedKey()
    {
        await Call(userId: 7);

        await _transport.Received(1).GenerateAsync("abc", "chave-do-usuario", Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("AC", "010-43")]
    public async Task KeyFailure_ThrowsWithTheLiteralMessage_AndNoHttpRequest()
    {
        _apiKeys.ResolveAsync(7, "gemini", Arg.Any<CancellationToken>())
            .Returns(new ApiKeyResolution.Failed(ApiKeyFailure.LimitReached, "Limite do plano atingido: texto do 008"));

        var ex = await Should.ThrowAsync<LlmKeyUnavailableException>(() => Call(userId: 7));

        ex.Message.ShouldBe("Limite do plano atingido: texto do 008");
        HttpCalls.ShouldBe(0);
    }

    [Fact]
    public async Task EmptySystemKey_ThrowsNotConfigured_WithoutHttp()
    {
        _settings.GeminiApiKey = string.Empty;

        var ex = await Should.ThrowAsync<LlmException>(() => Call());

        ex.Message.ShouldBe("Modelo Gemini não configurado.");
        HttpCalls.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-44")]
    public async Task Key_IsResolvedBeforeTheCache_EvenOnAHit()
    {
        await Call("p", 7, Intent);
        _order.Clear();

        (await Call("p", 7, Intent)).ShouldBe("resposta");

        _order.ShouldBe(["key"]);
    }

    [Fact]
    [Trait("AC", "010-52")]
    public async Task Miss_CallsOnce_StoresTheEntryAndCounts()
    {
        var text = await Call("abc", null, Intent);

        text.ShouldBe("resposta");
        HttpCalls.ShouldBe(1);
        var key = "gemini_cache:intent:global:900150983cd2";
        using var stored = JsonDocument.Parse(Cache.RawValue(key)!);
        stored.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["text", "prompt_feedback", "cached_at"]);
        stored.RootElement.GetProperty("text").GetString().ShouldBe("resposta");
        stored.RootElement.GetProperty("prompt_feedback").GetString().ShouldBe(string.Empty);
        stored.RootElement.GetProperty("cached_at").GetString().ShouldBe("2026-10-02T12:30:45.123000");
        var m = Metrics.Snapshot();
        (m.CacheMisses, m.CacheSaves, m.CacheHits).ShouldBe((1, 1, 0));
    }

    [Fact]
    [Trait("AC", "010-51")]
    public async Task Ttl_IsAppliedToTheStoredEntry()
    {
        await Call("abc", null, Intent);
        await Call("abc", 7, Category);
        await Call("abc", null, new LlmCachePolicy("desconhecido", TimeSpan.FromSeconds(259200), false));

        Cache.TtlOf("gemini_cache:intent:global:900150983cd2")!.Value.TotalSeconds.ShouldBe(604800, 1);
        Cache.TtlOf("gemini_cache:category:7:900150983cd2")!.Value.TotalSeconds.ShouldBe(2592000, 1);
        Cache.TtlOf("gemini_cache:desconhecido:global:900150983cd2")!.Value.TotalSeconds.ShouldBe(259200, 1);
    }

    [Fact]
    [Trait("AC", "010-52")]
    public async Task Hit_MakesNoRequest_AndCountsByPrefixAndOther()
    {
        await Call("abc", null, Intent);
        await Call("abc", null, Intent);
        await Call("x", null, new LlmCachePolicy("novo", TimeSpan.FromHours(1), false));
        await Call("x", null, new LlmCachePolicy("novo", TimeSpan.FromHours(1), false));

        HttpCalls.ShouldBe(2);
        var m = Metrics.Snapshot();
        m.CacheHits.ShouldBe(2);
        m.BreakdownByType["intent"].Hits.ShouldBe(1);
        m.BreakdownByType["other"].Hits.ShouldBe(1);
        m.BreakdownByType["other"].Misses.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-52")]
    public async Task RedisDown_CallsDirectly_WithoutError()
    {
        Cache.Unavailable = true;

        (await Call("abc", null, Intent)).ShouldBe("resposta");
        (await Call("abc", null, Intent)).ShouldBe("resposta");

        HttpCalls.ShouldBe(2);
        Metrics.Snapshot().TotalRequests.ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-53")]
    public async Task GeminiError_IsRethrown_NotCached_AndCountedAsError()
    {
        _transport.GenerateAsync(default!, default!, default).ReturnsForAnyArgs<Task<string>>(_ => throw new LlmBlockedException("bloqueada"));

        await Should.ThrowAsync<LlmBlockedException>(() => Call("abc", null, Intent));

        Cache.RawValue("gemini_cache:intent:global:900150983cd2").ShouldBeNull();
        var m = Metrics.Snapshot();
        (m.CacheErrors, m.CacheMisses, m.CacheSaves).ShouldBe((1, 0, 0));
    }

    [Fact]
    [Trait("AC", "010-54")]
    public async Task EntryWrittenByPython_IsAHit()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gemini-fixtures.json");
        using var fixture = JsonDocument.Parse(File.ReadAllText(path));
        var entry = fixture.RootElement.GetProperty("cache_entries")[0];
        await Cache.SetIfNotExistsAsync("gemini_cache:intent:global:900150983cd2", entry.GetProperty("json").GetString()!, TimeSpan.FromHours(1));

        (await Call("abc", null, Intent)).ShouldBe("Mercado ç");

        HttpCalls.ShouldBe(0);
        Metrics.Snapshot().CacheHits.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-55")]
    public async Task NullCachePolicy_NeverTouchesTheCache()
    {
        var cache = Substitute.For<ICache>();
        var client = new LlmClient(new ProviderKeySelector(_apiKeys, _settings), _transport, cache, Metrics, new BrazilClock(_time), _log);

        await client.GenerateTextAsync(new LlmRequest("abc"), CancellationToken.None);

        cache.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-50")]
    public async Task UserSpecificWithoutUser_ThrowsArgumentException()
    {
        var ex = await Should.ThrowAsync<ArgumentException>(() => Call("abc", null, Category));

        ex.Message.ShouldStartWith("user_specific cache requires usuario_id");
        HttpCalls.ShouldBe(0);
    }

    [Fact]
    public async Task CacheReadFailure_IsSwallowedAndCounted_ThenCallsGemini()
    {
        var cache = Substitute.For<ICache>();
        cache.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        cache.GetJsonElementAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns<Task<JsonElement?>>(_ => throw new InvalidOperationException("boom"));
        cache.SetJsonAsync(Arg.Any<string>(), Arg.Any<Dictionary<string, string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        var client = new LlmClient(new ProviderKeySelector(_apiKeys, _settings), _transport, cache, Metrics, new BrazilClock(_time), _log);

        (await client.GenerateTextAsync(new LlmRequest("abc", null, Intent), CancellationToken.None)).ShouldBe("resposta");

        var m = Metrics.Snapshot();
        (m.CacheErrors, m.CacheMisses, m.CacheSaves).ShouldBe((1, 1, 0));
    }

    [Fact]
    [Trait("AC", "010-61")]
    public async Task Logs_NeverContainPromptResponseOrKey()
    {
        _transport.GenerateAsync(default!, default!, default).ReturnsForAnyArgs(Task.FromResult("RESPOSTA-SECRETA"));
        await Call("PROMPT-SECRETO", 7, Intent);
        await Call("PROMPT-SECRETO", 7, Intent);
        _transport.GenerateAsync(default!, default!, default).ReturnsForAnyArgs<Task<string>>(_ => throw new LlmException("falha com PROMPT-SECRETO"));
        await Should.ThrowAsync<LlmException>(() => Call("outro PROMPT-SECRETO", 7));

        _log.All.ShouldNotBeNullOrEmpty();
        _log.All.ShouldNotContain("PROMPT-SECRETO");
        _log.All.ShouldNotContain("RESPOSTA-SECRETA");
        _log.All.ShouldNotContain("chave-do-usuario");
        _log.All.ShouldNotContain("chave-do-sistema");
    }

    [Fact]
    [Trait("AC", "010-57")]
    public void Metrics_EmptyAndAfterOneHitOneMiss()
    {
        var empty = Metrics.Snapshot();
        empty.HitRate.ShouldBe("0.0%");
        empty.TotalRequests.ShouldBe(0);
        empty.BreakdownByType.Keys.ShouldBe(["intent", "category", "extract_trans", "period_query", "calendar_query", "chart_type", "fatura_query", "saldo_query", "other"]);
        empty.BreakdownByType["intent"].HitRate.ShouldBe("0.0%");

        Metrics.RecordMiss("intent");
        Metrics.RecordHit("intent");

        var m = Metrics.Snapshot();
        m.HitRate.ShouldBe("50.0%");
        m.TotalRequests.ShouldBe(2);
        m.EstimatedSavings.ShouldBe(new LlmCacheSavings(1, "50.0%"));
        m.BreakdownByType["intent"].ShouldBe(new LlmCacheTypeMetrics(1, 1, 2, "50.0%"));
    }
}
