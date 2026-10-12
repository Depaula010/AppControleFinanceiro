using System.Globalization;
using System.Text;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Horário livre achado (<c>slots_livres</c> do Flask): data <c>yyyy-MM-dd</c>, horas <c>HH:mm</c>, qualidade <c>otimo|bom|regular</c>.</summary>
public sealed record FreeSlot(string Date, string StartTime, string EndTime, int DurationMinutes, string Quality, string Reason);

/// <summary>Resultado de <c>find_free_slots</c>; <paramref name="Error"/> é o <c>{"error": "Período inválido", "slots_livres": []}</c>.</summary>
public sealed record FreeSlotsResult(string? Error, IReadOnlyList<FreeSlot> Slots, string Insights, int TotalAnalyzed);

/// <summary>Geração e classificação dos horários livres (<c>free_time_finder_service.py:_generate_free_slots</c> em diante).</summary>
public static class FreeSlots
{
    public const string InvalidPeriod = "Período inválido";

    private static readonly Dictionary<string, int> QualityOrder = new(StringComparer.Ordinal) { ["otimo"] = 0, ["bom"] = 1, ["regular"] = 2 };

    /// <summary>Resultado de período inválido.</summary>
    public static FreeSlotsResult Invalid() => new(InvalidPeriod, [], string.Empty, 0);

    public static FreeSlotsResult Find(
        DateSpan range,
        IReadOnlyList<RawEvent> events,
        double? durationMinutes,
        UserPatterns patterns,
        DateTime now,
        TimeZoneInfo zone)
    {
        var workStart = new TimeOnly(8, 0);
        var workEnd = new TimeOnly(20, 0);

        // Com histórico suficiente o expediente vira "primeiro início - 1 h" até "último fim + 1 h" (a conta dá a volta à meia-noite).
        if (patterns.TotalAnalyzed > 5
            && PyTimeText.TryParseHourMinute(patterns.EarliestStart, out var earliest)
            && PyTimeText.TryParseHourMinute(patterns.LatestEnd, out var latest))
        {
            workStart = earliest.AddHours(-1);
            workEnd = latest.AddHours(1);
        }

        var slots = new List<FreeSlot>();
        for (var day = range.Start; day <= range.End; day = day.AddDays(1))
        {
            var dayEvents = EventsForDay(events, day, zone);
            slots.AddRange(SlotsForDay(day, dayEvents, workStart, workEnd, durationMinutes, patterns, now));
        }

        // Ordenação estável por (qualidade, data, hora de início), como o sort do Python.
        var ordered = slots
            .OrderBy(slot => QualityOrder.GetValueOrDefault(slot.Quality, 3))
            .ThenBy(slot => slot.Date, StringComparer.Ordinal)
            .ThenBy(slot => slot.StartTime, StringComparer.Ordinal)
            .ToList();

        return new FreeSlotsResult(null, ordered, SchedulePatterns.Insights(patterns), patterns.TotalAnalyzed);
    }

    /// <summary>Eventos com horário do dia, ordenados por início; dia inteiro e eventos de 18 h ou mais não bloqueiam.</summary>
    public static IReadOnlyList<(DateTime Start, DateTime End)> EventsForDay(IReadOnlyList<RawEvent> events, DateOnly day, TimeZoneInfo zone)
    {
        var result = new List<(DateTime Start, DateTime End)>();
        foreach (var item in events)
        {
            if (item.AllDay)
            {
                continue;
            }

            var start = CalendarTimes.ToLocal(item.Start, zone);
            if (start is null || DateOnly.FromDateTime(start.Value) != day)
            {
                continue;
            }

            var end = CalendarTimes.ToLocal(item.End, zone);
            if (end is null || (end.Value - start.Value).TotalSeconds / 3600 >= 18)
            {
                continue;
            }

            result.Add((start.Value, end.Value));
        }

        return [.. result.OrderBy(item => item.Start)];
    }

    private static List<FreeSlot> SlotsForDay(
        DateOnly day,
        IReadOnlyList<(DateTime Start, DateTime End)> dayEvents,
        TimeOnly workStart,
        TimeOnly workEnd,
        double? durationMinutes,
        UserPatterns patterns,
        DateTime now)
    {
        var slots = new List<FreeSlot>();
        var today = DateOnly.FromDateTime(now);
        if (day < today)
        {
            return slots;
        }

        var dayStart = day.ToDateTime(workStart);
        var dayEnd = day.ToDateTime(workEnd);

        if (day == today)
        {
            // Próximo múltiplo de 30 minutos depois de agora.
            var rounded = ((now.Minute / 30) + 1) * 30;
            var next = rounded == 60
                ? new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Unspecified).AddHours(1)
                : new DateTime(now.Year, now.Month, now.Day, now.Hour, rounded, 0, DateTimeKind.Unspecified);
            dayStart = dayStart > next ? dayStart : next;
        }

        if (dayStart >= dayEnd)
        {
            return slots;
        }

        if (dayEvents.Count == 0)
        {
            var total = Minutes(dayStart, dayEnd);
            if (Fits(total, durationMinutes))
            {
                slots.Add(Build(day, dayStart, dayEnd, total, Quality(dayStart, dayEnd, hasPrevious: false, hasNext: false, patterns, day)));
            }

            return slots;
        }

        var first = dayEvents[0];
        if (dayStart < first.Start)
        {
            var length = Minutes(dayStart, first.Start);
            if (Fits(length, durationMinutes))
            {
                slots.Add(Build(day, dayStart, first.Start, length, Quality(dayStart, first.Start, hasPrevious: false, hasNext: true, patterns, day)));
            }
        }

