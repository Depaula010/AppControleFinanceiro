using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;

namespace MeuSecretario.UnitTests.Application.Google;

/// <summary>Saídas comparadas com o <c>google_calendar_service.py</c> do Flask (fixtures geradas pelo Python, spec 010d T31).</summary>
public class CalendarFormatterTests
{
    private static JsonElement Calendar => GoogleFixtures.Root.GetProperty("calendar");

    private static DateOnly Today => DateOnly.Parse(Calendar.GetProperty("today").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static CalendarEvent ToEvent(JsonElement e) => new(
        "id",
        e.GetProperty("summary").GetString()!,
        e.GetProperty("start").GetString()!,
        e.GetProperty("end").GetString()!,
        e.GetProperty("location").GetString()!,
        e.GetProperty("description").GetString()!,
        e.GetProperty("all_day").GetBoolean(),
        null);

    private static string? Nullable(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetString();

    [Fact]
    [Trait("AC", "010-97")]
    public void StripHtml_MatchesThePython()
    {
        foreach (var item in Calendar.GetProperty("strip_html").EnumerateArray())
        {
            CalendarFormatter.StripHtml(Nullable(item.GetProperty("input"))).ShouldBe(Nullable(item.GetProperty("output")));
        }
    }

    [Fact]
    [Trait("AC", "010-97")]
    public void FormatTime_MatchesThePython_ForZ_Plus_Minus_NoT_AndEmpty()
    {
        var cases = Calendar.GetProperty("format_time").EnumerateArray().ToList();

        cases.Count.ShouldBeGreaterThanOrEqualTo(9);
        foreach (var item in cases)
        {
            CalendarFormatter.FormatTime(Nullable(item.GetProperty("input"))).ShouldBe(item.GetProperty("output").GetString());
        }
    }

    [Fact]
    [Trait("AC", "010-97")]
    public void FormatEventsForWhatsApp_MatchesThePython_ForEveryDayCase()
    {
        var cases = Calendar.GetProperty("day").EnumerateArray().ToList();

        cases.Count.ShouldBeGreaterThanOrEqualTo(10);
        foreach (var item in cases)
        {
            var events = item.GetProperty("events").EnumerateArray().Select(ToEvent).ToList();
            var date = DateOnly.Parse(item.GetProperty("date").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

            CalendarFormatter.FormatEventsForWhatsApp(events, date, Today)
                .ShouldBe(item.GetProperty("output").GetString(), item.GetProperty("name").GetString());
        }
    }

    [Fact]
    [Trait("AC", "010-97")]
    public void FormatPeriodEventsForWhatsApp_MatchesThePython_ForEveryPeriodCase()
    {
        var cases = Calendar.GetProperty("period").EnumerateArray().ToList();

        cases.Count.ShouldBe(3);
        foreach (var item in cases)
        {
            var byDate = item.GetProperty("by_date").EnumerateObject().ToDictionary(
                p => DateOnly.Parse(p.Name, System.Globalization.CultureInfo.InvariantCulture),
                p => (IReadOnlyList<CalendarEvent>)p.Value.EnumerateArray().Select(ToEvent).ToList());

            CalendarFormatter.FormatPeriodEventsForWhatsApp(
                    byDate,
                    DateOnly.Parse(item.GetProperty("start").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                    DateOnly.Parse(item.GetProperty("end").GetString()!, System.Globalization.CultureInfo.InvariantCulture))
                .ShouldBe(item.GetProperty("output").GetString(), item.GetProperty("name").GetString());
        }
    }
}
