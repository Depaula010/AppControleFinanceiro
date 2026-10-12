using System.Globalization;
using MeuSecretario.Api.Auth;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Api.Settings;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.CalendarAlerts;
using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Infrastructure.CalendarAlerts;

namespace MeuSecretario.Api.CalendarAlerts;

/// <summary>Textos literais da 014 (spec §3). Os 500 e o 403 são fixos (desvios 003 e 008).</summary>
public static class CalendarAlertsMessages
{
    public const string NoParameters = "Nenhum parâmetro fornecido. Use 'ativo' e/ou 'minutos_antes'";
    public const string NotConnected = "Você precisa conectar o Google Calendar primeiro para ativar alertas de tarefas";
    public const string InvalidMinutes = "minutos_antes deve ser um número entre 1 e 60";
    public const string InvalidActive = "ativo deve ser true ou false";
    public const string UpdateFailed = "Erro ao atualizar configuração";
    public const string Forbidden = "Acesso negado";
    public const string GetFailed = "Erro ao obter configuração de alertas";
    public const string ConfigFailed = "Erro ao atualizar configuração de alertas";
    public const string ActivateFailed = "Erro ao ativar alertas de tarefas";
    public const string DeactivateFailed = "Erro ao desativar alertas de tarefas";

    public static string For(AlertRejection reason) => reason switch
    {
        AlertRejection.NoParameters => NoParameters,
        AlertRejection.NotConnected => NotConnected,
        AlertRejection.InvalidMinutes => InvalidMinutes,
        AlertRejection.InvalidActive => InvalidActive,
        AlertRejection.UpdateFailed => UpdateFailed,
        _ => throw new InvalidOperationException("Motivo de rejeição desconhecido."),
    };
}

/// <summary>Respostas do envelope B com <c>config</c> na raiz, na ordem da spec.</summary>
public static class CalendarAlertsResponses
{
    public static Dictionary<string, object?> Config(CalendarAlertConfig config) => new()
    {
        ["alertas_tarefas_ativo"] = config.Active,
        ["minutos_antes"] = config.MinutesBefore,
    };

    /// <summary>GET: <c>{"status":"sucesso","config":{...}}</c>, sem <c>mensagem</c>.</summary>
    public static IResult ConfigOnly(CalendarAlertConfig config) =>
        Envelopes.Json(new Dictionary<string, object?> { ["status"] = "sucesso", ["config"] = Config(config) }, StatusCodes.Status200OK);

    public static IResult From(AlertConfigResult result) => result switch
    {
        AlertConfigResult.Ok ok => Envelopes.B.Success(
            ok.Message, new Dictionary<string, object?> { ["config"] = Config(ok.Config) }),
        AlertConfigResult.Rejected rejected => Envelopes.B.Error(
            CalendarAlertsMessages.For(rejected.Reason), StatusCodes.Status400BadRequest),
        _ => throw new InvalidOperationException("Resultado de alertas desconhecido."),
    };
}

internal static partial class CalendarAlertsLog
{
    [LoggerMessage(EventId = 14010, Level = LogLevel.Error, Message = "Erro em alertas de calendário ({Operation}, usuário {UserId}): {Error}")]
    public static partial void Failed(ILogger logger, string operation, int userId, string error);

    [LoggerMessage(EventId = 14011, Level = LogLevel.Warning, Message = "Acesso negado a alertas de calendário: usuário {UserId} tentou acessar outro usuário")]
    public static partial void Forbidden(ILogger logger, int userId);
}

/// <summary>
/// Feature 014: <c>/calendar-alerts/*</c> (4 rotas, envelope B). Desvio 003: JWT e id do caminho igual ao do token
/// (403 se diferir). Falha inesperada: 500 com texto fixo da rota (desvio 008).
/// </summary>
public sealed class CalendarAlertsModule : IFeatureModule
{
    private const string Id = @"^\d+$";