        for (var index = 0; index < dayEvents.Count - 1; index++)
        {
            var gapStart = dayEvents[index].End;
            var gapEnd = dayEvents[index + 1].Start;
            var length = Minutes(gapStart, gapEnd);
            if (Fits(length, durationMinutes))
            {
                slots.Add(Build(day, gapStart, gapEnd, length, Quality(gapStart, gapEnd, hasPrevious: true, hasNext: true, patterns, day)));
            }
        }

        var last = dayEvents[^1];
        if (last.End < dayEnd)
        {
            var length = Minutes(last.End, dayEnd);
            if (Fits(length, durationMinutes))
            {
                slots.Add(Build(day, last.End, dayEnd, length, Quality(last.End, dayEnd, hasPrevious: true, hasNext: false, patterns, day)));
            }
        }

        return slots;
    }

    /// <summary>
    /// <c>minutos &gt;= duracao_minutos</c>: duração ausente ou que não é número faz o Python lançar <c>TypeError</c> na primeira comparação
    /// (a intenção "template" responde o texto genérico).
    /// </summary>
    private static bool Fits(int minutes, double? duration) =>
        minutes >= (duration ?? throw new InvalidOperationException("duracao_minutos inválida."));

    /// <summary><c>int((fim - inicio).total_seconds() / 60)</c>: trunca em direção a zero.</summary>
    private static int Minutes(DateTime start, DateTime end) => (int)((end - start).TotalSeconds / 60);

    private static FreeSlot Build(DateOnly day, DateTime start, DateTime end, int minutes, (string Quality, string Reason) quality) =>
        new(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Hm(start), Hm(end), minutes, quality.Quality, quality.Reason);

    private static string Hm(DateTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary><c>_analyze_slot_quality</c>: pontos por eventos adjacentes, período preferido, dia menos ocupado e duração.</summary>
    private static (string Quality, string Reason) Quality(
        DateTime slotStart, DateTime slotEnd, bool hasPrevious, bool hasNext, UserPatterns patterns, DateOnly day)
    {
        var points = 0;
        var reasons = new List<string>();

        if (!hasPrevious && !hasNext)
        {
            points += 3;
            reasons.Add("dia livre");
        }
        else if (!hasPrevious || !hasNext)
        {
            points += 2;
            reasons.Add("sem eventos adjacentes");
        }
        else
        {
            points += 1;
        }

        var period = SchedulePatterns.PeriodOf(slotStart.Hour);
        if (patterns.PreferredPeriods.Contains(period))
        {
            points += 2;
            reasons.Add("período preferido (" + (period == "manha" ? "manhã" : period) + ")");
        }

        if (!patterns.BusiestDays.Contains(SchedulePatterns.EnglishDayName(day)))
        {
            points += 1;
            reasons.Add("dia menos ocupado");
        }

        var minutes = Minutes(slotStart, slotEnd);
        if (minutes >= 180)
        {
            points += 2;
            reasons.Add("longo período disponível");
        }
        else if (minutes >= 120)
        {
            points += 1;
        }

        var quality = points >= 6 ? "otimo" : points >= 4 ? "bom" : "regular";
        var reason = reasons.Count > 0 ? PyStrings.Capitalize(string.Join(", ", reasons)) : "Horário disponível";
        return (quality, reason);
    }
}

/// <summary>Mensagem dos horários livres (<c>format_free_slots_message</c>) e prompt da sugestão da IA.</summary>
public static class FreeSlotsMessage
{
    public static string Format(FreeSlotsResult result, string? context)
    {
        if (result.Slots.Count == 0)
        {
            var empty = "🗓️ *Horários Livres*\n\n❌ Nenhum horário livre encontrado no período solicitado.\n\n";
            return result.Insights.Length > 0 ? empty + "💡 " + result.Insights : empty;
        }

        var message = new StringBuilder("🗓️ *Horários Livres");
        if (!string.IsNullOrEmpty(context))
        {
            message.Append(" para ").Append(context);
        }

        message.Append("*\n\n");

        // Agrupa por data na ordem em que as datas aparecem (a ordenação já é por qualidade, não por data).
        var order = new List<string>();
        var byDate = new Dictionary<string, List<FreeSlot>>(StringComparer.Ordinal);
        foreach (var slot in result.Slots)
        {
            if (!byDate.TryGetValue(slot.Date, out var list))
            {
                list = [];
                byDate[slot.Date] = list;
                order.Add(slot.Date);
            }

            list.Add(slot);
        }

        foreach (var date in order)
        {
            var parsed = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            message.Append("📅 *").Append(WeekdayName(parsed.DayOfWeek)).Append(", ").Append(parsed.ToString("dd/MM", CultureInfo.InvariantCulture)).Append("*\n");

            foreach (var slot in byDate[date].Take(3))
            {
                var emoji = slot.Quality switch
                {
                    "otimo" => "⭐",
                    "bom" => "✓",
                    _ => "•",
                };
                message.Append(emoji).Append(' ').Append(slot.StartTime).Append('-').Append(slot.EndTime);
                if (slot.Quality is "otimo" or "bom")
                {
                    message.Append(" _").Append(slot.Reason).Append('_');
                }

                message.Append('\n');
            }

            message.Append('\n');
        }

        if (result.Insights.Length > 0 && result.TotalAnalyzed > 0)
        {
            message.Append("💡 ").Append(result.Insights);
        }

        return message.ToString().Trim();
    }

    private static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "Segunda",
        DayOfWeek.Tuesday => "Terça",
        DayOfWeek.Wednesday => "Quarta",
        DayOfWeek.Thursday => "Quinta",
        DayOfWeek.Friday => "Sexta",
        DayOfWeek.Saturday => "Sábado",
        _ => "Domingo",
    };
}
