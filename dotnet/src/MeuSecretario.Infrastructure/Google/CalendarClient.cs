using System.Globalization;
using System.Net;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Google;
using Microsoft.Extensions.Logging;
using GoogleApiException = Google.GoogleApiException;
using GoogleHttp = Google.Apis.Http;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>Logs do Calendar e do Drive (EventIds 10450 a 10479): só id numérico, rótulo fixo e tipo da exceção; nunca URL nem token.</summary>
internal static partial class GoogleApiLog
{
    [LoggerMessage(EventId = 10450, Level = LogLevel.Warning, Message = "Falha ao listar calendários (usuário {UserId}); usando só o principal: {Error}")]
    public static partial void CalendarListFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10451, Level = LogLevel.Warning, Message = "Falha ao buscar eventos em um calendário (usuário {UserId}); calendário ignorado: {Error}")]
    public static partial void CalendarEventsFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10452, Level = LogLevel.Warning, Message = "Busca de eventos por título falhou (usuário {UserId}): {Error}")]
    public static partial void FindByTitleFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10453, Level = LogLevel.Warning, Message = "Leitura do evento antes de remover falhou (usuário {UserId}); usando o título padrão: {Error}")]
    public static partial void DeleteTitleLookupFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10460, Level = LogLevel.Warning, Message = "Busca de pasta no Drive falhou (usuário {UserId}); tratando como não encontrada: {Error}")]
    public static partial void DriveFolderSearchFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10461, Level = LogLevel.Error, Message = "Falha no upload para o Drive (usuário {UserId}): {Error}")]
    public static partial void DriveUploadFailed(ILogger logger, int userId, string error);
}

/// <summary>Descrição de exceção segura para log e mensagens: só o tipo (e o código HTTP).</summary>
internal static class GoogleErrors
{
    public static string Describe(Exception exception) =>
        exception is GoogleApiException api ? $"GoogleApiException (HTTP {(int)api.HttpStatusCode})" : exception.GetType().Name;

    /// <summary>Erro do SDK ou de rede vira <see cref="CalendarApiException"/> sem corpo, URL nem token.</summary>
    public static CalendarApiException ToCalendarException(Exception exception) => exception switch
    {
        GoogleApiException api => new CalendarApiException(
            $"Falha na API do Google Calendar (HTTP {(int)api.HttpStatusCode})", api.HttpStatusCode, api),
        _ => new CalendarApiException($"Falha ao falar com o Google Calendar ({exception.GetType().Name})", null, exception),
    };

    public static bool IsExpected(Exception exception) =>
        exception is GoogleApiException or HttpRequestException or TaskCanceledException or GoogleNotConnectedException
        && exception is not OperationCanceledException { CancellationToken.IsCancellationRequested: true };
}

