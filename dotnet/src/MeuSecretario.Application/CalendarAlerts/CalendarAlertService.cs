using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.CalendarAlerts;

namespace MeuSecretario.Application.CalendarAlerts;

/// <summary>
/// Alertas de tarefas do Google Calendar por WhatsApp (<c>calendar_alert_service.py</c>, spec 014 §4). O job da 013
/// chama <see cref="ProcessAlertsForUserAsync"/> a cada minuto para cada <see cref="ActiveAlertUser"/>.
/// Nenhum método lança (exceto cancelamento): falha vira lista vazia, <c>false</c> ou 0.
/// </summary>
public sealed class CalendarAlertService(
    ICalendarClient calendar,
    ICache cache,
    IBotClient bot,
    IClock clock,
    ICalendarAlertDiagnostics diagnostics)
{
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromSeconds(7200);
    private const string ClaimValue = "processing";
    private const string NoCalendarName = "Sem nome";

    /// <summary>Eventos com horário que começam na janela alvo ± 30 s e ainda não começaram (RN-014-11 e 12).</summary>
    public async Task<IReadOnlyList<UpcomingEvent>> GetUpcomingEventsAsync(
        int userId, int minutesBefore, CancellationToken cancellationToken)
    {
        try
        {
            var now = TimeZoneInfo.ConvertTime(clock.UtcNow, clock.TimeZone);
            var (windowStart, windowEnd) = AlertWindow.For(now, minutesBefore);

            var calendars = await calendar.ListSelectedCalendarsAsync(userId, cancellationToken);
            var upcoming = new List<UpcomingEvent>();
            foreach (var selected in calendars)
            {
                IReadOnlyList<CalendarEvent> events;
                try
                {
                    events = await calendar.ListEventsAsync(
                        userId, new ListEventsQuery(selected.Id, windowStart, windowEnd), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    diagnostics.CalendarEventsFailed(userId, ex);
                    continue;
                }

                var calendarName = string.IsNullOrEmpty(selected.Summary) ? NoCalendarName : selected.Summary;
                foreach (var item in events)
                {
                    if (!item.AllDay && AlertMessage.TryParseIso(item.Start, out var start) && start > now)
                    {
                        upcoming.Add(new UpcomingEvent(item.Id, item.Summary, item.Start, item.Location, item.Description, calendarName));
                    }
                }
            }

            return upcoming;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Usuário sem Google conectado, token inválido ou qualquer falha: lista vazia (não derruba o job).
            diagnostics.EventsLookupFailed(userId, ex);
            return [];
        }
    }

    /// <summary>Envia o alerta pelo bot; qualquer falha devolve <c>false</c> (RN-014-14).</summary>
    public async Task<bool> SendEventAlertAsync(
        string whatsappNumber, UpcomingEvent upcoming, int minutesBefore, int userId, CancellationToken cancellationToken)
    {
        try
        {
            var message = AlertMessage.Build(upcoming.Summary, upcoming.Start, upcoming.Location, minutesBefore);
            return await bot.SendTextAsync(whatsappNumber, message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.AlertSendFailed(userId, ex);
            return false;
        }
    }

    /// <summary>Alertas enviados neste ciclo para o usuário (RN-014-13 e 15).</summary>
    public async Task<int> ProcessAlertsForUserAsync(
        int userId, string whatsappNumber, int minutesBefore, CancellationToken cancellationToken)
    {
        try
        {
            var events = await GetUpcomingEventsAsync(userId, minutesBefore, cancellationToken);
            var sent = 0;
            foreach (var upcoming in events)
            {
                if (!await TryClaimAsync(userId, upcoming, cancellationToken))
                {
                    continue;
                }

                if (await SendEventAlertAsync(whatsappNumber, upcoming, minutesBefore, userId, cancellationToken))
                {
                    sent++;
                }
                else
                {
                    await ReleaseClaimAsync(userId, upcoming, cancellationToken);
                }
            }

            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ProcessFailed(userId, ex);
            return 0;
        }
    }

    /// <summary><c>SET NX EX 7200</c>; sem Redis prossegue sem lock (fail-open, como o Python).</summary>
    private async Task<bool> TryClaimAsync(int userId, UpcomingEvent upcoming, CancellationToken cancellationToken)
    {
        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            diagnostics.RedisUnavailable(userId);
            return true;
        }

        return await cache.SetIfNotExistsAsync(
            CacheKeys.CalendarAlert(userId, upcoming.Id, upcoming.Start), ClaimValue, ClaimTtl, cancellationToken);
    }

    private async Task ReleaseClaimAsync(int userId, UpcomingEvent upcoming, CancellationToken cancellationToken)
    {
        if (await cache.IsAvailableAsync(cancellationToken))
        {
            await cache.DeleteAsync(CacheKeys.CalendarAlert(userId, upcoming.Id, upcoming.Start), cancellationToken);
        }
    }
}
