using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// <c>Criar Evento</c> (<c>calendar_intents.py:CriarEventoIntent</c>): extrai os dados no Gemini, grava o evento pendente no Redis
/// (TTL de 300 s) e pede a confirmação. O evento só vai ao Google no "sim" (<see cref="EventReplyStep"/>).
/// </summary>
public sealed class CreateEventIntent(ILlmClient llm, IPendingEventStore events, UserAddresses addresses, IClock clock) : IWhatsAppIntent
{
    public string Name => "Criar Evento";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.EventCreation(context.Message, clock.Today), context.User.Id, cancellationToken);

        var title = GeminiFields.Get(data, "titulo");
        var date = GeminiFields.Get(data, "data");
        if (!GeminiFields.Truthy(title) || !GeminiFields.Truthy(date))
        {
            return EventMessages.MissingTitleOrDate;
        }

        var dateText = GeminiFields.Text(date)!;
        DateOnly eventDate;
        switch (GeminiFields.StringOnly(date))
        {
            case "hoje":
                eventDate = clock.Today;
                break;
            case "amanha":
                eventDate = clock.Today.AddDays(1);
                break;
            default:
                if (GeminiFields.StringOnly(date) is not { } text || !EventMessages.TryParseIsoDate(text, out eventDate))
                {
                    return EventMessages.InvalidDate(dateText);
                }

                break;
        }

        var pending = new PendingEvent(
            context.User.Id,
            GeminiFields.Text(title)!,
            eventDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            GeminiFields.Text(GeminiFields.Get(data, "hora_inicio")),
            GeminiFields.Text(GeminiFields.Get(data, "hora_fim")),
            GeminiFields.Text(GeminiFields.Get(data, "descricao")),
            GeminiFields.Text(GeminiFields.Get(data, "localizacao")));

        var id = await events.CreateAsync(context.Number, pending, cancellationToken);
        if (id is null)
        {
            return EventMessages.PendingError;
        }

        bool? hasAddresses = null;
        if (EventMessages.NeedsAddressLookup(pending.Location, pending.UserId))
        {
            hasAddresses = (await addresses.GetAsync(pending.UserId, cancellationToken)).Count > 0;
        }

        return EventMessages.Confirmation(
            pending.Title, pending.EventDate, pending.StartTime, pending.EndTime, pending.Description, pending.Location, hasAddresses);
    }
}

/// <summary>
/// <c>Deletar Evento</c> (<c>DeletarEventoIntent</c>): busca o título nos próximos 90 dias (até 5 por calendário); um achado é
/// deletado, vários são listados. A falha em buscar vira "não encontrei" (a busca do Flask engole tudo).
/// </summary>
public sealed class DeleteEventIntent(ILlmClient llm, ICalendarClient calendar, IClock clock) : IWhatsAppIntent
{
    private const int PerCalendarLimit = 5;

