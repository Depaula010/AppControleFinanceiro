using System.Text.Json;
using MeuSecretario.Domain.Weather;

namespace MeuSecretario.UnitTests.Domain.Weather;

public class WeatherDomainTests
{
    private static JsonElement Fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "weather-routes-fixtures.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    [Trait("AC", "010-111")]
    public void Translate_EnglishText_MatchesAll22EntriesOfThePythonMap()
    {
        var entries = Fixtures().GetProperty("translations").EnumerateArray().ToList();
        entries.Count.ShouldBe(22);
        WeatherConditions.Entries.Count.ShouldBe(22);
        foreach (var e in entries)
        {
            var result = WeatherConditions.Translate(e.GetProperty("english").GetString(), null, null);
            result.Name.ShouldBe(e.GetProperty("portuguese").GetString());
            result.Emoji.ShouldBe(e.GetProperty("emoji").GetString());
        }
    }

    [Fact]
    [Trait("AC", "010-111")]
    public void Translate_PortugueseText_GetsTheEmojiOfThePythonMap_Deviation054()
    {
        foreach (var e in Fixtures().GetProperty("translations").EnumerateArray())
        {
            var result = WeatherConditions.Translate(e.GetProperty("portuguese").GetString()!.ToUpperInvariant(), null, null);
            result.Name.ShouldBe(e.GetProperty("portuguese").GetString());
            result.Emoji.ShouldBe(e.GetProperty("emoji").GetString());
        }
    }

    [Theory]
    [InlineData(1000, true, "Ensolarado")]
    [InlineData(1000, false, "Céu limpo")]
    [InlineData(1003, null, "Parcialmente nublado")]
    [InlineData(1189, null, "Chuva moderada")]
    [InlineData(1276, null, "Chuva forte com trovoada")]
    [InlineData(1135, null, "Neblina")]
    [InlineData(1237, null, "Granizo")]
    public void Translate_UnknownTextButKnownCode_UsesTheCode(int code, bool? isDay, string expectedName)
    {
        var result = WeatherConditions.Translate("Texto desconhecido", code, isDay);

        result.Name.ShouldBe(expectedName);
        result.Emoji.ShouldNotBe(WeatherConditions.FallbackEmoji);
    }

    [Fact]
    [Trait("AC", "010-111")]
    public void Translate_Unknown_KeepsTheApiTextWithTheThermometer()
    {
        var result = WeatherConditions.Translate("Algo novo", 9999, null);

        result.Name.ShouldBe("Algo novo");
        result.Emoji.ShouldBe("\U0001F321️");
        WeatherConditions.Translate(null, null, null).Name.ShouldBe("Desconhecido");
    }

    [Fact]
    [Trait("AC", "010-114")]
    public void FormatForBriefing_MatchesEveryCaseGeneratedByPython()
    {
        var cases = Fixtures().GetProperty("formats").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(10);
        foreach (var c in cases)
        {
            var input = c.GetProperty("input");
            var expected = c.GetProperty("output").GetString();
            if (input.ValueKind != JsonValueKind.Object || !input.TryGetProperty("temperatura", out _))
            {
                WeatherFormatter.FormatForBriefing(null, 0, 0, null).ShouldBe(expected);
                continue;
            }

            int? rain = input.GetProperty("chance_chuva").ValueKind == JsonValueKind.Null ? null : input.GetProperty("chance_chuva").GetInt32();
            WeatherFormatter.FormatForBriefing(
                input.GetProperty("descricao_completa").GetString(),
                input.GetProperty("temperatura").GetInt32(),
                input.GetProperty("sensacao_termica").GetInt32(),
                rain).ShouldBe(expected);
        }
    }

    [Theory]
    [InlineData(24, 26, null, "\U0001F321️ Clima: d")]
    [InlineData(24, 27, null, "\U0001F321️ Clima: d (sensação de 27°C)")]
    [InlineData(24, 24, 29, "\U0001F321️ Clima: d")]
    [InlineData(24, 24, 30, "\U0001F321️ Clima: d\n\U0001F326️ Possível chuva (30%)")]
    [InlineData(24, 24, 59, "\U0001F321️ Clima: d\n\U0001F326️ Possível chuva (59%)")]
    [InlineData(24, 24, 60, "\U0001F321️ Clima: d\n☔ Alta chance de chuva (60%)")]
    public void FormatForBriefing_Thresholds(int temp, int feels, int? rain, string expected) =>
        WeatherFormatter.FormatForBriefing("d", temp, feels, rain).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void FormatForBriefing_EmptyDescription_IsEmpty(string? description) =>
        WeatherFormatter.FormatForBriefing(description, 20, 20, 80).ShouldBe(string.Empty);

    [Fact]
    public void Truncate_MatchesPythonInt()
    {
        foreach (var c in Fixtures().GetProperty("truncation").EnumerateArray())
        {
            WeatherFormatter.Truncate(c.GetProperty("value").GetDouble()).ShouldBe(c.GetProperty("int").GetInt32());
        }
    }
}
