using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Google;

/// <summary>
/// Formatadores de agenda para WhatsApp (RN-010-53), literais de <c>google_calendar_service.py</c>: emojis,
/// <c>━</c> x 20, dias da semana e plurais idênticos. Puros: o "hoje" entra por parâmetro (o chamador passa
/// <c>IClock.Today</c>), no lugar do <c>date.today()</c> do Python.
/// </summary>
public static partial class CalendarFormatter
{
    private static readonly string[] LongDays =
        ["Segunda-feira", "Terça-feira", "Quarta-feira", "Quinta-feira", "Sexta-feira", "Sábado", "Domingo"];

    private static readonly string[] ShortDays = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];

    private const string Separator = "━━━━━━━━━━━━━━━━━━━━";

    /// <summary>Remove tags HTML e normaliza espaços (<c>strip_html</c>); vazio ou nulo volta como veio.</summary>
    public static string? StripHtml(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = HtmlTag().Replace(text, " ");
        text = text.Replace("&nbsp;", " ", StringComparison.Ordinal);
        text = text.Replace("&amp;", "&", StringComparison.Ordinal);
        text = text.Replace("&lt;", "<", StringComparison.Ordinal);
        text = text.Replace("&gt;", ">", StringComparison.Ordinal);
        text = SpacesAndTabs().Replace(text, " ");
        text = ThreeOrMoreNewlines().Replace(text, "\n\n");
        return text.Trim();
    }

    /// <summary><c>HH:MM</c> a partir de um <c>dateTime</c> ISO, só por texto (<c>format_time</c>); sem <c>T</c> devolve vazio.</summary>
    public static string FormatTime(string? timeText)
    {
        if (string.IsNullOrEmpty(timeText) || !timeText.Contains('T', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var timePart = timeText.Split('T')[1];
        if (timePart.Contains('Z', StringComparison.Ordinal))
        {
            timePart = timePart.Split('Z')[0];
        }
        else if (timePart.Contains('+', StringComparison.Ordinal))
        {
            timePart = timePart.Split('+')[0];
        }
        else if (timePart.Contains('-', StringComparison.Ordinal) && timePart.Split('-')[^1].Contains(':', StringComparison.Ordinal))
        {
            timePart = string.Join('-', timePart.Split('-')[..^1]);
        }

        return timePart.Length > 5 ? timePart[..5] : timePart;
    }

    /// <summary>Agenda de um dia (<c>format_events_for_whatsapp</c>).</summary>
    public static string FormatEventsForWhatsApp(IReadOnlyList<CalendarEvent> events, DateOnly targetDate, DateOnly today)
    {
        if (events.Count == 0)
        {
            var description = targetDate == today
                ? "hoje"
                : targetDate == today.AddDays(1) ? "amanhã" : $"no dia {Day(targetDate, "dd/MM/yyyy")}";
            return $"📅 Você não tem compromissos agendados {description}! 🎉";
        }

        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"📅 *AGENDA - {Day(targetDate, "dd/MM/yyyy")} ({LongDays[WeekdayIndex(targetDate)].ToUpperInvariant()})* 📅\n\n");
        message.Append(CultureInfo.InvariantCulture, $"📊 Total de compromissos: *{events.Count}*\n\n");
        message.Append(Separator).Append("\n\n");

        foreach (var item in events)
        {
            message.Append(item.AllDay ? "📆" : "🔵").Append(" *").Append(item.Summary).Append("*\n");

            if (item.AllDay)
            {
                message.Append("   ⏰ Dia inteiro\n");
            }
            else
            {
                var start = FormatTime(item.Start);
                var end = FormatTime(item.End);
                if (start.Length > 0 && end.Length > 0)
                {
                    message.Append("   ⏰ ").Append(start).Append(" - ").Append(end).Append('\n');
                }
                else if (start.Length > 0)
                {
                    message.Append("   ⏰ ").Append(start).Append('\n');
                }
            }

            if (!string.IsNullOrEmpty(item.Location))
            {
                message.Append("   📍 ").Append(item.Location).Append('\n');
            }

            if (!string.IsNullOrEmpty(item.Description) && !string.IsNullOrEmpty(StripHtml(item.Description)))
            {
                message.Append("   📝 ").Append(StripHtml(item.Description)).Append('\n');
            }

            message.Append('\n');
        }

        return message.ToString().Trim();
    }

    /// <summary>Agenda de vários dias (<c>format_period_events_for_whatsapp</c>); dias sem evento não entram no mapa.</summary>
    public static string FormatPeriodEventsForWhatsApp(
        IReadOnlyDictionary<DateOnly, IReadOnlyList<CalendarEvent>> eventsByDate, DateOnly startDate, DateOnly endDate)
    {
        if (eventsByDate.Count == 0)
        {
            return "📅 Você não tem compromissos agendados neste período! 🎉";
        }

        var total = eventsByDate.Values.Sum(events => events.Count);
        var message = new StringBuilder();
        message.Append(CultureInfo.InvariantCulture, $"📅 *AGENDA - {Day(startDate, "dd/MM")} a {Day(endDate, "dd/MM/yyyy")}* 📅\n\n");
        message.Append(CultureInfo.InvariantCulture, $"📊 Total: *{total} compromisso(s)* em *{eventsByDate.Count} dia(s)*\n\n");
        message.Append(Separator).Append("\n\n");

        foreach (var date in eventsByDate.Keys.Order())
        {
            var events = eventsByDate[date];
            message.Append(CultureInfo.InvariantCulture, $"📆 *{Day(date, "dd/MM")} ({ShortDays[WeekdayIndex(date)]})* - {events.Count} compromisso(s)\n");

            foreach (var item in events)
            {
                if (item.AllDay)
                {
                    message.Append("  • ").Append(item.Summary).Append(" (dia inteiro)\n");
                    continue;
                }

                var start = FormatTime(item.Start);
                message.Append("  • ");
                if (start.Length > 0)
                {
                    message.Append(start).Append(" - ");
                }

                message.Append(item.Summary).Append('\n');
            }

            message.Append('\n');
        }

        return message.ToString().Trim();
    }

    /// <summary><c>weekday()</c> do Python: segunda = 0.</summary>
    private static int WeekdayIndex(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    private static string Day(DateOnly date, string format) => date.ToString(format, CultureInfo.InvariantCulture);

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex("[ \t]+")]
    private static partial Regex SpacesAndTabs();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex ThreeOrMoreNewlines();
}