    public string Name => "Deletar Evento";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.EventDeletion(context.Message), context.User.Id, cancellationToken);

        var search = GeminiFields.Get(data, "titulo_busca");
        if (!GeminiFields.Truthy(search))
        {
            return EventMessages.MissingDeletionTitle;
        }

        var searchText = GeminiFields.Text(search)!;
        var when = GeminiFields.Get(data, "quando");

        // Desvio 192: só "hoje" e "amanha" filtram; qualquer outro valor é tratado como sem filtro (o Flask usava amanhã).
        var whenValue = GeminiFields.StringOnly(when);
        var whenTruthy = whenValue is "hoje" or "amanha";
        var whenText = whenTruthy ? whenValue : null;

        var found = await FindAsync(context.User.Id, searchText, cancellationToken);

        if (whenTruthy && found.Count > 0)
        {
            var target = whenValue == "hoje" ? clock.Today : clock.Today.AddDays(1);
            found = [.. found.Where(item => StartDay(item.Start) == target)];
        }

        if (found.Count == 0)
        {
            return EventMessages.DeleteNothingFound(searchText, whenText);
        }

        if (found.Count == 1)
        {
            var only = found[0];
            return await DeleteAsync(context.User.Id, only, cancellationToken);
        }

        return EventMessages.DeleteMany(
            [.. found.Select(item => new DeletionCandidate(item.Id, item.Summary, item.Start, item.CalendarName ?? string.Empty))]);
    }

    /// <summary><c>date.fromisoformat(evento['start'].split('T')[0])</c>.</summary>
    private static DateOnly StartDay(string start) =>
        EventMessages.TryParseIsoDate(start.Split('T')[0], out var day) ? day : throw new FormatException("Data de evento inválida.");

    private async Task<IReadOnlyList<CalendarEvent>> FindAsync(int userId, string search, CancellationToken cancellationToken)
    {
        IReadOnlyList<CalendarEvent> all;
        try
        {
            all = await calendar.FindEventsByTitleAsync(userId, search, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        // max_results=5 por calendário: o cliente devolve até 10 por calendário já ordenados por início, então os 5 primeiros são os mesmos.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var limited = new List<CalendarEvent>();
        foreach (var item in all)
        {
            var key = item.CalendarId ?? string.Empty;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            if (counts[key] <= PerCalendarLimit)
            {
                limited.Add(item);
            }
        }

        return limited;
    }

    private async Task<string> DeleteAsync(int userId, CalendarEvent item, CancellationToken cancellationToken)
    {
        try
        {
            var result = await calendar.DeleteEventAsync(userId, item.Id, item.CalendarId ?? "primary", cancellationToken);
            return result.Found
                ? EventMessages.DeleteResult(true, EventMessages.DeletedMessage(result.Title))
                : EventMessages.DeleteResult(false, EventMessages.DeleteNotFound);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EventMessages.DeleteResult(false, EventMessages.DeleteFailed);
        }
    }
}

/// <summary><c>Consultar Agenda</c> (<c>ConsultarAgendaIntent</c>): período e filtro de horário em duas chamadas ao Gemini.</summary>
public sealed class AgendaIntent(ILlmClient llm, AgendaService agenda) : IWhatsAppIntent
{
    public string Name => "Consultar Agenda";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        // "Tenho compromisso dia 15 de novembro?": data citada vale mais que o período do Gemini, que só conhece períodos fixos.
        if (AgendaDateParser.TryParse(context.Message, out var day, out var month))
        {
            return await agenda.HasEventsOnAsync(context.User.Id, day, month, cancellationToken);
        }

        var calendarData = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.CalendarQuery(context.Message), context.User.Id, cancellationToken);
        var period = GeminiFields.StringOr(calendarData, "period_type", "hoje");

        var timeData = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.TimeFilter(context.Message), context.User.Id, cancellationToken);
        var filter = GeminiFields.Get(timeData, "time_filter");

        return GeminiFields.Truthy(filter)
            ? await agenda.QueryWithTimeFilterAsync(context.User.Id, period, GeminiFields.StringOnly(filter) ?? string.Empty, cancellationToken)
            : await agenda.QueryAsync(context.User.Id, period, cancellationToken);
    }
}

/// <summary><c>Horários Livres</c> (<c>HorariosLivresIntent</c>): horários livres da agenda e, havendo contexto, a sugestão da IA.</summary>
public sealed class FreeSlotsIntent(ILlmClient llm, FreeTimeService freeTime) : IWhatsAppIntent
{
    public string Name => "Horários Livres";

    public bool Swallows => true;

    public async Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken)
    {
        var data = await GeminiFields.ObjectAsync(llm, WhatsAppPrompts.FreeTimeQuery(context.Message), context.User.Id, cancellationToken);

        var period = GeminiFields.StringOr(data, "period_type", "hoje");
        var duration = DurationOf(data);
        var contextValue = GeminiFields.Get(data, "contexto");

        var result = await freeTime.FindAsync(context.User.Id, period, duration, cancellationToken);

        var contextText = GeminiFields.Truthy(contextValue) ? GeminiFields.Text(contextValue)! : null;
        var message = FreeSlotsMessage.Format(result, contextText);

        if (contextText is not null && result.Slots.Count > 0)
        {
            var suggestion = await freeTime.SuggestAsync(result, contextText, result.Insights, context.User.Id, cancellationToken);
            if (!string.IsNullOrEmpty(suggestion))
            {
                message += $"\n\n{suggestion}";
            }
        }

        return message;
    }

    /// <summary><c>get('duracao_minutos', 60)</c>: número (ou booleano) vale; <c>null</c> e outros tipos fazem a comparação lançar depois.</summary>
    private static double? DurationOf(JsonElement data)
    {
        if (!data.TryGetProperty("duracao_minutos", out var value))
        {
            return 60;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => null,
        };
    }
}
