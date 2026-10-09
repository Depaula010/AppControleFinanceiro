using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

/// <summary>
/// <see cref="ICalendarClient"/> sobre o "mundo" dos cenários do Python (<c>efh_support.FakeGoogle</c>): calendários com eventos no formato
/// da API, recorte de tempo e <c>q</c> iguais aos do Google. Registra cada listagem como o Python registra (<c>Calls</c>).
/// </summary>
internal sealed class FixtureCalendarClient : ICalendarClient
{
    private readonly List<FakeCalendar> _calendars;
    private readonly TimeProvider _time;

    public FixtureCalendarClient(JsonElement world, TimeProvider time)
    {
        _time = time;
        _calendars =
        [
            .. world.EnumerateArray().Select(c => new FakeCalendar(
                c.GetProperty("id").GetString()!,
                c.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String ? summary.GetString()! : "Sem nome",
                !c.TryGetProperty("selected", out var selected) || selected.GetBoolean(),
                c.TryGetProperty("primary", out var primary) && primary.GetBoolean(),
                [.. c.GetProperty("events").EnumerateArray().Select(e => e.Clone())])),
        ];
    }

    public bool NotConnected { get; set; }

    public bool FailCalendarList { get; set; }

    public HashSet<string> FailEventsFor { get; } = [];

    public bool FailInsert { get; set; }

    /// <summary>Chamadas como o Python registra: <c>list|calendário|timeMin|timeMax|q|maxResults</c>.</summary>
    public List<string> Calls { get; } = [];

    public List<NewCalendarEvent> Inserted { get; } = [];

    public List<(string EventId, string CalendarId)> Deleted { get; } = [];

    public Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        if (FailCalendarList)
        {
            throw new CalendarApiException("falha ao listar calendários", null);
        }

