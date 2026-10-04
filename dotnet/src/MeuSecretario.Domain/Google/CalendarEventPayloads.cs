using System.Globalization;

namespace MeuSecretario.Domain.Google;

/// <summary><c>start</c> ou <c>end</c> do evento: <see cref="Date"/> (dia inteiro) ou <see cref="DateTimeText"/> (RFC 3339 com deslocamento).</summary>
public sealed record CalendarTime(string? Date, string? DateTimeText, string TimeZone);

/// <summary>Corpo de <c>events.insert</c> (RN-010-48), sem tipos do SDK: a Infrastructure só copia os campos.</summary>
public sealed record CalendarPayload(string Summary, string? Description, string? Location, CalendarTime Start, CalendarTime End);

/// <summary>Início e fim atuais de um evento, como a API devolve.</summary>
public sealed record ExistingEventTimes(string? StartDate, string? StartDateTime, string? EndDate, string? EndDateTime);

/// <summary>
/// Montagem dos corpos de evento do Google Calendar (RN-010-48 e 49, desvio 057). Puro: o fuso entra por parâmetro.
/// O <c>dateTime</c> imita o <c>isoformat()</c> do Python: segundos, sem fração, deslocamento <c>-03:00</c>.
/// </summary>
public static class CalendarEventPayloads
{
    public const string TimeZoneName = "America/Sao_Paulo";

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromHours(1);

    /// <summary>Corpo do <c>insert</c>: dia inteiro sem <paramref name="start"/>; senão com horário, fim = início + 1 h por padrão.</summary>
    public static CalendarPayload ForNew(
        string summary,
        DateOnly date,
        TimeOnly? start,
        TimeOnly? end,
        string? description,
        string? location,
        TimeZoneInfo zone)
    {
        var descriptionOrNull = string.IsNullOrEmpty(description) ? null : description;
        var locationOrNull = string.IsNullOrEmpty(location) ? null : location;

        if (start is null)
        {
            return new CalendarPayload(
                summary,
                descriptionOrNull,
                locationOrNull,
                new CalendarTime(DateText(date), null, TimeZoneName),
                new CalendarTime(DateText(date.AddDays(1)), null, TimeZoneName));
        }

        var startLocal = date.ToDateTime(start.Value);

        // Desvio 057: sem hora de fim, o fim é início + 1 h e passa para o dia seguinte quando ultrapassa 24:00.
        var endLocal = end is { } explicitEnd ? date.ToDateTime(explicitEnd) : startLocal + DefaultDuration;

        return new CalendarPayload(
            summary,
            descriptionOrNull,
            locationOrNull,
            new CalendarTime(null, IsoLocal(startLocal, zone), TimeZoneName),
            new CalendarTime(null, IsoLocal(endLocal, zone), TimeZoneName));
    }

    /// <summary>
    /// Novo <c>start</c>/<c>end</c> de uma atualização (desvio 057), ou <c>null</c> se nada de data/hora foi pedido.
    /// Data, início e fim valem de forma independente; sem fim informado, a duração atual é preservada (ou 1 h para
    /// evento de dia inteiro que ganhou horário). Só a data muda ⇒ mantém horário e duração.
    /// </summary>
    public static (CalendarTime Start, CalendarTime End)? ForUpdate(
        ExistingEventTimes existing,
        DateOnly? newDate,
        TimeOnly? newStart,
        TimeOnly? newEnd,
        TimeZoneInfo zone)
    {
        if (newDate is null && newStart is null && newEnd is null)
        {
            return null;
        }

        var existingTimed = TryParseLocal(existing.StartDateTime, out var existingStart);
        var hasEnd = TryParseLocal(existing.EndDateTime, out var existingEnd);
        var duration = existingTimed && hasEnd && existingEnd > existingStart ? existingEnd - existingStart : DefaultDuration;

        DateOnly targetDate;
        if (newDate is { } requestedDate)
        {
            targetDate = requestedDate;
        }
        else if (existingTimed)
        {
            targetDate = DateOnly.FromDateTime(existingStart);
        }
        else if (!DateOnly.TryParseExact(existing.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out targetDate))
        {
            return null;
        }

        // Evento de dia inteiro sem horário novo: só desloca a data, mantendo a quantidade de dias.
        if (!existingTimed && newStart is null)
        {
            var days = 1;
            if (DateOnly.TryParseExact(existing.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var oldStart)
                && DateOnly.TryParseExact(existing.EndDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var oldEnd)
                && oldEnd > oldStart)
            {
                days = oldEnd.DayNumber - oldStart.DayNumber;
            }

            return (
                new CalendarTime(DateText(targetDate), null, TimeZoneName),
                new CalendarTime(DateText(targetDate.AddDays(days)), null, TimeZoneName));
        }

        var startTime = newStart ?? TimeOnly.FromDateTime(existingStart);
        var startLocal = targetDate.ToDateTime(startTime);
        var endLocal = newEnd is { } explicitEnd ? targetDate.ToDateTime(explicitEnd) : startLocal + duration;

        return (
            new CalendarTime(null, IsoLocal(startLocal, zone), TimeZoneName),
            new CalendarTime(null, IsoLocal(endLocal, zone), TimeZoneName));
    }

    /// <summary><c>2025-11-20T07:00:00-03:00</c>: relógio local + deslocamento do fuso naquele instante.</summary>
    public static string IsoLocal(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone.GetUtcOffset(unspecified);
        return unspecified.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + OffsetText(offset);
    }

    /// <summary><c>-03:00</c>.</summary>
    public static string OffsetText(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var absolute = offset.Duration();
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{absolute.Hours:00}:{absolute.Minutes:00}");
    }

    public static string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Relógio como está escrito no texto (a data de <c>fromisoformat(...).date()</c> do Python), sem converter de fuso.</summary>
    private static bool TryParseLocal(string? text, out DateTime local)
    {
        local = default;
        if (string.IsNullOrEmpty(text) || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        local = parsed.DateTime;
        return true;
    }
}
