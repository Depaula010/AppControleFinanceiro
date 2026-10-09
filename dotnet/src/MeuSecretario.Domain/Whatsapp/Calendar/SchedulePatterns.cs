using System.Globalization;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Evento do Google como os serviços de horários livres o enxergam: dia inteiro ou os dois textos <c>dateTime</c>.</summary>
public sealed record RawEvent(bool AllDay, string Start, string End);

/// <summary>Padrões de agenda do usuário (<c>UserSchedulePatternService.analyze_user_patterns</c>).</summary>
/// <param name="BusiestDays">Nomes de dia em inglês e minúsculas (<c>strftime('%A').lower()</c>, desvio 009).</param>
public sealed record UserPatterns(
    string EarliestStart,
    string LatestEnd,
    IReadOnlyList<string> BusiestDays,
    IReadOnlyList<string> PreferredPeriods,
    int AverageDuration,
    int TotalAnalyzed);

/// <summary>Análise do histórico de eventos (<c>user_schedule_pattern_service.py</c>) e o texto de insights.</summary>
public static class SchedulePatterns
{
    public static readonly UserPatterns Default = new("09:00", "18:00", [], ["manha", "tarde"], 60, 0);

    private static readonly string[] EnglishDays = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    /// <summary>Nome do dia em inglês e minúsculas (<c>date.strftime('%A').lower()</c> com o locale <c>C</c>).</summary>
    public static string EnglishDayName(DateOnly date) => EnglishDays[(int)date.DayOfWeek];

    /// <summary>Período do dia pela hora de início: <c>manha</c> (6-11), <c>tarde</c> (12-17), <c>noite</c> (o resto).</summary>
    public static string PeriodOf(int hour) => hour is >= 6 and < 12 ? "manha" : hour is >= 12 and < 18 ? "tarde" : "noite";

    public static UserPatterns Extract(IReadOnlyList<RawEvent> events, TimeZoneInfo zone)
    {
        var starts = new List<TimeOnly>();
        var ends = new List<TimeOnly>();
        var days = new Dictionary<string, int>(StringComparer.Ordinal);
        var dayOrder = new List<string>();
        var periods = new Dictionary<string, int>(StringComparer.Ordinal);
        var periodOrder = new List<string>();
        var durations = new List<double>();

        foreach (var item in events)
        {
            if (item.AllDay)
            {
                continue;
            }

            var start = CalendarTimes.ToLocal(item.Start, zone);
            var end = CalendarTimes.ToLocal(item.End, zone);
            if (start is null || end is null)
            {
                continue;
            }

            starts.Add(TimeOnly.FromDateTime(start.Value));
            ends.Add(TimeOnly.FromDateTime(end.Value));

            var day = EnglishDayName(DateOnly.FromDateTime(start.Value));
            Count(days, dayOrder, day);
            Count(periods, periodOrder, PeriodOf(start.Value.Hour));

            var minutes = (end.Value - start.Value).TotalSeconds / 60;
            if (minutes is > 0 and < 480)
            {
                durations.Add(minutes);
            }
        }

        if (starts.Count == 0)
        {
            return Default;
        }

        // sorted(..., reverse=True) do Python é estável: empates mantêm a ordem de primeira aparição.
        var busiest = dayOrder.OrderByDescending(day => days[day]).Take(2).ToList();
        var preferred = periodOrder.OrderByDescending(period => periods[period]).ToList();
        var average = durations.Count > 0 ? (int)(durations.Sum() / durations.Count) : 60;

        return new UserPatterns(
            Hm(starts.Min()), Hm(ends.Max()), busiest, preferred, average, starts.Count);
    }

    /// <summary><c>get_insights_text</c>.</summary>
    public static string Insights(UserPatterns patterns)
    {
        if (patterns.TotalAnalyzed == 0)
        {
            return "Sem histórico suficiente para análise de padrões.";
        }

        var parts = new List<string>
        {
            $"Você costuma ter compromissos entre {patterns.EarliestStart} e {patterns.LatestEnd}",
        };

        if (patterns.PreferredPeriods.Count > 0)
        {
            var names = patterns.PreferredPeriods.Take(2).Select(period => period switch
            {
                "manha" => "manhãs",
                "tarde" => "tardes",
                "noite" => "noites",
                _ => period,
            });
            parts.Add("com preferência por " + string.Join(", ", names));
        }

        if (patterns.BusiestDays.Count > 0)
        {
            var names = patterns.BusiestDays.Select(day => day switch
            {
                "monday" => "segunda",
                "tuesday" => "terça",
                "wednesday" => "quarta",
                "thursday" => "quinta",
                "friday" => "sexta",
                "saturday" => "sábado",
                "sunday" => "domingo",
                _ => day,
            });
            parts.Add("Seus dias mais ocupados: " + string.Join(" e ", names));
        }

        return string.Join(". ", parts) + ".";
    }

    internal static string Hm(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static void Count(Dictionary<string, int> counts, List<string> order, string key)
    {
        if (!counts.TryAdd(key, 1))
        {
            counts[key]++;
        }
        else
        {
            order.Add(key);
        }
    }
}