    public void AddServices(IServiceCollection services)
    {
        services.AddCalendarAlertsInfrastructure();

        services.AddScoped<GetCalendarAlertConfig>();
        services.AddScoped<UpdateCalendarAlertConfig>();
        services.AddScoped<ActivateCalendarAlerts>();
        services.AddScoped<DeactivateCalendarAlerts>();
        services.AddScoped<CalendarAlertService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/calendar-alerts");

        group.MapGet("/config/{usuarioId:regex(" + Id + ")}", GetConfigAsync).RequireUser();
        group.MapPost("/config/{usuarioId:regex(" + Id + ")}", UpdateConfigAsync).RequireUser();
        group.MapPost("/ativar/{usuarioId:regex(" + Id + ")}", ActivateAsync).RequireUser();
        group.MapPost("/desativar/{usuarioId:regex(" + Id + ")}", DeactivateAsync).RequireUser();
    }

    private static Task<IResult> GetConfigAsync(
        string usuarioId, HttpContext http, ICurrentUser user, GetCalendarAlertConfig useCase, ILogger<CalendarAlertsModule> logger, CancellationToken ct) =>
        GuardedAsync(usuarioId, http, user, logger, "obter configuração", CalendarAlertsMessages.GetFailed, async () =>
            CalendarAlertsResponses.ConfigOnly(await useCase.ExecuteAsync(user.UserId, ct)));

    private static Task<IResult> UpdateConfigAsync(
        string usuarioId, HttpContext http, ICurrentUser user, UpdateCalendarAlertConfig useCase, ILogger<CalendarAlertsModule> logger, CancellationToken ct) =>
        GuardedAsync(usuarioId, http, user, logger, "atualizar configuração", CalendarAlertsMessages.ConfigFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(http.Request, ct);
            return CalendarAlertsResponses.From(await useCase.ExecuteAsync(user.UserId, body, ct));
        });

    private static Task<IResult> ActivateAsync(
        string usuarioId, HttpContext http, ICurrentUser user, ActivateCalendarAlerts useCase, ILogger<CalendarAlertsModule> logger, CancellationToken ct) =>
        GuardedAsync(usuarioId, http, user, logger, "ativar", CalendarAlertsMessages.ActivateFailed, async () =>
        {
            var body = await SettingsBodyReader.ReadAsync(http.Request, ct);
            return CalendarAlertsResponses.From(await useCase.ExecuteAsync(user.UserId, body, ct));
        });

    private static Task<IResult> DeactivateAsync(
        string usuarioId, HttpContext http, ICurrentUser user, DeactivateCalendarAlerts useCase, ILogger<CalendarAlertsModule> logger, CancellationToken ct) =>
        GuardedAsync(usuarioId, http, user, logger, "desativar", CalendarAlertsMessages.DeactivateFailed, async () =>
            CalendarAlertsResponses.From(await useCase.ExecuteAsync(user.UserId, ct)));

    /// <summary>
    /// <c>&lt;int:usuario_id&gt;</c> acima de <see cref="int.MaxValue"/> é 404; id diferente do token é 403 (desvio 003);
    /// o resto roda dentro do <c>try</c> que troca qualquer falha pelo texto fixo da rota (desvio 008).
    /// </summary>
    private static async Task<IResult> GuardedAsync(
        string usuarioId,
        HttpContext http,
        ICurrentUser user,
        ILogger<CalendarAlertsModule> logger,
        string operation,
        string failureMessage,
        Func<Task<IResult>> action)
    {
        if (!int.TryParse(usuarioId, NumberStyles.None, CultureInfo.InvariantCulture, out var pathId))
        {
            await FlaskNotFound.WriteAsync(http);
            return Results.Empty;
        }

        if (pathId != user.UserId)
        {
            CalendarAlertsLog.Forbidden(logger, user.UserId);
            return Envelopes.B.Error(CalendarAlertsMessages.Forbidden, StatusCodes.Status403Forbidden);
        }

        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CalendarAlertsLog.Failed(logger, operation, user.UserId, SafeError.Describe(ex));
            return Envelopes.B.Error(failureMessage, StatusCodes.Status500InternalServerError);
        }
    }
}
