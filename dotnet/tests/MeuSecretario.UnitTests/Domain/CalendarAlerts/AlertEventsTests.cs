using MeuSecretario.Domain.CalendarAlerts;

namespace MeuSecretario.UnitTests.Domain.CalendarAlerts;

public class AlertEventsTests
{
    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);

    [Fact]
    [Trait("AC", "014-40")]
    public void Window_IsTheTargetMinusSixtyPlusThirtySeconds()
    {
        // Desvio 121: (alvo - 60 s, alvo + 30 s]; alvo = 18:00:20.
        var now = new DateTimeOffset(2026, 10, 5, 17, 59, 20, SaoPaulo);

        var (start, end) = AlertWindow.For(now, 1);

        start.ShouldBe(new DateTimeOffset(2026, 10, 5, 17, 59, 20, SaoPaulo));
        end.ShouldBe(new DateTimeOffset(2026, 10, 5, 18, 0, 50, SaoPaulo));
    }

    [Fact]
    [Trait("AC", "014-40")]
    public void Window_WithSixtyMinutes_CrossesTheHourAndKeepsTheOffset()
    {
        var now = new DateTimeOffset(2026, 10, 5, 23, 30, 0, SaoPaulo);

        var (start, end) = AlertWindow.For(now, 60);

        start.ShouldBe(new DateTimeOffset(2026, 10, 6, 0, 29, 0, SaoPaulo));
        start.Offset.ShouldBe(SaoPaulo);
        end.ShouldBe(new DateTimeOffset(2026, 10, 6, 0, 30, 30, SaoPaulo));
    }

    [Theory]
    [Trait("AC", "014-48")]
    [InlineData("2026-10-05T17:59:20-03:00", false)]
    [InlineData("2026-10-05T17:59:21-03:00", true)]
    [InlineData("2026-10-05T18:00:50-03:00", true)]
    [InlineData("2026-10-05T18:00:51-03:00", false)]
    public void Window_LowerBoundIsExclusive_UpperBoundIsInclusive(string eventStart, bool inside)
    {
        var window = AlertWindow.For(new DateTimeOffset(2026, 10, 5, 17, 59, 20, SaoPaulo), 1);

        AlertWindow.Contains(window, DateTimeOffset.Parse(eventStart, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(inside);
    }

    [Theory]
    [Trait("AC", "014-45")]
    [InlineData("2024-01-15T14:30:00-03:00", "14:30")]
    [InlineData("2024-01-15T14:30:00Z", "14:30")]
    [InlineData("2024-01-15T09:05:00+05:30", "09:05")]
    [InlineData("2024-01-15T14:30:00.123456-03:00", "14:30")]
    [InlineData("2024-01-15T14:30:00", "14:30")]
    [InlineData("2024-01-15", "00:00")]
    [InlineData("amanhã cedo", "amanhã cedo")]
    [InlineData("", "")]
    public void FormatTime_UsesTheClockWrittenInTheText(string text, string expected) =>
        AlertMessage.FormatTime(text).ShouldBe(expected);

    [Fact]
    [Trait("AC", "014-45")]
    public void Build_WithLocation_HasThreeLines() =>
        AlertMessage.Build("Reunião", "2026-10-05T18:00:00-03:00", "Sala 2", 1)
            .ShouldBe("⚠️ Faltam 1 min: Reunião\n🕒 18:00\n📍 Sala 2");

    [Fact]
    [Trait("AC", "014-45")]
    public void Build_WithoutLocation_HasTwoLines() =>
        AlertMessage.Build("Dentista", "2026-10-05T18:00:00-03:00", string.Empty, 15)
            .ShouldBe("⚠️ Faltam 15 min: Dentista\n🕒 18:00");

    [Theory]
    [Trait("AC", "014-41")]
    [InlineData("2026-10-05T18:00:00-03:00", true)]
    [InlineData("2026-10-05T21:00:00Z", true)]
    [InlineData("2026-10-05", true)]
    [InlineData("nao e data", false)]
    [InlineData("", false)]
    [InlineData("05/10/2026 18:00", false)]
    public void TryParseIso_AcceptsOnlyIsoText(string text, bool expected) =>
        AlertMessage.TryParseIso(text, out _).ShouldBe(expected);
}
