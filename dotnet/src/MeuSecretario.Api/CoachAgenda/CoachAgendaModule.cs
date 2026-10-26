using System.Globalization;
using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CoachAgenda;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.CoachAgenda;
using MeuSecretario.Infrastructure.CoachAgenda;
using MeuSecretario.Infrastructure.Security;

namespace MeuSecretario.Api.CoachAgenda;

/// <summary>
/// Descobre o dono da chave apresentada (spec 016). Primeiro a chave antiga do ambiente (<c>COACH_AGENDA_API_KEY</c> + <c>COACH_AGENDA_USER_ID</c>, transição
/// RN-016-06), em tempo constante; depois as chaves por usuário (hash no banco). Não é a <c>API_SECRET_KEY</c> (AC-015-02). Falha do banco = ninguém (nega).
/// </summary>
public sealed class CoachAgendaApiKey(IApiKeyValidator validator, int? usuarioDaChaveAntiga, IChavesDaAgenda chaves)
{
    public async Task<int?> DonoAsync(string? apresentada, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(apresentada))
        {
            return null;
        }

        if (usuarioDaChaveAntiga is { } antigo && validator.IsValid(apresentada))
        {
            return antigo;
        }

        try
        {
            return await chaves.ResolverAsync(apresentada, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>
/// Feature 015 (parte B) e 016: <c>GET /internal/coach/agenda</c>, somente leitura, para o Coach (projeto amazfit-dados). A rota está <b>sempre registrada</b>:
/// cada pessoa tem a própria chave (hash no banco) e a rota lê a agenda do dono da chave; sem nenhuma chave ativa, responde 401. As rotas
/// <c>/coach/agenda/chave</c> (gerar, consultar, revogar) usam o login normal do app.
/// </summary>
public sealed class CoachAgendaModule : IFeatureModule
{
    public const string Caminho = "/internal/coach/agenda";
    public const string CaminhoDaChave = "/coach/agenda/chave";
    public const string ChaveDaRota = "COACH_AGENDA_API_KEY";
    public const string UsuarioDaAgenda = "COACH_AGENDA_USER_ID";

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ICoachAgendaConfig, DbCoachAgendaConfig>();
        services.AddSingleton<IChavesDaAgenda, DbChavesDaAgenda>();
        services.AddScoped<IWhatsAppStep, CoachKeyStep>();                              // "chave do coach" no WhatsApp (Q-016-1)
        services.AddSingleton(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            int? antigo = int.TryParse(config[UsuarioDaAgenda], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;
            return new CoachAgendaApiKey(new ApiSecretKeyValidator(config[ChaveDaRota] ?? string.Empty), antigo, sp.GetRequiredService<IChavesDaAgenda>());
        });
        services.AddScoped<ConsultarAgendaDoCoach>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost(CaminhoDaChave, GerarChaveAsync).RequireUser();
        app.MapGet(CaminhoDaChave, ConsultarChaveAsync).RequireUser();
        app.MapDelete(CaminhoDaChave, RevogarChaveAsync).RequireUser();

        app.MapGet(Caminho, async (HttpContext http, CoachAgendaApiKey key, ConsultarAgendaDoCoach useCase, ILogger<CoachAgendaModule> logger, CancellationToken ct) =>
        {
            // a chave vem antes de tudo: sem ela nem a query é lida
            if (await key.DonoAsync(http.Request.Headers["X-API-KEY"].FirstOrDefault(), ct).ConfigureAwait(false) is not { } userId)
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

    // a chave sai UMA vez, aqui; depois só o final dela (AC-016-02)
    private static async Task<IResult> GerarChaveAsync(ICurrentUser user, IChavesDaAgenda chaves, CancellationToken ct)
    {
        var nova = await chaves.GerarAsync(user.UserId, ct).ConfigureAwait(false);
        return Envelopes.Json(new Dictionary<string, object?> { ["chave"] = nova.Chave, ["final"] = nova.Final, ["criadaEm"] = nova.CriadaEm.ToString("O", CultureInfo.InvariantCulture) }, StatusCodes.Status201Created);
    }

    private static async Task<IResult> ConsultarChaveAsync(ICurrentUser user, IChavesDaAgenda chaves, CancellationToken ct)
    {
        var atual = await chaves.ConsultarAsync(user.UserId, ct).ConfigureAwait(false);
        return Envelopes.Json(atual is null
            ? new Dictionary<string, object?> { ["existe"] = false }
            : new Dictionary<string, object?>
            {
                ["existe"] = true,
                ["final"] = atual.Final,
                ["criadaEm"] = atual.CriadaEm.ToString("O", CultureInfo.InvariantCulture),
                ["ultimoUsoEm"] = atual.UltimoUsoEm?.ToString("O", CultureInfo.InvariantCulture),
            }, StatusCodes.Status200OK);
    }

    private static async Task<IResult> RevogarChaveAsync(ICurrentUser user, IChavesDaAgenda chaves, CancellationToken ct)
    {
        var revogou = await chaves.RevogarAsync(user.UserId, ct).ConfigureAwait(false);
        return Envelopes.Json(new Dictionary<string, object?> { ["revogada"] = revogou }, StatusCodes.Status200OK);
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
