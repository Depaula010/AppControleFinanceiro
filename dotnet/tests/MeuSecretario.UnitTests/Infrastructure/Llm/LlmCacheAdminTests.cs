using MeuSecretario.Application.Abstractions;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.UnitTests.TestSupport;
using NSubstitute;

namespace MeuSecretario.UnitTests.Infrastructure.Llm;

public class LlmCacheAdminTests
{
    private static readonly string[] Seed =
    [
        "gemini_cache:category:7:aaaaaaaaaaaa",
        "gemini_cache:category:8:bbbbbbbbbbbb",
        "gemini_cache:intent:global:cccccccccccc",
        "gemini_cache:intent:7:dddddddddddd",
        "gemini_cache:extract_trans:global:eeeeeeeeeeee",
        "outra_chave:7",
    ];

    private readonly InMemoryCache _cache = new();

    private async Task<LlmCacheAdmin> Admin()
    {
        foreach (var key in Seed)
        {
            await _cache.SetJsonAsync(key, "v", TimeSpan.FromHours(1));
        }

        return new LlmCacheAdmin(_cache, new LlmCacheMetricsCollector(), new ListLogger<LlmCacheAdmin>());
    }

    private async Task<string[]> Remaining() =>
        [.. (await _cache.KeysByPatternAsync("*")).Order(StringComparer.Ordinal)];

    [Fact]
    [Trait("AC", "010-56")]
    public async Task InvalidateUser_WithPattern_DeletesOnlyThatTypeForThatUser()
    {
        var admin = await Admin();

        // Quirk do Python mantido: "category:*" vira gemini_cache:category:*:7:* e não casa com category:7:hash.
        (await admin.InvalidateUserAsync(7, "category:*", CancellationToken.None)).ShouldBe(0);
        (await admin.InvalidateUserAsync(7, "category", CancellationToken.None)).ShouldBe(1);

        (await Remaining()).ShouldNotContain("gemini_cache:category:7:aaaaaaaaaaaa");
        (await Remaining()).Length.ShouldBe(5);
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task InvalidateUser_PatternIsInsertedBetweenRootAndUserId()
    {
        var cache = Substitute.For<ICache>();
        cache.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        cache.KeysByPatternAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<string>>([]));
        var admin = new LlmCacheAdmin(cache, new LlmCacheMetricsCollector(), new ListLogger<LlmCacheAdmin>());

        await admin.InvalidateUserAsync(7, "category:*", CancellationToken.None);
        await admin.InvalidateUserAsync(7, null, CancellationToken.None);
        await admin.InvalidatePatternAsync("intent:*", CancellationToken.None);
        await admin.InvalidatePatternAsync("*", CancellationToken.None);

        Received.InOrder(() =>
        {
            cache.KeysByPatternAsync("gemini_cache:category:*:7:*", Arg.Any<CancellationToken>());
            cache.KeysByPatternAsync("gemini_cache:*:7:*", Arg.Any<CancellationToken>());
            cache.KeysByPatternAsync("gemini_cache:intent:*:*", Arg.Any<CancellationToken>());
            cache.KeysByPatternAsync("gemini_cache:*:*", Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task InvalidateUser_WithoutPattern_DeletesEveryTypeOfThatUser()
    {
        var admin = await Admin();

        (await admin.InvalidateUserAsync(7, null, CancellationToken.None)).ShouldBe(2);

        (await Remaining()).ShouldBe(
        [
            "gemini_cache:category:8:bbbbbbbbbbbb",
            "gemini_cache:extract_trans:global:eeeeeeeeeeee",
            "gemini_cache:intent:global:cccccccccccc",
            "outra_chave:7",
        ]);
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task InvalidatePattern_Intent_DeletesGlobalAndUserEntries()
    {
        var admin = await Admin();

        (await admin.InvalidatePatternAsync("intent:*", CancellationToken.None)).ShouldBe(2);
        (await Remaining()).Length.ShouldBe(4);
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task InvalidatePattern_Star_DeletesAllGeminiKeysOnly()
    {
        var admin = await Admin();

        (await admin.InvalidatePatternAsync("*", CancellationToken.None)).ShouldBe(5);

        (await Remaining()).ShouldBe(["outra_chave:7"]);
    }

    [Fact]
    [Trait("AC", "010-56")]
    public async Task RedisDown_ReturnsZero()
    {
        var admin = await Admin();
        _cache.Unavailable = true;

        (await admin.InvalidateUserAsync(7, null, CancellationToken.None)).ShouldBe(0);
        (await admin.InvalidatePatternAsync("*", CancellationToken.None)).ShouldBe(0);
    }

    [Fact]
    [Trait("AC", "010-57")]
    public void GetMetrics_ReturnsTheCollectorSnapshot()
    {
        var metrics = new LlmCacheMetricsCollector();
        metrics.RecordHit("intent");
        var admin = new LlmCacheAdmin(_cache, metrics, new ListLogger<LlmCacheAdmin>());

        admin.GetMetrics().CacheHits.ShouldBe(1);
    }
}