/// <summary>
/// Google Calendar do usuário (RN-010-46 a 49). O token vem de <see cref="IGoogleAccountService"/> (que renova, D-4); o SDK
/// recebe só o <c>access_token</c>. Nenhum tipo do SDK atravessa a porta.
/// </summary>
public sealed class CalendarClient(
    IGoogleAccountService account,
    GoogleHttp.IHttpClientFactory httpFactory,
    IClock clock,
    ILogger<CalendarClient> logger) : ICalendarClient
{
    public const string NotConnectedMessage = "Usuário não conectou Google Calendar";
    private const string Primary = "primary";
    private const string NoTitle = "Sem título";

    public async Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        return await ListSelectedAsync(service, cancellationToken);
    }

    public async Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        try
        {
            var request = service.Events.List(query.CalendarId);
            SetTimeWindow(
                request,
                query.TimeMinRaw ?? (query.TimeMin is { } min ? Rfc3339(min) : null),
                query.TimeMaxRaw ?? (query.TimeMax is { } max ? Rfc3339(max) : null));

            request.SingleEvents = true;
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.Q = query.Query;
            request.MaxResults = query.MaxResults;

            var response = await request.ExecuteAsync(cancellationToken);
            return Map(response.Items, calendarName: null, query.CalendarId);
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(
        int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        var calendars = await ListCalendarsOrPrimaryAsync(service, userId, cancellationToken);

        // time.min e time.max do Python: 00:00:00 e 23:59:59.999999, no deslocamento de São Paulo daquela data.
        var min = Rfc3339(WithOffset(startDate.ToDateTime(TimeOnly.MinValue)));
        var max = Rfc3339(WithOffset(endDate.ToDateTime(new TimeOnly(23, 59, 59)).AddTicks(9_999_990)));

        var all = new List<CalendarEvent>();
        foreach (var calendar in calendars)
        {
            try
            {
                var request = service.Events.List(calendar.Id);
                SetTimeWindow(request, min, max);
                request.SingleEvents = true;
                request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                var response = await request.ExecuteAsync(cancellationToken);
                all.AddRange(Map(response.Items, calendar.Summary, calendar.Id));
            }
            catch (Exception ex) when (GoogleErrors.IsExpected(ex))
            {
                GoogleApiLog.CalendarEventsFailed(logger, userId, GoogleErrors.Describe(ex));
            }
        }

        // sort estável por texto de início (como o sort do Python).
        return [.. all.OrderBy(e => e.Start, StringComparer.Ordinal)];
    }

    public async Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        try
        {
            var found = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);
            return ToCalendarEvent(found, null, calendarId);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    public async Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        var payload = CalendarEventPayloads.ForNew(
            newEvent.Summary, newEvent.Date, newEvent.StartTime, newEvent.EndTime, newEvent.Description, newEvent.Location, clock.TimeZone);

        var body = new Event
        {
            Summary = payload.Summary,
            Description = payload.Description,
            Location = payload.Location,
            Start = ToEventDateTime(payload.Start),
            End = ToEventDateTime(payload.End),
        };

        try
        {
            var created = await service.Events.Insert(body, newEvent.CalendarId).ExecuteAsync(cancellationToken);
            return created.Id;
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    public async Task<bool> UpdateEventAsync(
        int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);
        try
        {
            var current = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);

            if (!string.IsNullOrEmpty(update.Summary))
            {
                current.Summary = update.Summary;
            }

            if (update.Description is not null)
            {
                current.Description = update.Description;
            }

            if (update.Location is not null)
            {
                current.Location = update.Location;
            }

            var times = CalendarEventPayloads.ForUpdate(
                new ExistingEventTimes(current.Start?.Date, current.Start?.DateTimeRaw, current.End?.Date, current.End?.DateTimeRaw),
                update.Date,
                update.StartTime,
                update.EndTime,
                clock.TimeZone);
            if (times is { } change)
            {
                current.Start = ToEventDateTime(change.Start);
                current.End = ToEventDateTime(change.End);
            }

            await service.Events.Update(current, calendarId, eventId).ExecuteAsync(cancellationToken);
            return true;
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    public async Task<CalendarDeleteResult> DeleteEventAsync(
        int userId, string eventId, string calendarId, CancellationToken cancellationToken)
    {
        using var service = await OpenAsync(userId, cancellationToken);

        // Lê o título só para a mensagem; qualquer falha da leitura vira o título "Evento" (como o Python).
        var title = "Evento";
        try
        {
            var current = await service.Events.Get(calendarId, eventId).ExecuteAsync(cancellationToken);
            title = current.Summary ?? NoTitle;
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            GoogleApiLog.DeleteTitleLookupFailed(logger, userId, GoogleErrors.Describe(ex));
        }

        try
        {
            await service.Events.Delete(calendarId, eventId).ExecuteAsync(cancellationToken);
            return new CalendarDeleteResult(true, title);
        }
        catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return new CalendarDeleteResult(false, title);
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    public async Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken)
    {
        try
        {
            using var service = await OpenAsync(userId, cancellationToken);
            var calendars = await ListCalendarsOrPrimaryAsync(service, userId, cancellationToken);

            var now = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.TimeZone);
            var min = Rfc3339(now);
            var max = Rfc3339(now.AddDays(90));

            var found = new List<CalendarEvent>();
            foreach (var calendar in calendars)
            {
                try
                {
                    var request = service.Events.List(calendar.Id);
                    SetTimeWindow(request, min, max);
                    request.Q = title;
                    request.MaxResults = 10;
                    request.SingleEvents = true;
                    request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
                    var response = await request.ExecuteAsync(cancellationToken);
                    found.AddRange(Map(response.Items, calendar.Summary, calendar.Id));
                }
                catch (Exception ex) when (GoogleErrors.IsExpected(ex))
                {
                    GoogleApiLog.CalendarEventsFailed(logger, userId, GoogleErrors.Describe(ex));
                }
            }

            return found;
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            // Como o Python: qualquer falha (inclusive "não conectado") vira lista vazia.
            GoogleApiLog.FindByTitleFailed(logger, userId, GoogleErrors.Describe(ex));
            return [];
        }
    }

    // ------------------------------------------------------------------------------------------------

    private async Task<CalendarService> OpenAsync(int userId, CancellationToken cancellationToken)
    {
        var credentials = await account.GetCredentialsAsync(userId, cancellationToken);
        if (credentials.Status != GoogleCredentialStatus.Connected || credentials.AccessToken is null)
        {
            throw new GoogleNotConnectedException(NotConnectedMessage);
        }

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(credentials.AccessToken),
            HttpClientFactory = httpFactory,
            ApplicationName = "MeuSecretario",
            GZipEnabled = false, // o corpo enviado fica legível para o teste e idêntico ao do Python (que não comprime)
        });
    }

    private static async Task<IReadOnlyList<CalendarInfo>> ListSelectedAsync(CalendarService service, CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.CalendarList.List().ExecuteAsync(cancellationToken);
            return
            [
                .. (response.Items ?? [])
                    .Where(entry => entry.Selected == true)
                    .Select(entry => new CalendarInfo(entry.Id, entry.Summary ?? "Sem nome", entry.Primary == true)),
            ];
        }
        catch (Exception ex) when (GoogleErrors.IsExpected(ex))
        {
            throw GoogleErrors.ToCalendarException(ex);
        }
    }

    /// <summary>Falha ao listar calendários ⇒ só o principal, com o nome <c>Primary</c> (RN-010-47).</summary>
    private async Task<IReadOnlyList<CalendarInfo>> ListCalendarsOrPrimaryAsync(
        CalendarService service, int userId, CancellationToken cancellationToken)
    {
        try
        {
            return await ListSelectedAsync(service, cancellationToken);
        }
        catch (CalendarApiException ex)
        {
            GoogleApiLog.CalendarListFailed(logger, userId, ex.GetType().Name + (ex.StatusCode is { } s ? $" (HTTP {(int)s})" : string.Empty));
            return [new CalendarInfo(Primary, "Primary", true)];
        }
    }

    private static List<CalendarEvent> Map(IList<Event>? items, string? calendarName, string calendarId) =>
        [.. (items ?? []).Select(item => ToCalendarEvent(item, calendarName, calendarId))];

    private static CalendarEvent ToCalendarEvent(Event item, string? calendarName, string calendarId)
    {
        var start = item.Start?.DateTimeRaw ?? item.Start?.Date ?? string.Empty;
        var end = item.End?.DateTimeRaw ?? item.End?.Date ?? string.Empty;
        return new CalendarEvent(
            item.Id ?? string.Empty,
            item.Summary ?? NoTitle,
            start,
            end,
            item.Location ?? string.Empty,
            item.Description ?? string.Empty,
            item.Start?.Date is not null,
            calendarName,
            calendarId);
    }

    private static EventDateTime ToEventDateTime(CalendarTime time) => new()
    {
        Date = time.Date,
        DateTimeRaw = time.DateTimeText,
        TimeZone = time.TimeZone,
    };

    private DateTimeOffset WithOffset(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, clock.TimeZone.GetUtcOffset(unspecified));
    }

    /// <summary>
    /// <c>datetime.isoformat()</c> do Python: segundos, microssegundos só se houver, deslocamento <c>-03:00</c>. O setter de
    /// <c>DateTimeOffset</c> do SDK escreveria <c>-03</c> (sem os minutos), que não é RFC 3339.
    /// </summary>
    internal static string Rfc3339(DateTimeOffset value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = value.Ticks % TimeSpan.TicksPerSecond / 10;
        if (microseconds != 0)
        {
            text += "." + microseconds.ToString("D6", CultureInfo.InvariantCulture);
        }

        return text + CalendarEventPayloads.OffsetText(value.Offset);
    }

    private static readonly System.Reflection.PropertyInfo? TimeMinProperty = typeof(EventsResource.ListRequest).GetProperty("TimeMinRaw");
    private static readonly System.Reflection.PropertyInfo? TimeMaxProperty = typeof(EventsResource.ListRequest).GetProperty("TimeMaxRaw");

    /// <summary>
    /// <c>TimeMinRaw</c> e <c>TimeMaxRaw</c> só têm setter privado no SDK; escrevemos o texto RFC 3339 exato. Se uma versão
    /// futura do SDK mudar isso, o fallback usa o setter público (<c>DateTimeOffset</c>) e os testes do payload acusam a mudança.
    /// </summary>
    private static void SetTimeWindow(EventsResource.ListRequest request, string? min, string? max)
    {
        SetRaw(request, TimeMinProperty, min, value => request.TimeMinDateTimeOffset = value);
        SetRaw(request, TimeMaxProperty, max, value => request.TimeMaxDateTimeOffset = value);
    }

    private static void SetRaw(EventsResource.ListRequest request, System.Reflection.PropertyInfo? property, string? text, Action<DateTimeOffset> fallback)
    {
        if (text is null)
        {
            return;
        }

        if (property?.GetSetMethod(nonPublic: true) is { } setter)
        {
            setter.Invoke(request, [text]);
        }
        else if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            fallback(parsed);
        }
    }
}
