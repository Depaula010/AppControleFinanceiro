using System.Text.Json;
using MeuSecretario.Infrastructure.Caching;

namespace MeuSecretario.UnitTests.Infrastructure.Caching;

public class CacheJsonTests
{
    private sealed record Payload(
        [property: System.Text.Json.Serialization.JsonPropertyName("text")] string Text,
        [property: System.Text.Json.Serialization.JsonPropertyName("prompt_feedback")] string? PromptFeedback,
        [property: System.Text.Json.Serialization.JsonPropertyName("cached_at")] string CachedAt);

    private static JsonElement Fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "redis-bot-fixtures.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    [Fact]
    [Trait("AC", "010-02")]
    public void Deserialize_ValuesWrittenByPython_HaveTheRightTypes()
    {
        CacheJson.Deserialize<string>("\"abc\"").ShouldBe("abc");
        CacheJson.Deserialize<bool>("true").ShouldBeTrue();
        CacheJson.Deserialize<int>("3").ShouldBe(3);
        CacheJson.Deserialize<int>("\"3\"").ShouldBe(3);

        var element = CacheJson.ParseElement("{\"text\": \"a\\u00e7\\u00e3o\", \"x\": [1, 2]}");
        element.ShouldNotBeNull();
        element.Value.GetProperty("text").GetString().ShouldBe("ação");
        element.Value.GetProperty("x").GetArrayLength().ShouldBe(2);
    }

    [Theory]
    [Trait("AC", "010-03")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("running")]
    [InlineData("not json {")]
    [InlineData("null")]
    public void Deserialize_AbsentEmptyRawOrNull_ReturnsNullWithoutThrowing(string? raw)
    {
        CacheJson.Deserialize<string>(raw).ShouldBeNull();
        CacheJson.Deserialize<Payload>(raw).ShouldBeNull();
        CacheJson.ParseElement(raw).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-03")]
    public void Fixtures_RawNonJsonTexts_ReturnNull()
    {
        foreach (var raw in Fixtures().GetProperty("raw_non_json").EnumerateArray())
        {
            CacheJson.Deserialize<string>(raw.GetString()).ShouldBeNull();
        }
    }

    [Fact]
    [Trait("AC", "010-13")]
    public void Fixtures_PythonJsonDumps_AreReadAndRewrittenToEquivalentJson()
    {
        foreach (var entry in Fixtures().GetProperty("redis_json").EnumerateArray())
        {
            var pythonJson = entry.GetProperty("json").GetString()!;
            var expected = entry.GetProperty("value");

            // Ida: o que o .NET grava é lido igual por um parser JSON (o json.loads do Python faz o mesmo).
            var written = CacheJson.Serialize(expected);
            using var rewritten = JsonDocument.Parse(written);
            JsonSerializer.Serialize(rewritten.RootElement).ShouldBe(JsonSerializer.Serialize(expected));

            // Volta: o que o Python gravou é lido pelo .NET.
            using var read = JsonDocument.Parse(pythonJson);
            JsonSerializer.Serialize(read.RootElement).ShouldBe(JsonSerializer.Serialize(expected));
            written.ShouldNotContain("\n");
        }
    }

    [Fact]
    [Trait("AC", "010-13")]
    public void Serialize_AccentsAndEmoji_AreEscapedAsAsciiLikeEnsureAscii()
    {
        var json = CacheJson.Serialize("ação 😀");

        json.ShouldAllBe(c => c < 128);
        CacheJson.Deserialize<string>(json).ShouldBe("ação 😀");
        // Vetor literal do json.dumps do Python (caixa baixa dos escapes), lido pelo .NET:
        CacheJson.Deserialize<string>("\"a\\u00e7\\u00e3o \\ud83d\\ude00\"").ShouldBe("ação 😀");
    }

    [Fact]
    [Trait("AC", "010-12")]
    public void Serialize_StringHoldingJson_IsDoubleEncodedLikeThePythonSecurityFilter()
    {
        var json = CacheJson.Serialize("{\"a\":1}");

        // O .NET escapa aspas como " (JSON válido); o json.loads do Python devolve o mesmo texto do json.dumps ("{\"a\":1}").
        json.ShouldBe("\"{\\u0022a\\u0022:1}\"");
        CacheJson.Deserialize<string>(json).ShouldBe("{\"a\":1}");
        CacheJson.Deserialize<string>("\"{\\\"a\\\":1}\"").ShouldBe("{\"a\":1}");
    }

    [Fact]
    public void Serialize_KeepsPropertyNamesAsDeclared_AndRoundTrips()
    {
        var value = new Payload("ok", null, "2026-10-02T10:00:00");

        var json = CacheJson.Serialize(value);

        json.ShouldBe("{\"text\":\"ok\",\"prompt_feedback\":null,\"cached_at\":\"2026-10-02T10:00:00\"}");
        CacheJson.Deserialize<Payload>(json).ShouldBe(value);
    }

    [Fact]
    public void Serialize_Dictionary_KeepsKeysUntouched()
    {
        CacheJson.Serialize(new Dictionary<string, object> { ["snake_case"] = 1, ["Pascal"] = true })
            .ShouldBe("{\"snake_case\":1,\"Pascal\":true}");
    }
}
