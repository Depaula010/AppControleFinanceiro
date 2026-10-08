using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>012e: regras puras do calendário contra a referência gerada pelo Flask (<c>whatsapp_sections/012e_calendar.py</c>).</summary>
public class CalendarDomainTests
{
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static string? Str(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    [Fact]
    [Trait("AC", "012-505")]
    public void AgendaPeriods_MatchTheFlaskForEveryPeriodAndDay()
    {
        var cases = WhatsAppFixture.Cases("agenda_period_cases");
        cases.Count.ShouldBeGreaterThan(80);

        foreach (var c in cases)
        {
            var span = AgendaPeriods.ForAgenda(Str(c, "period"), Date(c.GetProperty("today").GetString()!));
            var label = $"{c.GetProperty("today")} {c.GetProperty("period")}";
            span.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("start").GetString(), label);
            span.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("end").GetString(), label);
        }
    }

    [Fact]
    [Trait("AC", "012-506")]
    public void FreeTimeRanges_MatchTheFlask()
    {
        foreach (var c in WhatsAppFixture.Cases("free_range_cases"))
        {
            var span = AgendaPeriods.ForFreeTime(Str(c, "period"), Date(c.GetProperty("today").GetString()!));
            var label = $"{c.GetProperty("today")} {c.GetProperty("period")}";
            if (c.GetProperty("start").ValueKind == JsonValueKind.Null)
            {
                span.ShouldBeNull(label);
                continue;
            }

            span.ShouldNotBeNull(label);
            span.Value.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("start").GetString(), label);
            span.Value.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("end").GetString(), label);
        }
    }

    [Fact]
    [Trait("AC", "012-501")]
    public void ConfirmationMessage_MatchesTheFlask()
    {
        foreach (var c in WhatsAppFixture.Cases("event_confirmation_cases"))
        {
            var e = c.GetProperty("event");
            var location = Str(e, "localizacao");
            var userId = e.GetProperty("usuario_id").ValueKind == JsonValueKind.Null ? 0 : e.GetProperty("usuario_id").GetInt32();

            // addresses: quantidade de endereços, ou "falha" (a leitura falhou e o Flask trata como lista vazia).
            var count = c.GetProperty("addresses").ValueKind == JsonValueKind.Number ? c.GetProperty("addresses").GetInt32() : 0;
            bool? hasAddresses = EventMessages.NeedsAddressLookup(location, userId) ? count > 0 : null;

            var text = EventMessages.Confirmation(
                e.GetProperty("titulo").GetString()!, e.GetProperty("data_evento").GetString()!, Str(e, "hora_inicio"), Str(e, "hora_fim"),
                Str(e, "descricao"), location, hasAddresses);

            text.ShouldBe(c.GetProperty("text").GetString(), e.GetProperty("titulo").GetString());
        }
    }

    [Theory]
    [InlineData("07:05", true, 7, 5)]
    [InlineData("7:5", true, 7, 5)]
    [InlineData("23:59", true, 23, 59)]
    [InlineData("0:00", true, 0, 0)]
    [InlineData("24:00", false, 0, 0)]
    [InlineData("12:60", false, 0, 0)]
    [InlineData("7h", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(" 07:00", false, 0, 0)]
    [InlineData("07:00 ", false, 0, 0)]
    [InlineData("07:00\n", false, 0, 0)]
    [InlineData("07:00:00", false, 0, 0)]
    [InlineData("٧:٣", true, 7, 3)]
    [InlineData("١٢:٣٠", false, 0, 0)]
    public void StrptimeHourMinute_FollowsPython(string text, bool valid, int hour, int minute)
    {
        PyTimeText.TryParseHourMinute(text, out var time).ShouldBe(valid);
        if (valid)
        {
            time.ShouldBe(new TimeOnly(hour, minute));
        }
    }

    [Theory]
    [InlineData("08:00:00", true)]
    [InlineData("8:30:5", true)]
    [InlineData("23:59:59", true)]
    [InlineData("23:59:60", false)]
    [InlineData("23:59:61", false)]
    [InlineData("08:00", false)]
    [InlineData("24:00:00", false)]
    public void StrptimeHourMinuteSecond_FollowsPython(string text, bool valid) =>
        PyTimeText.TryParseHourMinuteSecond(text, out _).ShouldBe(valid);

    [Theory]
    [InlineData("2026-10-08T09:00:00-03:00", "2026-10-08T09:00:00")]
    [InlineData("2026-10-08T12:00:00Z", "2026-10-08T09:00:00")]
    [InlineData("2026-10-08T12:00:00+00:00", "2026-10-08T09:00:00")]
    [InlineData("2026-10-08T09:00:00", "2026-10-08T09:00:00")]
    [InlineData("2026-10-08T23:30:00-02:00", "2026-10-08T22:30:00")]
    public void LocalTime_ConvertsToSaoPaulo(string text, string expected) =>
        CalendarTimes.ToLocal(text, SaoPaulo)!.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture).ShouldBe(expected);

    [Theory]
    [InlineData("2026-10-08")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2026-10-08Tlixo")]
    public void LocalTime_RejectsWhatIsNotADateTime(string? text) => CalendarTimes.ToLocal(text, SaoPaulo).ShouldBeNull();

    [Fact]
    [Trait("AC", "012-506")]
    public void SchedulePatterns_MatchTheFlask()
    {
        foreach (var c in WhatsAppFixture.Cases("pattern_cases"))
        {
            var events = c.GetProperty("events").EnumerateArray().Select(ToRaw).ToList();
            var patterns = SchedulePatterns.Extract(events, SaoPaulo);
            var expected = c.GetProperty("patterns");
            var name = c.GetProperty("name").GetString();

            patterns.EarliestStart.ShouldBe(expected.GetProperty("horario_mais_cedo").GetString(), name);
            patterns.LatestEnd.ShouldBe(expected.GetProperty("horario_mais_tarde").GetString(), name);
            patterns.BusiestDays.ShouldBe(expected.GetProperty("dias_mais_ocupados").EnumerateArray().Select(x => x.GetString()!), name);
            patterns.PreferredPeriods.ShouldBe(expected.GetProperty("periodos_preferidos").EnumerateArray().Select(x => x.GetString()!), name);
            patterns.AverageDuration.ShouldBe(expected.GetProperty("duracao_media_eventos").GetInt32(), name);
            patterns.TotalAnalyzed.ShouldBe(expected.GetProperty("total_eventos_analisados").GetInt32(), name);
            SchedulePatterns.Insights(patterns).ShouldBe(c.GetProperty("insights").GetString(), name);
        }
    }

    [Fact]
    public void DefaultPatterns_MatchTheFlask()
    {
        var expected = WhatsAppFixture.Section("default_patterns");
        SchedulePatterns.Default.EarliestStart.ShouldBe(expected.GetProperty("horario_mais_cedo").GetString());
        SchedulePatterns.Default.LatestEnd.ShouldBe(expected.GetProperty("horario_mais_tarde").GetString());
        SchedulePatterns.Default.PreferredPeriods.ShouldBe(["manha", "tarde"]);
        SchedulePatterns.Default.TotalAnalyzed.ShouldBe(0);
        SchedulePatterns.Insights(SchedulePatterns.Default).ShouldBe(WhatsAppFixture.Section("default_insights").GetString());
    }

    [Fact]
    [Trait("AC", "012-506")]
    public void FreeSlotsMessage_MatchesTheFlask()
    {
        foreach (var c in WhatsAppFixture.Cases("free_format_cases"))
        {
            var result = c.GetProperty("result");
            var slots = result.GetProperty("slots_livres").EnumerateArray()
                .Select(s => new FreeSlot(
                    s.GetProperty("data").GetString()!, s.GetProperty("hora_inicio").GetString()!, s.GetProperty("hora_fim").GetString()!,
                    s.GetProperty("duracao_minutos").GetInt32(), s.GetProperty("qualidade").GetString()!, s.GetProperty("motivo").GetString()!))
                .ToList();
            var free = new FreeSlotsResult(
                Str(result, "error"),
                slots,
                Str(result, "insights_usuario") ?? string.Empty,
                result.TryGetProperty("total_analisado", out var total) ? total.GetInt32() : 0);

            FreeSlotsMessage.Format(free, Str(c, "contexto")).ShouldBe(c.GetProperty("text").GetString());
        }
    }

    [Fact]
    [Trait("AC", "012-506")]
    public void SuggestionPrompt_MatchesTheFlask()
    {
        var cases = WhatsAppFixture.Cases("suggestion_cases").Where(c => c.GetProperty("prompt").ValueKind == JsonValueKind.String).ToList();
        cases.Count.ShouldBe(3);

        foreach (var c in cases)
        {
            var slots = c.GetProperty("slots").EnumerateArray()
                .Select(s => new FreeSlot(
                    s.GetProperty("data").GetString()!, s.GetProperty("hora_inicio").GetString()!, s.GetProperty("hora_fim").GetString()!,
                    0, s.GetProperty("qualidade").GetString()!, s.GetProperty("motivo").GetString()!))
                .ToList();

            CalendarPrompts.Suggestion(slots, c.GetProperty("contexto").GetString()!, c.GetProperty("prefs").GetString()!)
                .ShouldBe(c.GetProperty("prompt").GetString());
        }
    }

    [Fact]
    public void SuggestionPrompt_ADigitedMarkerDoesNotContaminateTheRest()
    {
        var slot = new FreeSlot("2026-10-09", "09:00", "10:00", 60, "otimo", "Dia livre");

        var text = CalendarPrompts.Suggestion([slot], "⟦PREFS⟧ x", "padrões");

        text.ShouldContain("O usuário quer marcar: \"⟦PREFS⟧ x\"");
        text.ShouldContain("Padrões do usuário:\npadrões");
    }

    [Fact]
    [Trait("AC", "012-504")]
    public void DeleteMany_ListsEventsWithDatesAsWritten()
    {
        var text = EventMessages.DeleteMany(
        [
            new DeletionCandidate("a1", "Reunião", "2026-10-08T18:00:00-03:00", "Principal"),
            new DeletionCandidate("a2", "Dia", "2026-10-10", "Trabalho"),
            new DeletionCandidate("a3", "UTC", "2026-10-08T21:00:00Z", "Outro"),
        ]);

        text.ShouldBe(
            "📋 Encontrei 3 eventos:\n\n" +
            "1. *Reunião*\n   📅 08/10 às 18:00\n   📂 Principal\n   _ID: a1_\n\n" +
            "2. *Dia*\n   📅 10/10\n   📂 Trabalho\n   _ID: a2_\n\n" +
            "3. *UTC*\n   📅 08/10 às 21:00\n   📂 Outro\n   _ID: a3_\n\n" +
            "Para deletar um específico, envie:\n'Deletar evento a1'");
    }

    private static RawEvent ToRaw(JsonElement e)
    {
        var start = e.GetProperty("start");
        var end = e.GetProperty("end");
        return new RawEvent(
            start.TryGetProperty("date", out _),
            start.TryGetProperty("dateTime", out var s) ? s.GetString()! : string.Empty,
            end.TryGetProperty("dateTime", out var f) ? f.GetString()! : string.Empty);
    }
}
