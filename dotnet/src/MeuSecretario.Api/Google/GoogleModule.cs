using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;

namespace MeuSecretario.Api.Google;

/// <summary>Logs das rotas de OAuth (EventIds 10430 a 10449): só o tipo da exceção e ids; nunca token, <c>code</c>, <c>state</c> nem segredo.</summary>
internal static partial class GoogleRouteLog
{
    [LoggerMessage(EventId = 10430, Level = LogLevel.Error, Message = "Erro ao conectar Google Calendar (usuário {UserId}): {Error}")]
    public static partial void ConnectFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10431, Level = LogLevel.Error, Message = "Erro no callback do OAuth do Google: {Error}")]
    public static partial void CallbackFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 10432, Level = LogLevel.Error, Message = "Erro ao desconectar Google Calendar (usuário {UserId}): {Error}")]
    public static partial void DisconnectFailed(ILogger logger, int userId, string error);

    [LoggerMessage(EventId = 10433, Level = LogLevel.Warning, Message = "Link de conexão do Google rejeitado (usuário {UserId}): sem token válido")]
    public static partial void ConnectLinkRejected(ILogger logger, int userId);

    [LoggerMessage(EventId = 10434, Level = LogLevel.Information, Message = "Autorização do Google recusada ou state inválido no callback")]
    public static partial void CallbackRejected(ILogger logger);
}

/// <summary>
/// Feature 010d: rotas do OAuth do Google (spec 010 §7.1 a §7.6). Sem JWT; cada ramo devolve o seu formato (HTML,
/// texto ou JSON), por isso não usam o envelope A. <c>disconnect-calendar</c> exige <c>x-api-key</c> (desvio 003).
/// </summary>
public sealed class GoogleModule : IFeatureModule
{
    private const string Id = @"^\d+$";

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<ConnectCalendar>();
        // Fábrica: o IBotClient é da 010b e a validação de dependências do host não deve exigi-lo antes de existir.
        services.AddScoped(sp => new HandleOAuthCallback(
            sp.GetRequiredService<GoogleAccountService>(),
            sp.GetRequiredService<IGoogleUserLookup>(),
            sp.GetRequiredService<IBotClient>()));
        services.AddScoped<DisconnectCalendar>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapGet("/connect-calendar/{usuarioId:regex(" + Id + ")}", ConnectAsync);
        app.MapGet("/oauth2callback", CallbackAsync);
        app.MapPost("/disconnect-calendar/{usuarioId:regex(" + Id + ")}", DisconnectAsync)
            .RequireApiKey(GoogleResponses.DisconnectUnauthorized);
    }

    /// <summary><c>&lt;int:usuario_id&gt;</c> do Flask: só dígitos; acima de <see cref="int.MaxValue"/> também é 404.</summary>
    private static bool TryId(string text, out int id) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);

    private static async Task<IResult> ConnectAsync(
        string usuarioId,
        HttpContext http,
        ConnectCalendar useCase,
        ILogger<GoogleModule> logger)
    {
        if (!TryId(usuarioId, out var userId))
        {
            await FlaskNotFound.WriteAsync(http);
            return Results.Empty;
        }

        // Valor repetido: vale o primeiro (request.args.get do Flask). `force` só vale com o texto exato "true".
        var token = http.Request.Query["t"].FirstOrDefault();
        var force = http.Request.Query["force"].FirstOrDefault() == "true";

        var result = await useCase.ExecuteAsync(userId, token, force, http.RequestAborted);
        switch (result)
        {
            case ConnectResult.Forbidden:
                GoogleRouteLog.ConnectLinkRejected(logger, userId);
                return GoogleResponses.Html(GooglePageRenderer.Forbidden(), StatusCodes.Status403Forbidden);
            case ConnectResult.DatabaseUnavailable:
                return GoogleResponses.DatabaseUnavailable();
            case ConnectResult.UserNotFound:
                return GoogleResponses.Html(GoogleMessages.UserNotFound, StatusCodes.Status404NotFound);
            case ConnectResult.AlreadyConnected:
                return GoogleResponses.Html(GooglePages.AlreadyConnected, StatusCodes.Status200OK);
            case ConnectResult.Redirect redirect:
                return Results.Redirect(redirect.Url);
            case ConnectResult.Failed failed:
                GoogleRouteLog.ConnectFailed(logger, userId, SafeError.Describe(failed.Error));
                return GoogleResponses.Html(GoogleMessages.ConnectFailed, StatusCodes.Status500InternalServerError);
            default:
                throw new InvalidOperationException("Resultado de conexão desconhecido.");
        }
    }

    private static async Task<IResult> CallbackAsync(HttpContext http, HandleOAuthCallback useCase, ILogger<GoogleModule> logger)
    {
        var query = http.Request.Query;
        var result = await useCase.ExecuteAsync(
            query["code"].FirstOrDefault(),
            query["state"].FirstOrDefault(),
            query["error"].FirstOrDefault(),
            http.RequestAborted);

        switch (result)
        {
            case CallbackResult.Denied:
                return GoogleResponses.Html(GooglePages.Denied, StatusCodes.Status200OK);
            case CallbackResult.MissingParams:
                return GoogleResponses.Html(GoogleMessages.MissingCodeOrState, StatusCodes.Status400BadRequest);
            case CallbackResult.Connected connected:
                return GoogleResponses.Html(GooglePageRenderer.Connected(connected.UserName), StatusCodes.Status200OK);
            case CallbackResult.InvalidState:
                GoogleRouteLog.CallbackRejected(logger);
                return GoogleResponses.Html(
                    GooglePageRenderer.Error(GoogleMessages.InvalidStateText), StatusCodes.Status500InternalServerError);
            case CallbackResult.Failed failed:
                GoogleRouteLog.CallbackFailed(logger, SafeError.Describe(failed.Error));
                return GoogleResponses.Html(
                    GooglePageRenderer.Error(GoogleMessages.InternalErrorText), StatusCodes.Status500InternalServerError);
            default:
                throw new InvalidOperationException("Resultado de callback desconhecido.");
        }
    }

    private static async Task<IResult> DisconnectAsync(
        string usuarioId,
        HttpContext http,
        DisconnectCalendar useCase,
        ILogger<GoogleModule> logger)
    {
        if (!TryId(usuarioId, out var userId))
        {
            await FlaskNotFound.WriteAsync(http);
            return Results.Empty;
        }

        var result = await useCase.ExecuteAsync(userId, http.RequestAborted);
        if (result is DisconnectResult.Failed failed)
        {
            GoogleRouteLog.DisconnectFailed(logger, userId, SafeError.Describe(failed.Error));
            return Envelopes.B.Error(GoogleMessages.DisconnectFailed, StatusCodes.Status500InternalServerError);
        }

        return Envelopes.B.Success(GoogleMessages.Disconnected);
    }
}
