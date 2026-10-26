using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Domain.CoachAgenda;
using MeuSecretario.Infrastructure.CoachAgenda;
using MeuSecretario.Infrastructure.Security;

namespace MeuSecretario.Api.CoachAgenda;

/// <summary>A chave própria da rota (<c>COACH_AGENDA_API_KEY</c>), validada em tempo constante. Não é a <c>API_SECRET_KEY</c> (AC-015-02).</summary>
public sealed class CoachAgendaApiKey(IApiKeyValidator validator)
{
    public bool IsValid(string? presented) => validator.IsValid(presented);
}

/// <summary>
/// Feature 015 (parte B): <c>GET /internal/coach/agenda</c>, somente leitura, para o Coach (projeto amazfit-dados). <b>Desligada por padrão</b>: sem
/// <c>COACH_AGENDA_API_KEY</c> e <c>COACH_AGENDA_USER_ID</c> a rota não é registrada (404) e nada mais muda. Nenhuma rota existente é tocada.
/// </summary>
public sealed class CoachAgendaModule : IFeatureModule
{
    public const string Caminho = "/internal/coach/agenda";
    public const string ChaveDaRota = "COACH_AGENDA_API_KEY";
    public const string UsuarioDaAgenda = "COACH_AGENDA_USER_ID";

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ICoachAgendaConfig, DbCoachAgendaConfig>();
        services.AddSingleton(sp => new CoachAgendaApiKey(new ApiSecretKeyValidator(sp.GetRequiredService<IConfiguration>()[ChaveDaRota] ?? string.Empty)));
        services.AddScoped<ConsultarAgendaDoCoach>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var config = app.ServiceProvider.GetRequiredService<IConfiguration>();
        if (string.IsNullOrWhiteSpace(config[ChaveDaRota])
            || !int.TryParse(config[UsuarioDaAgenda], NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
            || userId <= 0)
        {
            return;                                                                      // RN-015-01: desligada
        }

        app.MapGet(Caminho, async (HttpContext http, CoachAgendaApiKey key, ConsultarAgendaDoCoach useCase, ILogger<CoachAgendaModule> logger, CancellationToken ct) =>
        {
            // a chave vem antes de tudo: sem ela nem a query é lida
            if (!key.IsValid(http.Request.Headers["X-API-KEY"].FirstOrDefault()))
            {
                return Erro("Chave inválida", StatusCodes.Status401Unauthorized);
            }

            var result = await useCase.ExecuteAsync(userId, http.Request.Query["de"].FirstOrDefault(), http.Request.Query["ate"].FirstOrDefault(), ct);
            switch (result.Status)
            {
                case StatusDaAgendaDoCoach.Ok:
                    CoachAgendaLog.Atendida(logger, result.Agenda!.Eventos.Count, result.Agenda.De, result.Agenda.Ate);
                    return Envelopes.Json(Corpo(result.Agenda), StatusCodes.Status200OK);
                case StatusDaAgendaDoCoach.EntradaInvalida:
                    return Erro("Informe de e ate (AAAA-MM-DD), com até 42 dias", StatusCodes.Status400BadRequest);
                case StatusDaAgendaDoCoach.NaoConectado:
                    CoachAgendaLog.Falhou(logger, "google_nao_conectado");
                    return Erro("Google Calendar não conectado", StatusCodes.Status503ServiceUnavailable);
                default:
                    CoachAgendaLog.Falhou(logger, "google_indisponivel");
                    return Erro("Agenda indisponível agora", StatusCodes.Status503ServiceUnavailable);
            }
        });
    }

    private static IResult Erro(string mensagem, int status) => Envelopes.Json(new Dictionary<string, object?> { ["erro"] = mensagem }, status);

    // campo que o nível não inclui NÃO aparece (nem nulo)
    private static Dictionary<string, object?> Corpo(AgendaDoCoach agenda) => new()
    {
        ["de"] = agenda.De,
        ["ate"] = agenda.Ate,
        ["geradoEm"] = agenda.GeradoEm,
        ["eventos"] = agenda.Eventos.Select(Evento).ToList(),
    };

    private static Dictionary<string, object?> Evento(EventoParaOCoach e)
    {
        var d = new Dictionary<string, object?>
        {
            ["inicio"] = e.Inicio,
            ["fim"] = e.Fim,
            ["diaInteiro"] = e.DiaInteiro,
            ["nivel"] = e.Nivel.ToString().ToLowerInvariant(),
        };
        if (e.Titulo is not null)
        {
            d["titulo"] = e.Titulo;
        }

        if (e.Descricao is not null)
        {
            d["descricao"] = e.Descricao;
        }

        if (e.Local is not null)
        {
            d["local"] = e.Local;
        }

        if (e.Calendario is not null)
        {
            d["calendario"] = e.Calendario;
        }

        return d;
    }
}

internal static partial class CoachAgendaLog
{
    // só contagens, o intervalo e o motivo: título, descrição, local e calendário nunca entram (RN-015-06)
    [LoggerMessage(EventId = 15001, Level = LogLevel.Information, Message = "Agenda do Coach atendida: {Count} eventos de {De} a {Ate}")]
    public static partial void Atendida(ILogger logger, int count, string de, string ate);

    [LoggerMessage(EventId = 15002, Level = LogLevel.Warning, Message = "Agenda do Coach indisponível: {Motivo}")]
    public static partial void Falhou(ILogger logger, string motivo);
}
