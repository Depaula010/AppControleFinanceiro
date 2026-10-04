using System.Text.Json;
using MeuSecretario.Domain.Routes;

namespace MeuSecretario.UnitTests.Domain.Routes;

public class RoutesDomainTests
{
    private static JsonElement Fixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "weather-routes-fixtures.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }

    [Fact]
    [Trait("AC", "010-118")]
    public void DistanceKm_MatchesPythonRoundForEveryFixture()
    {
        var cases = Fixtures().GetProperty("distance_rounding").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(400);
        foreach (var c in cases)
        {
            var meters = c.GetProperty("meters").GetDouble();
            TravelMath.DistanceKm(meters).ShouldBe(c.GetProperty("km").GetDouble(), $"meters={meters:R}");
        }
    }

    [Theory]
    [InlineData(12549, 12.5)]
    [InlineData(12450, 12.4)]
    [InlineData(12550, 12.6)]
    [InlineData(12350, 12.3)]
    [InlineData(250, 0.2)]
    [InlineData(750, 0.8)]
    [InlineData(-250, -0.2)]
    public void DistanceKm_HalfToEvenOnTheExactBinaryValue(double meters, double km) =>
        TravelMath.DistanceKm(meters).ShouldBe(km);

    [Fact]
    [Trait("AC", "010-118")]
    public void DurationMinutes_TruncatesLikePythonInt()
    {
        TravelMath.DurationMinutes(2759).ShouldBe(45);
        foreach (var c in Fixtures().GetProperty("duration_truncation").EnumerateArray())
        {
            TravelMath.DurationMinutes(c.GetProperty("seconds").GetDouble()).ShouldBe(c.GetProperty("minutes").GetInt32());
        }
    }

    [Theory]
    [InlineData(-23.5, -46.6, true)]
    [InlineData(-33, -74, true)]
    [InlineData(5, -34, true)]
    [InlineData(-33.01, -50, false)]
    [InlineData(5.01, -50, false)]
    [InlineData(-10, -74.01, false)]
    [InlineData(-10, -33.99, false)]
    [InlineData(40.7, -74.0, false)]
    public void BrazilBounds_Contains(double lat, double lon, bool expected) =>
        BrazilBounds.Contains(lat, lon).ShouldBe(expected);
}
