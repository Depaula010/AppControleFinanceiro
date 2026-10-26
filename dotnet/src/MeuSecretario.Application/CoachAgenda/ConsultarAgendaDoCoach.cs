using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.CoachAgenda;

namespace MeuSecretario.Application.CoachAgenda;

/// <summary>Configuração da agenda para o Coach (spec 015, RN-015-03 e 04). A implementação nunca lança: falha vira o mais privado.</summary>
public interface ICoachAgendaConfig
{
    Task<NivelDeDetalhe> NivelDeAsync(string? calendarId, CancellationToken cancellationToken);

    Task<bool> MascararAsync(CancellationToken cancellationToken);
}

public enum StatusDaAgendaDoCoach
{
    Ok,
    EntradaInvalida,
    NaoConectado,
    Indisponivel,
}

public sealed record AgendaDoCoach(string De, string Ate, string GeradoEm, IReadOnlyList<EventoParaOCoach> Eventos);

public sealed record ResultadoDaAgendaDoCoach(StatusDaAgendaDoCoach Status, AgendaDoCoach? Agenda)
{
    public static ResultadoDaAgendaDoCoach Falha(StatusDaAgendaDoCoach status) => new(status, null);
}

/// <summary>
/// Spec 015: lê os eventos do Google Calendar do usuário configurado (calendários selecionados) e os devolve já no nível de detalhe de cada calendário e
/// mascarados. Só leitura; nada é guardado (RN-015-07). Teto de 10 s para o Google (RN-015-08).
/// </summary>
public sealed class ConsultarAgendaDoCoach(IGoogleAccountService accounts, ICalendarClient calendar, ICoachAgendaConfig config, IClock clock)
{
    public const int MaximoDeDias = 42;
    public static readonly TimeSpan TetoDoGoogle = TimeSpan.FromSeconds(10);

    public async Task<ResultadoDaAgendaDoCoach> ExecuteAsync(int userId, string? de, string? ate, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParseExact(de, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio)
            || !DateOnly.TryParseExact(ate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fim)
            || fim < inicio
            || fim.DayNumber - inicio.DayNumber + 1 > MaximoDeDias)
        {
            return ResultadoDaAgendaDoCoach.Falha(StatusDaAgendaDoCoach.EntradaInvalida);
        }

        IReadOnlyList<CalendarEvent> eventos;
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TetoDoGoogle);
        try
        {
            if (!await accounts.IsConnectedAsync(userId, limite.Token).ConfigureAwait(false))
            {
                return ResultadoDaAgendaDoCoach.Falha(StatusDaAgendaDoCoach.NaoConectado);
            }

            eventos = await calendar.GetEventsInRangeAsync(userId, inicio, fim, limite.Token).ConfigureAwait(false);
        }
        catch (GoogleNotConnectedException)
        {
            return ResultadoDaAgendaDoCoach.Falha(StatusDaAgendaDoCoach.NaoConectado);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ResultadoDaAgendaDoCoach.Falha(StatusDaAgendaDoCoach.Indisponivel);        // estourou os 10 s
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ResultadoDaAgendaDoCoach.Falha(StatusDaAgendaDoCoach.Indisponivel);        // texto fixo: nada da exceção sai
        }

        // o nível de cada calendário é pedido uma vez só (a configuração tem cache de 60 s)
        var niveis = new Dictionary<string, NivelDeDetalhe>(StringComparer.Ordinal);
        foreach (var id in eventos.Select(e => e.CalendarId ?? string.Empty).Distinct(StringComparer.Ordinal))
        {
            niveis[id] = await config.NivelDeAsync(id.Length == 0 ? null : id, cancellationToken).ConfigureAwait(false);
        }

        var mascarar = await config.MascararAsync(cancellationToken).ConfigureAwait(false);
        var brutos = eventos.Select(e => new EventoBruto(e.Summary, e.Start, e.End, e.Location, e.Description, e.AllDay, e.CalendarName, e.CalendarId));
        var saida = AgendaParaOCoach.Montar(brutos, id => niveis.TryGetValue(id ?? string.Empty, out var n) ? n : NivelDeDetalhe.Ocupado, mascarar);

        return new ResultadoDaAgendaDoCoach(
            StatusDaAgendaDoCoach.Ok,
            new AgendaDoCoach(
                inicio.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), fim.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeZoneInfo.ConvertTime(clock.UtcNow, clock.TimeZone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), saida));
    }
}
