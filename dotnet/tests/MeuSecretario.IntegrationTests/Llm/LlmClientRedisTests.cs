using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Llm;

/// <summary>010c contra o Redis real: valor compatível com o <c>json.dumps</c> do Python, TTL, HIT e invalidação.</summary>
[Trait("Category", "Docker")]
public sealed class LlmClientRedisTests(RedisFixture redis) : IClassFixture<RedisFixture>, IDisposable
{
    private readonly RedisCache _cache = new(RedisConnectionOptions.Parse(redis.RedisUrl), TimeProvider.System, NullLogger<RedisCache>.Instance);
    private readonly ConnectionMultiplexer _mux = ConnectionMultiplexer.Connect(redis.ConnectionString);
    private readonly CountingTransport _transport = new();
    private readonly string _prompt = $"prompt-{Guid.NewGuid():N}";

    public void Dispose()
    {
        _mux.Dispose();
        _cache.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class FixedKeys : IProviderKeySelector
    {
        public Task<ProviderKeyResult> SelectAsync(int? userId, string provider, CancellationToken cancellationToken) =>
            Task.FromResult(ProviderKeyResult.WithKey("chave-de-teste"));
    }

    private sealed class CountingTransport : IGeminiTransport
    {
        public int Calls { get; private set; }

        public Task<string> GenerateAsync(string prompt, string apiKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult("Mercado ç 😀");
        }
    }

    private LlmClient Client(LlmCacheMetricsCollector? metrics = null) =>
        new(new FixedKeys(), _transport, _cache, metrics ?? new LlmCacheMetricsCollector(), new BrazilClock(TimeProvider.System), NullLogger<LlmClient>.Instance);

    private IDatabase Db() => _mux.GetDatabase();

    [Fact]
    [Trait("AC", "010-51")]
    public async Task Miss_StoresAReadableEntryWithTheTtl_AndTheNextCallIsAHit()
    {
        var policy = new LlmCachePolicy("category", TimeSpan.FromSeconds(2592000), true);
        var request = new LlmRequest(_prompt, 7, policy);
        var key = $"gemini_cache:category:7:{MeuSecretario.Domain.Llm.LlmCacheKey.Hash12(_prompt)}";
        var client = Client();

        (await client.GenerateTextAsync(request, default)).ShouldBe("Mercado ç 😀");
        (await client.GenerateTextAsync(request, default)).ShouldBe("Mercado ç 😀");

        _transport.Calls.ShouldBe(1);
        var db = Db();
        using var stored = JsonDocument.Parse((string)db.StringGet(key)!);
        stored.RootElement.GetProperty("text").GetString().ShouldBe("Mercado ç 😀");
        stored.RootElement.GetProperty("prompt_feedback").GetString().ShouldBe(string.Empty);
        stored.RootElement.GetProperty("cached_at").GetString().ShouldNotBeNullOrEmpty();
        db.KeyTimeToLive(key)!.Value.TotalSeconds.ShouldBeInRange(2591990, 2592000);
    }

    [Fact]
    [Trait("AC", "010-54")]
    public async Task EntryAsPythonWritesIt_IsAHit()
    {
        var policy = new LlmCachePolicy("intent", TimeSpan.FromSeconds(604800), false);
        var key = $"gemini_cache:intent:global:{MeuSecretario.Domain.Llm.LlmCacheKey.Hash12(_prompt)}";
        // json.dumps com ensure_ascii e separadores com espaço, como o redis_service do Flask grava.
        await Db().StringSetAsync(key, "{\"text\": \"Mercado \\u00e7\", \"prompt_feedback\": \"\", \"cached_at\": \"2026-10-02T10:00:00.123456\"}");
        var metrics = new LlmCacheMetricsCollector();

        (await Client(metrics).GenerateTextAsync(new LlmRequest(_prompt, null, policy), default)).ShouldBe("Mercado ç");

        _transport.Calls.ShouldBe(0);
        metrics.Snapshot().CacheHits.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task Admin_DeletesUserEntries_OnRealRedis()
    {
        var user = Random.Shared.Next(100_000, 900_000);
        var policy = new LlmCachePolicy("category", TimeSpan.FromHours(1), true);
        var client = Client();
        await client.GenerateTextAsync(new LlmRequest(_prompt + "a", user, policy), default);
        await client.GenerateTextAsync(new LlmRequest(_prompt + "b", user, policy), default);
        var admin = new LlmCacheAdmin(_cache, new LlmCacheMetricsCollector(), NullLogger<LlmCacheAdmin>.Instance);

        (await admin.InvalidateUserAsync(user, null, default)).ShouldBe(2);
        (await admin.InvalidateUserAsync(user, null, default)).ShouldBe(0);
    }
}
