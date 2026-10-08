using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>
/// "sim"/"confirmar"/"ok"/"s" criam e "não"/"cancelar"/"desistir"/"n" descartam o evento pendente mais recente
/// (<c>whatsapp_handler.py:133-174</c>); sem evento pendente o pipeline segue. "calcular rota" fica com <see cref="RouteRequestStep"/>.
/// </summary>
public sealed class EventReplyStep(IPendingEventStore events, ICalendarClient calendar) : IWhatsAppStep
{
    public int Order => StepOrder.EventReply;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (MessageIntent.WantsRoute(context.Lower) || !MessageIntent.IsEventAnswer(context.Lower))
        {
            return null;
        }

        var latest = await events.GetLatestAsync(context.Number, cancellationToken);
        if (latest.Id is null || latest.Data is null)
        {
            return null;
        }

        return MessageIntent.IsEventConfirm(context.Lower)
            ? await ConfirmAsync(context.Number, latest.Id, latest.Data, cancellationToken)
            : await CancelAsync(context.Number, latest.Id, cancellationToken);
    }

    /// <summary><c>EventConfirmationService.confirm_and_create_event</c>: lê de novo, cria no Google e só apaga a pendência se criou.</summary>
    private async Task<string> ConfirmAsync(string number, string id, PendingEvent first, CancellationToken cancellationToken)
    {
        var data = await events.GetAsync(number, id, cancellationToken);
        if (data is null)
        {
            // O Flask prefixa "❌ " na mensagem que já começa com "❌ ".
            return EventMessages.ConfirmFailed(EventMessages.NoPendingToConfirm);
        }

        // date.fromisoformat fora do try do Flask: data ilegível sobe (500), não vira mensagem.
        if (!EventMessages.TryParseIsoDate(data.EventDate, out var date))
        {
            throw new FormatException("Data do evento pendente inválida.");
        }

        var failure = await TryCreateAsync(data, date, cancellationToken);
        if (failure is not null)
        {
            return EventMessages.ConfirmFailed(failure);
        }

        await events.DeleteAsync(number, id, cancellationToken);
        return EventMessages.Created(first.Title, EventMessages.CreatedMessage(data.Title));
    }

    /// <summary>
    /// <c>CalendarManagementService.create_event</c>: horas <c>HH:MM</c> inválidas e falhas do Google viram "Erro ao criar evento."
    /// (o motivo só aparece para "usuário sem Google conectado", desvio 008). Devolve <c>null</c> quando criou.
    /// </summary>
    private async Task<string?> TryCreateAsync(PendingEvent data, DateOnly date, CancellationToken cancellationToken)
    {
        TimeOnly? start = null;
        if (data.StartTime is not null)
        {
            if (!PyTimeText.TryParseHourMinute(data.StartTime, out var parsedStart))
            {
                return EventMessages.CreateFailed;
            }

            start = parsedStart;
        }

        TimeOnly? end = null;
        if (start is not null && data.EndTime is not null)
        {
            if (!PyTimeText.TryParseHourMinute(data.EndTime, out var parsedEnd))
            {
                return EventMessages.CreateFailed;
            }

            end = parsedEnd;
        }

        try
        {
            await calendar.InsertEventAsync(
                data.UserId,
                new NewCalendarEvent(
                    data.Title,
                    date,
                    start,
                    end,
                    string.IsNullOrEmpty(data.Description) ? null : data.Description,
                    string.IsNullOrEmpty(data.Location) ? null : data.Location),
                cancellationToken);
            return null;
        }
        catch (GoogleNotConnectedException ex)
        {
            return EventMessages.CreateFailedBecause(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return EventMessages.CreateFailed;
        }
    }

    private async Task<string> CancelAsync(string number, string id, CancellationToken cancellationToken)
    {
        var data = await events.GetAsync(number, id, cancellationToken);
        if (data is null)
        {
            return EventMessages.NoPendingToCancel;
        }

        await events.DeleteAsync(number, id, cancellationToken);
        return EventMessages.Cancelled;
    }
}

/// <summary>
/// "calcular rota" (<c>whatsapp_handler.py:176-266</c>): tempo de deslocamento do endereço favorito até o local do evento pendente.
/// Sempre responde. Com vários endereços pergunta a origem, mas a resposta não é tratada (desvio 156, proposto). O limite diário de
/// rotas não é aplicado (desvio 055).
/// </summary>
public sealed class RouteRequestStep(IPendingEventStore events, UserAddresses addresses, IRouteClient routes) : IWhatsAppStep
{
    public int Order => StepOrder.RouteRequest;

    public async Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (!MessageIntent.WantsRoute(context.Lower))
        {
            return null;
        }

        var latest = await events.GetLatestAsync(context.Number, cancellationToken);
        if (latest.Data is not { } pending)
        {
            return EventMessages.RouteNoPendingEvent;
        }

        if (string.IsNullOrEmpty(pending.Location))
        {
            return EventMessages.RouteNoLocation;
        }

        var list = await addresses.GetAsync(pending.UserId, cancellationToken);
        if (list.Count == 0)
        {
            return EventMessages.RouteNoAddresses;
        }

        if (list.Count > 1)
        {
            return EventMessages.RouteAskOrigin(list);
        }

        var origin = list[0];

        // Chave do sistema (sem usuário), como o TravelTimeService.geocode_address do Flask.
        var destination = await routes.GeocodeAsync(pending.Location, null, cancellationToken);
        if (destination is null || destination.Lat == 0 || destination.Lon == 0)
        {
            return EventMessages.RouteDestinationNotFound(pending.Location);
        }

        // Endereço sem coordenadas: o OpenRouteService recusa a rota e o Flask responde que não conseguiu calcular.
        TravelResult? travel = null;
        if (origin.Lat is { } originLat && origin.Lon is { } originLon)
        {
            travel = await routes.CalculateTravelTimeAsync(
                new Coordinates(originLat, originLon), new Coordinates(destination.Lat, destination.Lon), null, cancellationToken);
        }

        if (travel is null)
        {
            return EventMessages.RouteCalculationFailed;
        }

        return EventMessages.RouteCalculated(
            origin.Label,
            string.IsNullOrEmpty(destination.Label) ? pending.Location : destination.Label,
            travel.DurationMinutes,
            PyFloat.Repr(travel.DistanceKm));
    }
}
