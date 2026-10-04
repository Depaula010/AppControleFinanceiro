namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Google Calendar do usuário (spec 010 RN-010-47 a 49). Lança <see cref="GoogleNotConnectedException"/> sem credencial.
/// Nenhum tipo do SDK do Google atravessa esta porta.
/// </summary>
public interface ICalendarClient
{
    /// <summary>Só os calendários com <c>selected == true</c>.</summary>
    Task<IReadOnlyList<CalendarInfo>> ListSelectedCalendarsAsync(int userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarEvent>> ListEventsAsync(int userId, ListEventsQuery query, CancellationToken cancellationToken);

    /// <summary>Multi-calendário (RN-010-47): falha ao listar usa só <c>primary</c>; calendário com erro é pulado; ordenado por <c>start</c>.</summary>
    Task<IReadOnlyList<CalendarEvent>> GetEventsInRangeAsync(int userId, DateOnly startDate, DateOnly endDate, CancellationToken cancellationToken);

    Task<CalendarEvent?> GetEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken);

    /// <summary>Devolve o id do evento criado. Falha da API lança <see cref="CalendarApiException"/>.</summary>
    Task<string> InsertEventAsync(int userId, NewCalendarEvent newEvent, CancellationToken cancellationToken);

    /// <summary><c>false</c> se o evento não existe (HTTP 404). Outra falha da API lança <see cref="CalendarApiException"/>.</summary>
    Task<bool> UpdateEventAsync(int userId, string eventId, string calendarId, EventUpdate update, CancellationToken cancellationToken);

    /// <summary>
    /// Remove o evento; <see cref="CalendarDeleteResult.Found"/> falso = HTTP 404 ("Evento não encontrado ou já foi deletado.").
    /// O título vem da leitura prévia (<c>Evento</c> se ela falhar, <c>Sem título</c> se o evento não tiver título).
    /// </summary>
    Task<CalendarDeleteResult> DeleteEventAsync(int userId, string eventId, string calendarId, CancellationToken cancellationToken);

    /// <summary>
    /// Busca por título nos próximos 90 dias em todos os calendários selecionados. Como o Python, qualquer falha
    /// (inclusive usuário sem Google conectado) vira lista vazia; calendário com erro é pulado.
    /// </summary>
    Task<IReadOnlyList<CalendarEvent>> FindEventsByTitleAsync(int userId, string title, CancellationToken cancellationToken);
}

/// <summary>Falha da API do Google Calendar (sem corpo, sem URL, sem token na mensagem).</summary>
public sealed class CalendarApiException(string message, System.Net.HttpStatusCode? statusCode, Exception? inner = null)
    : Exception(message, inner)
{
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;
}

public sealed record CalendarDeleteResult(bool Found, string Title);

public sealed record CalendarInfo(string Id, string Summary, bool Primary);

/// <param name="Start">Texto <c>dateTime</c> ou <c>date</c> da API.</param>
public sealed record CalendarEvent(
    string Id,
    string Summary,
    string Start,
    string End,
    string Location,
    string Description,
    bool AllDay,
    string? CalendarName,
    string? CalendarId = null);

/// <param name="StartTime">Nulo = evento de dia inteiro.</param>
/// <param name="EndTime">Nulo = <paramref name="StartTime"/> mais 1 hora.</param>
public sealed record NewCalendarEvent(
    string Summary,
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Description,
    string? Location,
    string CalendarId = "primary");

/// <summary><c>null</c> = não alterar; <c>""</c> em descrição/local limpa o campo (RN-010-49).</summary>
public sealed record EventUpdate(
    string? Summary,
    DateOnly? Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Description,
    string? Location);

/// <param name="TimeMin">Ou <paramref name="TimeMinRaw"/>: texto RFC 3339 com <c>-03:00</c> quando o formato exato importa.</param>
public sealed record ListEventsQuery(
    string CalendarId,
    DateTimeOffset? TimeMin,
    DateTimeOffset? TimeMax,
    string? TimeMinRaw = null,
    string? TimeMaxRaw = null,
    string? Query = null,
    int? MaxResults = null);