        IReadOnlyList<CalendarInfo> list = [.. _calendars.Where(c => c.Selected).Select(c => new CalendarInfo(c.Id, c.Summary, c.Primary))];
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var min = query.TimeMinRaw ?? (query.TimeMin is { } a ? Rfc3339(a) : null);
        var max = query.TimeMaxRaw ?? (query.TimeMax is { } b ? Rfc3339(b) : null);
        IReadOnlyList<CalendarEvent> events = List(query.CalendarId, min, max, query.Query, query.MaxResults, calendarName: null, throwOnFailure: true);
        return Task.FromResult(events);
    }

    public Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(
        int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var min = Rfc3339(new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)));
        var max = Rfc3339(new DateTimeOffset(endDate.ToDateTime(new TimeOnly(23, 59, 59)).AddTicks(9_999_990), TimeSpan.FromHours(-3)));

        var all = new List<CalendarEvent>();
        foreach (var calendar in CalendarsOrPrimary())
        {
            all.AddRange(List(calendar.Id, min, max, null, null, calendar.Summary, throwOnFailure: false));
        }

        IReadOnlyList<CalendarEvent> sorted = [.. all.OrderBy(e => e.Start, StringComparer.Ordinal)];
        return Task.FromResult(sorted);
    }

    public Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken)
    {
        EnsureConnected();
        if (FailInsert)
        {
            throw new CalendarApiException("falha da API", System.Net.HttpStatusCode.Forbidden);
        }

        Inserted.Add(newEvent);
        return Task.FromResult("novo-evento-1");
    }

    public Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken)
    {
        EnsureConnected();
        var calendar = _calendars.FirstOrDefault(c => c.Id == calendarId);
        var item = calendar?.Events.FirstOrDefault(e => e.GetProperty("id").GetString() == eventId);
        if (calendar is null || item is not { } found)
        {
            return Task.FromResult(new CalendarDeleteResult(false, "Evento"));
        }

        calendar.Events.Remove(found);
        Deleted.Add((eventId, calendarId));
        return Task.FromResult(new CalendarDeleteResult(true, found.TryGetProperty("summary", out var s) ? s.GetString()! : "Sem título"));
    }

    public Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken)
    {
        try
        {
            EnsureConnected();
            var now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
            var min = Rfc3339(now);
            var max = Rfc3339(now.AddDays(90));
            var found = new List<CalendarEvent>();
            foreach (var calendar in CalendarsOrPrimary())
            {
                found.AddRange(List(calendar.Id, min, max, title, 10, calendar.Summary, throwOnFailure: false));
            }

            return Task.FromResult<IReadOnlyList<CalendarEvent>>(found);
        }
        catch (Exception ex) when (ex is GoogleNotConnectedException or CalendarApiException)
        {
            return Task.FromResult<IReadOnlyList<CalendarEvent>>([]);
        }
    }

    private void EnsureConnected()
    {
        if (NotConnected)
        {
            throw new GoogleNotConnectedException("Usuário não conectou Google Calendar");
        }
    }

    private IEnumerable<FakeCalendar> CalendarsOrPrimary() =>
        FailCalendarList ? [new FakeCalendar("primary", "Primary", true, true, [])] : _calendars.Where(c => c.Selected);

    private static readonly string[] SearchFields = ["summary", "description", "location"];

    private List<CalendarEvent> List(
        string calendarId, string? min, string? max, string? q, int? maxResults, string? calendarName, bool throwOnFailure)
    {
        Calls.Add(string.Join("|", "list", calendarId, min ?? string.Empty, max ?? string.Empty, q ?? string.Empty,
            maxResults?.ToString(CultureInfo.InvariantCulture) ?? string.Empty));

        if (FailEventsFor.Contains(calendarId))
        {
            return throwOnFailure ? throw new CalendarApiException("falha no calendário", null) : [];
        }

        var calendar = _calendars.FirstOrDefault(c => c.Id == calendarId);
        var low = min is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(min, CultureInfo.InvariantCulture);
        var high = max is null ? (DateTimeOffset?)null : DateTimeOffset.Parse(max, CultureInfo.InvariantCulture);

        var items = new List<CalendarEvent>();
        foreach (var item in calendar?.Events ?? [])
        {
            var (start, end) = (Bound(item.GetProperty("start")), Bound(item.GetProperty("end")));
            if ((low is { } l && end <= l) || (high is { } h && start >= h))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(q))
            {
                var haystack = string.Join(" ", SearchFields
                    .Select(k => item.TryGetProperty(k, out var v) ? v.GetString() : string.Empty)).ToLowerInvariant();
                if (!haystack.Contains(q.ToLowerInvariant(), StringComparison.Ordinal))
                {
                    continue;
                }
            }

            items.Add(ToEvent(item, calendarName, calendarId));
        }

        return maxResults is { } limit ? [.. items.Take(limit)] : items;
    }

    private static DateTimeOffset Bound(JsonElement part) =>
        part.TryGetProperty("dateTime", out var dateTime)
            ? DateTimeOffset.Parse(dateTime.GetString()!.Replace("Z", "+00:00", StringComparison.Ordinal), CultureInfo.InvariantCulture)
            : new DateTimeOffset(DateTime.ParseExact(part.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture), TimeSpan.FromHours(-3));

    private static CalendarEvent ToEvent(JsonElement item, string? calendarName, string calendarId)
    {
        var start = item.GetProperty("start");
        var end = item.GetProperty("end");
        return new CalendarEvent(
            item.GetProperty("id").GetString()!,
            item.TryGetProperty("summary", out var summary) ? summary.GetString()! : "Sem título",
            start.TryGetProperty("dateTime", out var sd) ? sd.GetString()! : start.GetProperty("date").GetString()!,
            end.TryGetProperty("dateTime", out var ed) ? ed.GetString()! : end.TryGetProperty("date", out var ed2) ? ed2.GetString()! : string.Empty,
            item.TryGetProperty("location", out var location) ? location.GetString()! : string.Empty,
            item.TryGetProperty("description", out var description) ? description.GetString()! : string.Empty,
            start.TryGetProperty("date", out _),
            calendarName,
            calendarId);
    }

    /// <summary><c>datetime.isoformat()</c>: microssegundos só se houver e deslocamento <c>-03:00</c>.</summary>
    private static string Rfc3339(DateTimeOffset value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var micro = value.Ticks % TimeSpan.TicksPerSecond / 10;
        if (micro != 0)
        {
            text += "." + micro.ToString("D6", CultureInfo.InvariantCulture);
        }

        var offset = value.Offset;
        return text + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString("hh\\:mm", CultureInfo.InvariantCulture);
    }

    private sealed record FakeCalendar(string Id, string Summary, bool Selected, bool Primary, List<JsonElement> Events);
}
