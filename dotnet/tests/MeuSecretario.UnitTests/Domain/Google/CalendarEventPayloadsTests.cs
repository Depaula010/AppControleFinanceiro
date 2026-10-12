using MeuSecretario.Domain.Google;

namespace MeuSecretario.UnitTests.Domain.Google;

public class CalendarEventPayloadsTests
{
    private static readonly TimeZoneInfo Zone = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }

    private const string Tz = "America/Sao_Paulo";

    // ---- insert (AC-010-92) ----

    [Fact]
    [Trait("AC", "010-92")]
    public void ForNew_AllDay_UsesDatesAndTheNextDay()
    {
        var payload = CalendarEventPayloads.ForNew("Aniversario", new DateOnly(2025, 11, 20), null, null, null, null, Zone);

        payload.ShouldBe(new CalendarPayload(
            "Aniversario",
            null,
            null,
            new CalendarTime("2025-11-20", null, Tz),
            new CalendarTime("2025-11-21", null, Tz)));
    }

    [Fact]
    [Trait("AC", "010-92")]
    public void ForNew_AllDay_RollsOverTheMonth()
    {
        CalendarEventPayloads.ForNew("x", new DateOnly(2025, 12, 31), null, null, null, null, Zone)
            .End.Date.ShouldBe("2026-01-01");
    }

    [Fact]
    [Trait("AC", "010-92")]
    public void ForNew_WithStart_EndsOneHourLater_AndHasIsoFormat()
    {
        var payload = CalendarEventPayloads.ForNew("Dentista", new DateOnly(2025, 11, 20), new TimeOnly(7, 0), null, "Limpeza", "Clinica", Zone);

        payload.Start.DateTimeText.ShouldBe("2025-11-20T07:00:00-03:00");
        payload.End.DateTimeText.ShouldBe("2025-11-20T08:00:00-03:00");
        payload.Start.Date.ShouldBeNull();
        payload.Description.ShouldBe("Limpeza");
        payload.Location.ShouldBe("Clinica");
        payload.Start.TimeZone.ShouldBe(Tz);
    }

    [Fact]
    [Trait("AC", "010-92")]
    public void ForNew_WithStartAndEnd_UsesBoth()
    {
        var payload = CalendarEventPayloads.ForNew("Reuniao", new DateOnly(2025, 11, 20), new TimeOnly(9, 30), new TimeOnly(11, 0), null, null, Zone);

        payload.Start.DateTimeText.ShouldBe("2025-11-20T09:30:00-03:00");
        payload.End.DateTimeText.ShouldBe("2025-11-20T11:00:00-03:00");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [Trait("AC", "010-92")]
    public void ForNew_EmptyDescriptionAndLocation_AreOmitted(string? empty)
    {
        var payload = CalendarEventPayloads.ForNew("x", new DateOnly(2025, 11, 20), null, null, empty, empty, Zone);

        payload.Description.ShouldBeNull();
        payload.Location.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-92")]
    public void ForNew_Deviation057_StartAt2330WithoutEnd_EndsOnTheNextDay()
    {
        var payload = CalendarEventPayloads.ForNew("Plantao", new DateOnly(2025, 11, 20), new TimeOnly(23, 30), null, null, null, Zone);

        payload.Start.DateTimeText.ShouldBe("2025-11-20T23:30:00-03:00");
        payload.End.DateTimeText.ShouldBe("2025-11-21T00:30:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-92")]
    public void IsoLocal_UsesTheZoneOffsetOfThatInstant()
    {
        // Horário de verão de 2018 (-02:00), como o ZoneInfo do Python; hoje o Brasil não tem horário de verão (-03:00).
        CalendarEventPayloads.IsoLocal(new DateTime(2018, 12, 1, 10, 0, 0), Zone).ShouldBe("2018-12-01T10:00:00-02:00");
        CalendarEventPayloads.IsoLocal(new DateTime(2026, 12, 1, 10, 0, 0), Zone).ShouldBe("2026-12-01T10:00:00-03:00");
    }

    [Theory]
    [InlineData(-3, 0, "-03:00")]
    [InlineData(0, 0, "+00:00")]
    [InlineData(5, 30, "+05:30")]
    [InlineData(-2, 0, "-02:00")]
    public void OffsetText_FormatsSignHoursAndMinutes(int hours, int minutes, string expected)
    {
        CalendarEventPayloads.OffsetText(new TimeSpan(hours, hours < 0 ? -minutes : minutes, 0)).ShouldBe(expected);
    }

    // ---- update (AC-010-93, desvio 057) ----

    private static readonly ExistingEventTimes Timed = new(null, "2025-11-20T10:00:00-03:00", null, "2025-11-20T11:30:00-03:00");
    private static readonly ExistingEventTimes AllDay = new("2025-11-20", null, "2025-11-21", null);

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_WithNothingToChange_ReturnsNull()
    {
        CalendarEventPayloads.ForUpdate(Timed, null, null, null, Zone).ShouldBeNull();
        CalendarEventPayloads.ForUpdate(AllDay, null, null, null, Zone).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_OnlyStart_KeepsTheOriginalDuration()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(Timed, null, new TimeOnly(14, 0), null, Zone)!.Value;

        start.DateTimeText.ShouldBe("2025-11-20T14:00:00-03:00");
        end.DateTimeText.ShouldBe("2025-11-20T15:30:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_OnlyDate_KeepsTimeAndDuration()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(Timed, new DateOnly(2025, 11, 25), null, null, Zone)!.Value;

        start.DateTimeText.ShouldBe("2025-11-25T10:00:00-03:00");
        end.DateTimeText.ShouldBe("2025-11-25T11:30:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_OnlyEnd_KeepsTheStart()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(Timed, null, null, new TimeOnly(18, 0), Zone)!.Value;

        start.DateTimeText.ShouldBe("2025-11-20T10:00:00-03:00");
        end.DateTimeText.ShouldBe("2025-11-20T18:00:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_DateStartAndEnd_ReplaceEverything()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(Timed, new DateOnly(2025, 12, 1), new TimeOnly(8, 0), new TimeOnly(9, 15), Zone)!.Value;

        start.DateTimeText.ShouldBe("2025-12-01T08:00:00-03:00");
        end.DateTimeText.ShouldBe("2025-12-01T09:15:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_AllDayEvent_OnlyDate_ShiftsTheDay()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(AllDay, new DateOnly(2025, 12, 5), null, null, Zone)!.Value;

        start.ShouldBe(new CalendarTime("2025-12-05", null, Tz));
        end.ShouldBe(new CalendarTime("2025-12-06", null, Tz));
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_AllDayEvent_WithStart_BecomesTimedWithOneHour()
    {
        var (start, end) = CalendarEventPayloads.ForUpdate(AllDay, null, new TimeOnly(9, 0), null, Zone)!.Value;

        start.DateTimeText.ShouldBe("2025-11-20T09:00:00-03:00");
        end.DateTimeText.ShouldBe("2025-11-20T10:00:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_Deviation057_StartAt2330_EndsOnTheNextDay()
    {
        var (_, end) = CalendarEventPayloads.ForUpdate(AllDay, null, new TimeOnly(23, 30), null, Zone)!.Value;

        end.DateTimeText.ShouldBe("2025-11-21T00:30:00-03:00");
    }

    [Fact]
    [Trait("AC", "010-93")]
    public void ForUpdate_UnparseableExistingEvent_WithOnlyTime_FallsBackToOneHour()
    {
        var weird = new ExistingEventTimes(null, "lixo", null, "lixo");

        CalendarEventPayloads.ForUpdate(weird, new DateOnly(2025, 11, 20), new TimeOnly(9, 0), null, Zone)!.Value.End.DateTimeText
            .ShouldBe("2025-11-20T10:00:00-03:00");
    }
}
