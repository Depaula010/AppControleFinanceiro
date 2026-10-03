using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Llm;

namespace MeuSecretario.UnitTests.Domain.Llm;

public class LlmDomainTests
{
    [Fact]
    [Trait("AC", "010-50")]
    public void CacheKey_AbcGlobalIntent_MatchesThePythonVector()
    {
        LlmCacheKey.Hash12("abc").ShouldBe("900150983cd2");
        LlmCacheKey.Build("intent", null, false, "abc").ShouldBe("gemini_cache:intent:global:900150983cd2");
    }

    [Fact]
    [Trait("AC", "010-50")]
    public void CacheKey_UserSpecific_UsesTheUserId_ButGlobalIgnoresIt()
    {
        LlmCacheKey.Build("category", 7, true, "abc").ShouldBe("gemini_cache:category:7:900150983cd2");
        LlmCacheKey.Build("intent", 7, false, "abc").ShouldBe("gemini_cache:intent:global:900150983cd2");
        LlmCacheKey.Build("category", 0, true, "abc").ShouldBe("gemini_cache:category:global:900150983cd2");
    }

    [Fact]
    [Trait("AC", "010-50")]
    public void CacheKey_MatchesEveryKeyGeneratedByPython()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "gemini-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = document.RootElement.GetProperty("cache_keys").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(4);
        foreach (var c in cases)
        {
            int? userId = c.GetProperty("user_id").ValueKind == JsonValueKind.Null ? null : c.GetProperty("user_id").GetInt32();
            var key = LlmCacheKey.Build(c.GetProperty("prefix").GetString()!, userId, userId is not null, c.GetProperty("prompt").GetString()!);
            key.ShouldBe(c.GetProperty("key").GetString());
        }
    }

    [Theory]
    [InlineData("intent", 604800)]
    [InlineData("category", 2592000)]
    [InlineData("extract_trans", 86400)]
    [InlineData("period_query", 604800)]
    [InlineData("calendar_query", 604800)]
    [InlineData("chart_type", 604800)]
    [InlineData("fatura_query", 604800)]
    [InlineData("saldo_query", 604800)]
    [InlineData("qualquer", 259200)]
    [Trait("AC", "010-51")]
    public void Kinds_TtlPerPrefix(string prefix, int seconds) =>
        LlmCacheKinds.TtlFor(prefix).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Fact]
    [Trait("AC", "010-51")]
    public void Policies_MatchTheFiveCachedFunctionsOfThePython()
    {
        LlmCachePolicies.Intent.ShouldBe(new LlmCachePolicy("intent", TimeSpan.FromSeconds(604800), false));
        LlmCachePolicies.Category.ShouldBe(new LlmCachePolicy("category", TimeSpan.FromSeconds(2592000), true));
        LlmCachePolicies.ExtractTransaction.ShouldBe(new LlmCachePolicy("extract_trans", TimeSpan.FromSeconds(86400), false));
        LlmCachePolicies.PeriodQuery.ShouldBe(new LlmCachePolicy("period_query", TimeSpan.FromSeconds(604800), false));
        LlmCachePolicies.ChartType.ShouldBe(new LlmCachePolicy("chart_type", TimeSpan.FromSeconds(604800), false));
    }

    [Fact]
    public void Kinds_MetricKey_UnknownPrefixCountsAsOther()
    {
        LlmCacheKinds.MetricKey("intent").ShouldBe("intent");
        LlmCacheKinds.MetricKey("default").ShouldBe("other");
        LlmCacheKinds.MetricPrefixes.Count.ShouldBe(9);
    }

    [Theory]
    [InlineData("```json\n{\"a\":1}\n```")]
    [InlineData("{\"a\":1}")]
    [InlineData("  {\"a\":1}  ")]
    [InlineData("```\n{\"a\":1}\n```")]
    [Trait("AC", "010-58")]
    public void LlmJson_ParsesWithAndWithoutCodeFences(string text) =>
        LlmJson.Parse(text).GetProperty("a").GetInt32().ShouldBe(1);

    [Fact]
    [Trait("AC", "010-58")]
    public void LlmJson_NotJson_ThrowsJsonException() =>
        Should.Throw<JsonException>(() => LlmJson.Parse("isto não é json"));
}
