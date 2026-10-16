using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Filtro de eventos por período do dia (<c>CalendarQueryService.filter_events_by_time_period</c>).</summary>
public static class AgendaTimeFilter
{
    private static readonly Dictionary<string, (TimeOnly From, TimeOnly To)> Ranges = new(StringComparer.Ordinal)
    {
        ["madrugada"] = (new TimeOnly(0, 0), new TimeOnly(5, 59)),
        ["manha"] = (new TimeOnly(6, 0), new TimeOnly(11, 59)),
        ["tarde"] = (new TimeOnly(12, 0), new TimeOnly(17, 59)),
        ["noite"] = (new TimeOnly(18, 0), new TimeOnly(23, 59)),
    };

    /// <summary>
    /// <c>agora</c>: eventos com horário que começam entre agora e 4 h à frente. Madrugada, manhã, tarde e noite: dia inteiro entra
    /// sempre; os demais precisam começar na faixa (até o minuto da ponta, segundos incluídos) e depois de agora. Outro valor devolve tudo.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> Apply(IReadOnlyList<CalendarEvent> events, string? period, DateTime now, TimeZoneInfo zone)
    {
        if (period == "agora")
        {
            var cutoff = now.AddHours(4);
            return
            [
                .. events.Where(item =>
                {
                    if (item.AllDay)
                    {
                        return false;
                    }

                    var start = Start(item, zone);
                    return start is { } value && value >= now && value <= cutoff;
                }),
            ];
        }

        if (period is null || !Ranges.TryGetValue(period, out var range))
        {
            return events;
        }

        return
        [
            .. events.Where(item =>
            {
                if (item.AllDay)
                {
                    return true;
                }

                var start = Start(item, zone);
                if (start is not { } value)
                {
                    return false;
                }

                var time = TimeOnly.FromDateTime(value);
                return time >= range.From && time <= range.To && value > now;
            }),
        ];
    }

    private static DateTime? Start(CalendarEvent item, TimeZoneInfo zone) =>
        item.Start.Contains('T', StringComparison.Ordinal) ? CalendarTimes.ToLocal(item.Start, zone) : null;
}

/// <summary>
/// Consulta da agenda para o WhatsApp (<c>CalendarQueryService.query_agenda</c> e <c>query_agenda_with_time_filter</c>) sobre
/// <see cref="ICalendarClient"/>. Os formatadores são os da 010 (<see cref="CalendarFormatter"/>).
/// </summary>
public sealed class AgendaService(ICalendarClient calendar, IGoogleAccountService account, GoogleLinks links, IClock clock)
{
    /// <summary>
    /// <c>query_agenda</c>: sem Google conectado devolve o link de conexão (assinado, desvio 053) e qualquer falha vira a mensagem
    /// de erro sem o motivo (desvio 008).
    /// </summary>
    public async Task<string> QueryAsync(int userId, string? periodType, CancellationToken cancellationToken)
    {
        if (!await account.IsConnectedAsync(userId, cancellationToken))
        {
            var url = links.ConnectUrl(userId);
            return url is null ? EventMessages.AgendaNotConfigured : EventMessages.AgendaNotConnected(url);
        }

        try
        {
            var span = AgendaPeriods.ForAgenda(periodType, clock.Today);
            var events = await calendar.GetEventsInRangeAsync(userId, span.Start, span.End, cancellationToken);
            return span.Start == span.End
                ? CalendarFormatter.FormatEventsForWhatsApp(events, span.Start, clock.Today)
                : CalendarFormatter.FormatPeriodEventsForWhatsApp(GroupByDate(events), span.Start, span.End);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EventMessages.AgendaFailed;
        }
    }

    /// <summary>
    /// "Tenho compromisso dia 15 de novembro?": responde sim/não para o dia (sem ano, a próxima ocorrência) e, havendo, lista a
    /// agenda do dia. Mesmas regras de conexão e de erro do <see cref="QueryAsync"/>.
    /// </summary>
    public async Task<string> HasEventsOnAsync(int userId, int day, int month, CancellationToken cancellationToken)
    {
        if (!await account.IsConnectedAsync(userId, cancellationToken))
        {
            var url = links.ConnectUrl(userId);
            return url is null ? EventMessages.AgendaNotConfigured : EventMessages.AgendaNotConnected(url);
        }

        var date = AgendaPeriods.NextOccurrence(day, month, clock.Today);
        if (date is null)
        {
            return $"❌ Data inválida: {day:00}/{month:00}.";
        }

        try
        {
            var events = await calendar.GetEventsInRangeAsync(userId, date.Value, date.Value, cancellationToken);
            var label = date.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            return events.Count == 0
                ? $"📅 Não, você não tem compromisso no dia {label}! 🎉"
                : $"✅ Sim, você tem {events.Count} compromisso(s) no dia {label}.\n\n"
                    + CalendarFormatter.FormatEventsForWhatsApp(events, date.Value, clock.Today);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EventMessages.AgendaFailed;
        }
    }

    /// <summary>
    /// <c>query_agenda_with_time_filter</c>: não confere a conexão nem captura erros (usuário sem Google lança, e a intenção
    /// "template" responde o texto genérico).
    /// </summary>
    public async Task<string> QueryWithTimeFilterAsync(int userId, string? periodType, string timeFilter, CancellationToken cancellationToken)
    {
        var span = AgendaPeriods.ForAgenda(periodType, clock.Today);
        var events = await calendar.GetEventsInRangeAsync(userId, span.Start, span.End, cancellationToken);
        var now = clock.Now;

        if (span.Start == span.End)
        {
            var filtered = AgendaTimeFilter.Apply(events, timeFilter, now, clock.TimeZone);
            return CalendarFormatter.FormatEventsForWhatsApp(filtered, span.Start, clock.Today);
        }

        var grouped = new Dictionary<DateOnly, IReadOnlyList<CalendarEvent>>();
        foreach (var (date, items) in GroupByDate(events))
        {
            var filtered = AgendaTimeFilter.Apply(items, timeFilter, now, clock.TimeZone);
            if (filtered.Count > 0)
            {
                grouped[date] = filtered;
            }
        }

        return CalendarFormatter.FormatPeriodEventsForWhatsApp(grouped, span.Start, span.End);
    }

    /// <summary>
    /// <c>_get_events_for_period</c>: agrupa pelo dia de início (horário convertido para São Paulo) e ordena cada dia pelo texto do início;
    /// evento com início ilegível é pulado.
    /// </summary>
    private Dictionary<DateOnly, IReadOnlyList<CalendarEvent>> GroupByDate(IReadOnlyList<CalendarEvent> events)
    {
        var groups = new Dictionary<DateOnly, List<CalendarEvent>>();
        foreach (var item in events)
        {
            DateOnly date;
            if (item.Start.Contains('T', StringComparison.Ordinal))
            {
                var local = CalendarTimes.ToLocal(item.Start, clock.TimeZone);
                if (local is null)
                {
                    continue;
                }

                date = DateOnly.FromDateTime(local.Value);
            }
            else if (!EventMessages.TryParseIsoDate(item.Start, out date))
            {
                continue;
            }

            if (!groups.TryGetValue(date, out var list))
            {
                list = [];
                groups[date] = list;
            }

            list.Add(item);
        }

        return groups.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CalendarEvent>)[.. pair.Value.OrderBy(item => item.Start, StringComparer.Ordinal)]);
    }
}
