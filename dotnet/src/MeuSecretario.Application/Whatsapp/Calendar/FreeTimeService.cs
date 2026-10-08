using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// Endereços favoritos do usuário na ordem que o WhatsApp usa (<c>casa</c>, <c>trabalho</c>, <c>outro</c>;
/// <c>UserAddressService.get_user_addresses</c>). Qualquer falha de leitura vira lista vazia, como o Flask.
/// </summary>
public sealed class UserAddresses(IFavoriteAddressRepository repository)
{
    public async Task<IReadOnlyList<UserAddress>> GetAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await repository.ListAsync(userId, cancellationToken);
            return
            [
                .. rows
                    .OrderBy(row => Rank(row.Label))
                    .Select(row => new UserAddress(
                        row.Label,
                        row.Address,
                        row.Latitude is { } lat && lat != 0 ? (double)lat : null,
                        row.Longitude is { } lon && lon != 0 ? (double)lon : null)),
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    private static int Rank(string label) => label switch
    {
        "casa" => 1,
        "trabalho" => 2,
        "outro" => 3,
        _ => 4,
    };
}

/// <summary>
/// Horários livres (<c>FreeTimeFinderService.find_free_slots</c> e <c>suggest_best_slot_with_ai</c>) e a análise de padrões
/// (<c>UserSchedulePatternService.analyze_user_patterns</c>) sobre <see cref="ICalendarClient"/>.
/// </summary>
public sealed class FreeTimeService(ICalendarClient calendar, IGoogleTokenRepository tokens, ILlmClient llm, IClock clock)
{
    private const int HistoryDays = 90;
    private const int HistoryMaxResults = 500;

    public async Task<FreeSlotsResult> FindAsync(int userId, string? periodType, double? durationMinutes, CancellationToken cancellationToken)
    {
        var patterns = await AnalyzePatternsAsync(userId, cancellationToken);

        var span = AgendaPeriods.ForFreeTime(periodType, clock.Today);
        if (span is not { } range)
        {
            return FreeSlots.Invalid();
        }

        var events = await FetchEventsAsync(userId, range, cancellationToken);
        return FreeSlots.Find(range, events, durationMinutes, patterns, clock.Now, clock.TimeZone);
    }

    /// <summary>
    /// A sugestão do Gemini (desvio 063: com a chave do usuário). Sem horários não pergunta; qualquer falha devolve <c>null</c>.
    /// </summary>
    public async Task<string?> SuggestAsync(
        FreeSlotsResult result, string context, string preferences, int userId, CancellationToken cancellationToken)
    {
        if (result.Slots.Count == 0)
        {
            return null;
        }

        try
        {
            var prompt = CalendarPrompts.Suggestion(result.Slots, context, preferences);
            var text = await llm.GenerateTextAsync(new LlmRequest(prompt, userId), cancellationToken);
            return "🤖 *Sugestão IA:* " + text.Trim();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Sem tokens do Google, sem conexão, com falha ao ler ou sem eventos nos últimos 90 dias valem os padrões.</summary>
    private async Task<UserPatterns> AnalyzePatternsAsync(int userId, CancellationToken cancellationToken)
    {
        if (await tokens.FindAsync(userId, cancellationToken) is null)
        {
            return SchedulePatterns.Default;
        }

        var now = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.TimeZone);
        var events = new List<RawEvent>();
        try
        {
            var calendars = await calendar.ListSelectedCalendarsAsync(userId, cancellationToken);
            foreach (var selected in calendars)
            {
                var items = await calendar.ListEventsAsync(
                    userId,
                    new ListEventsQuery(selected.Id, now.AddDays(-HistoryDays), now, MaxResults: HistoryMaxResults),
                    cancellationToken);
                events.AddRange(items.Select(item => new RawEvent(item.AllDay, item.Start, item.End)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SchedulePatterns.Default;
        }

        return events.Count == 0 ? SchedulePatterns.Default : SchedulePatterns.Extract(events, clock.TimeZone);
    }

    /// <summary>Qualquer falha (inclusive de um só calendário) devolve lista vazia: o dia inteiro aparece livre.</summary>
    private async Task<IReadOnlyList<RawEvent>> FetchEventsAsync(int userId, DateSpan range, CancellationToken cancellationToken)
    {
        var min = new DateTimeOffset(range.Start.ToDateTime(TimeOnly.MinValue), OffsetAt(range.Start.ToDateTime(TimeOnly.MinValue)));
        var maxLocal = range.End.ToDateTime(new TimeOnly(23, 59, 59)).AddTicks(9_999_990);
        var max = new DateTimeOffset(maxLocal, OffsetAt(maxLocal));

        var events = new List<RawEvent>();
        try
        {
            var calendars = await calendar.ListSelectedCalendarsAsync(userId, cancellationToken);
            foreach (var selected in calendars)
            {
                var items = await calendar.ListEventsAsync(userId, new ListEventsQuery(selected.Id, min, max), cancellationToken);
                events.AddRange(items.Select(item => new RawEvent(item.AllDay, item.Start, item.End)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }

        return events;
    }

    private TimeSpan OffsetAt(DateTime local) => clock.TimeZone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
}
