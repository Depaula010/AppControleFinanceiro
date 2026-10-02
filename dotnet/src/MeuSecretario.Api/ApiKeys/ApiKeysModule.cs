using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;
using MeuSecretario.Infrastructure.ApiKeys;
using MeuSecretario.Infrastructure.Configuration;

namespace MeuSecretario.Api.ApiKeys;

/// <summary>
/// Feature 008: <c>/api-keys/*</c> (envelope C), autenticada por <c>X-API-KEY</c>. As rotas só existem com
/// <c>API_KEYS_ROUTES_ENABLED=true</c> (desvio 052); os serviços e a porta <c>IApiKeyProvider</c> ficam registrados sempre
/// (a 010 não depende da flag).
/// </summary>
public sealed class ApiKeysModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddApiKeysInfrastructure();

        services.AddScoped<RegisterApiKey>();
        services.AddScoped<ListApiKeys>();
        services.AddScoped<UpdateApiKey>();
        services.AddScoped<DeactivateApiKey>();
        services.AddScoped<SetPreference>();
        services.AddScoped<ListPreferences>();
        services.AddScoped<DeletePreference>();
        services.AddScoped<GetUsage>();
        services.AddScoped<GetAccessLogs>();

        services.AddScoped<RegisterConsent>();
        services.AddScoped<ListConsents>();
        services.AddScoped<CheckConsent>();
        services.AddScoped<ExportUserData>();
        services.AddScoped<DeleteAccount>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<AppSettings>().ApiKeysRoutesEnabled)
        {
            return;
        }

        var group = app.MapGroup("/api-keys");

        // Health sem autenticação (api_keys.py:840-847).
        group.MapGet("/health", () => Envelopes.C.Success(new Dictionary<string, object?>
        {
            ["status"] = "ok",
            ["servico"] = ApiKeysMessages.ServiceName,
            ["versao"] = ApiKeysMessages.ServiceVersion,
        }));

        var secured = group.MapGroup(string.Empty).RequireApiKey(ApiKeyUnauthorized.EnvelopeC);
        KeysEndpoints.Map(secured);
        LgpdEndpoints.Map(secured);
    }

    // ------------------------------------------------------------------ utilidades compartilhadas

    /// <summary><c>&lt;int:id&gt;</c> do Flask: só dígitos; acima de <see cref="int.MaxValue"/> também é 404.</summary>
    internal static bool TryId(string text, out int id) =>
        int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out id);

    internal static async Task<IResult> NotFoundHtml(HttpContext http)
    {
        await FlaskNotFound.WriteAsync(http);
        return Results.Empty;
    }

    internal static IResult Incomplete(string message) =>
        Envelopes.C.Error(ApiKeysMessages.IncompleteTitle, message, StatusCodes.Status400BadRequest);

    internal static IResult InvalidProvider() =>
        Envelopes.C.Error(ApiKeysMessages.InvalidProviderTitle, ApiKeysMessages.InvalidProviderMessage, StatusCodes.Status400BadRequest);

    internal static IResult UserNotFound(int userId) =>
        Envelopes.C.Error(ApiKeysMessages.UserNotFoundTitle, ApiKeysMessages.UserNotFound(userId), StatusCodes.Status404NotFound);

    internal static IResult ServerError(string title) =>
        Envelopes.C.Error(title, ApiKeysMessages.Fixed500(title), StatusCodes.Status500InternalServerError);
}

/// <summary>Rotas 1 a 9 (chaves, preferências, uso e logs).</summary>
internal static class KeysEndpoints
{
    private const string Id = @"^\d+$";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/usuario/{usuarioId:regex(" + Id + ")}", RegisterKeyAsync);
        group.MapGet("/usuario/{usuarioId:regex(" + Id + ")}", ListKeysAsync);
        group.MapPut("/chave/{chaveId:regex(" + Id + ")}", UpdateKeyAsync);
        group.MapDelete("/chave/{chaveId:regex(" + Id + ")}", DeleteKeyAsync);
        group.MapPost("/preferencias/{usuarioId:regex(" + Id + ")}", SetPreferenceAsync);
        group.MapGet("/preferencias/{usuarioId:regex(" + Id + ")}", ListPreferencesAsync);
        group.MapDelete("/preferencias/{usuarioId:regex(" + Id + ")}/{provedor}", DeletePreferenceAsync);
        group.MapGet("/uso/{usuarioId:regex(" + Id + ")}", UsageAsync);
        group.MapGet("/logs/{usuarioId:regex(" + Id + ")}", LogsAsync);
    }

    private static string? DateText(DateTime? value) => PythonDateFormat.Iso(value);

    // ------------------------------------------------------------------ 1. POST /usuario/<id>

    private static async Task<IResult> RegisterKeyAsync(
        HttpContext http, string usuarioId, RegisterApiKey useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var body = await JsonBody.ReadObjectAsync(http.Request, cancellationToken);
            if (!body.TextOrAbsent("provedor") || !body.TextOrAbsent("chave_api"))
            {
                return ApiKeysModule.Incomplete(ApiKeysMessages.RegisterKeyMissing);
            }

            var result = await useCase.ExecuteAsync(new RegisterKeyInput(userId, body.GetText("provedor"), body.GetText("chave_api")), cancellationToken);
            return result switch
            {
                RegisterKeyResult.Success success => Envelopes.C.Success(
                    new Dictionary<string, object?>
                    {
                        ["mensagem"] = ApiKeysMessages.KeyRegistered,
                        ["id"] = success.Id,
                        ["provedor"] = success.Provider,
                        ["usuario_id"] = success.UserId,
                    },
                    StatusCodes.Status201Created),
                RegisterKeyResult.MissingFields => ApiKeysModule.Incomplete(ApiKeysMessages.RegisterKeyMissing),
                RegisterKeyResult.InvalidProvider => ApiKeysModule.InvalidProvider(),
                RegisterKeyResult.UserNotFound => ApiKeysModule.UserNotFound(userId),
                RegisterKeyResult.AlreadyExists => Envelopes.C.Error(
                    ApiKeysMessages.KeyDuplicateTitle, ApiKeysMessages.KeyDuplicateMessage, StatusCodes.Status409Conflict),
                _ => throw new InvalidOperationException("Resultado de cadastro de chave desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.RegisterKeyFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.RegisterKeyFailed);
        }
    }

    // ------------------------------------------------------------------ 2. GET /usuario/<id>

    private static async Task<IResult> ListKeysAsync(
        HttpContext http, string usuarioId, ListApiKeys useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var rows = await useCase.ExecuteAsync(userId, cancellationToken);
            return Envelopes.C.Success(new Dictionary<string, object?>
            {
                ["usuario_id"] = userId,
                ["total"] = rows.Count,
                ["chaves"] = rows.Select(r => new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["provedor"] = r.Provider,
                    ["ativo"] = r.Active,
                    ["ultimo_uso_em"] = DateText(r.LastUsedAt),
                    ["criado_em"] = DateText(r.CreatedAt),
                    ["atualizado_em"] = DateText(r.UpdatedAt),
                }).ToList(),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.ListKeysFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.ListKeysFailed);
        }
    }

    // ------------------------------------------------------------------ 3. PUT /chave/<id>

    private static async Task<IResult> UpdateKeyAsync(
        HttpContext http, string chaveId, UpdateApiKey useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(chaveId, out var keyId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var body = await JsonBody.ReadObjectAsync(http.Request, cancellationToken);
            if (!body.TextOrAbsent("chave_api") || !body.BoolOrAbsent("ativo"))
            {
                return ApiKeysModule.Incomplete(ApiKeysMessages.UpdateKeyMissing);
            }

            // usuario_id de tipo errado conta como ausente: cai no 400 "usuario_id é obrigatório" (desvios 045 e 048).
            var result = await useCase.ExecuteAsync(
                new UpdateKeyInput(keyId, body.GetInt("usuario_id"), body.GetText("chave_api"), body.GetBool("ativo")), cancellationToken);

            return result switch
            {
                UpdateKeyResult.Success success => Envelopes.C.Success(new Dictionary<string, object?>
                {
                    ["mensagem"] = ApiKeysMessages.KeyUpdated,
                    ["id"] = success.KeyId,
                    ["provedor"] = success.Provider,
                    ["usuario_id"] = success.OwnerUserId,
                }),
                UpdateKeyResult.NothingToUpdate => ApiKeysModule.Incomplete(ApiKeysMessages.UpdateKeyMissing),
                UpdateKeyResult.OwnerRequired => ApiKeysModule.Incomplete(ApiKeysMessages.UserRequired),
                UpdateKeyResult.NotFound => KeyNotFound(keyId),
                _ => throw new InvalidOperationException("Resultado de atualização de chave desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.UpdateKeyFailed(logger, keyId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.UpdateKeyFailed);
        }
    }

    private static IResult KeyNotFound(int keyId) =>
        Envelopes.C.Error(ApiKeysMessages.KeyNotFoundTitle, ApiKeysMessages.KeyNotFound(keyId), StatusCodes.Status404NotFound);

    // ------------------------------------------------------------------ 4. DELETE /chave/<id>?usuario_id=

    private static async Task<IResult> DeleteKeyAsync(
        HttpContext http, string chaveId, DeactivateApiKey useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(chaveId, out var keyId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            // Ausente ou vazio: 400. Valor não inteiro conta como "outro usuário" (-1 nunca é dono): 404.
            var raw = http.Request.Query["usuario_id"].FirstOrDefault();
            int? owner = string.IsNullOrEmpty(raw)
                ? null
                : int.TryParse(raw, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;

            return await useCase.ExecuteAsync(keyId, owner, cancellationToken) switch
            {
                DeactivateKeyResult.Success success => Envelopes.C.Success(new Dictionary<string, object?>
                {
                    ["mensagem"] = ApiKeysMessages.KeyDeactivated,
                    ["id"] = success.KeyId,
                    ["provedor"] = success.Provider,
                }),
                DeactivateKeyResult.OwnerRequired => ApiKeysModule.Incomplete(ApiKeysMessages.UserRequired),
                DeactivateKeyResult.NotFound => KeyNotFound(keyId),
                _ => throw new InvalidOperationException("Resultado de desativação de chave desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.DeleteKeyFailed(logger, keyId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.DeleteKeyFailed);
        }
    }

    // ------------------------------------------------------------------ 5. POST /preferencias/<id>

    private static async Task<IResult> SetPreferenceAsync(
        HttpContext http, string usuarioId, SetPreference useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var body = await JsonBody.ReadObjectAsync(http.Request, cancellationToken);
            if (!body.TextOrAbsent("provedor") || !body.BoolOrAbsent("usar_chave_propria"))
            {
                return ApiKeysModule.Incomplete(ApiKeysMessages.SetPreferenceMissing);
            }

            var result = await useCase.ExecuteAsync(
                new SetPreferenceInput(userId, body.GetText("provedor"), body.GetBool("usar_chave_propria")), cancellationToken);

            return result switch
            {
                SetPreferenceResult.Success success => Envelopes.C.Success(
                    new Dictionary<string, object?>
                    {
                        ["mensagem"] = ApiKeysMessages.PreferenceSaved,
                        ["id"] = success.Id,
                        ["usuario_id"] = userId,
                        ["provedor"] = success.Provider,
                        ["usar_chave_propria"] = success.UseOwnKey,
                        ["tipo"] = PreferenceKind.Label(success.UseOwnKey),
                    },
                    StatusCodes.Status201Created),
                SetPreferenceResult.MissingFields => ApiKeysModule.Incomplete(ApiKeysMessages.SetPreferenceMissing),
                SetPreferenceResult.InvalidProvider => ApiKeysModule.InvalidProvider(),
                SetPreferenceResult.UserNotFound => ApiKeysModule.UserNotFound(userId),
                _ => throw new InvalidOperationException("Resultado de preferência desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.SetPreferenceFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.SetPreferenceFailed);
        }
    }

    // ------------------------------------------------------------------ 6. GET /preferencias/<id>

    private static async Task<IResult> ListPreferencesAsync(
        HttpContext http, string usuarioId, ListPreferences useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var rows = await useCase.ExecuteAsync(userId, cancellationToken);
            return Envelopes.C.Success(new Dictionary<string, object?>
            {
                ["usuario_id"] = userId,
                ["total"] = rows.Count,
                ["preferencias"] = rows.Select(r => new Dictionary<string, object?>
                {
                    ["id"] = r.Id,
                    ["provedor"] = r.Provider,
                    ["usar_chave_propria"] = r.UseOwnKey,
                    ["tipo"] = PreferenceKind.Label(r.UseOwnKey),
                    ["atualizado_em"] = DateText(r.UpdatedAt),
                }).ToList(),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.ListPreferencesFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.ListPreferencesFailed);
        }
    }

    // ------------------------------------------------------------------ 7. DELETE /preferencias/<id>/<provedor>

    private static async Task<IResult> DeletePreferenceAsync(
        HttpContext http, string usuarioId, string provedor, DeletePreference useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            return await useCase.ExecuteAsync(userId, provedor, cancellationToken)
                ? Envelopes.C.Success(new Dictionary<string, object?>
                {
                    ["mensagem"] = ApiKeysMessages.PreferenceRemoved,
                    ["usuario_id"] = userId,
                    ["provedor"] = provedor,
                })
                : Envelopes.C.Error(
                    ApiKeysMessages.PreferenceNotFoundTitle, ApiKeysMessages.PreferenceNotFound(provedor), StatusCodes.Status404NotFound);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.DeletePreferenceFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.DeletePreferenceFailed);
        }
    }

    // ------------------------------------------------------------------ 8. GET /uso/<id>

    private static async Task<IResult> UsageAsync(
        HttpContext http, string usuarioId, GetUsage useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var result = await useCase.ExecuteAsync(
                userId, http.Request.Query["mes_ano"].FirstOrDefault(), http.Request.Query["provedor"].FirstOrDefault(), cancellationToken);

            return Envelopes.C.Success(new Dictionary<string, object?>
            {
                ["usuario_id"] = userId,
                ["mes_ano"] = result.Month,
                ["resumo"] = new Dictionary<string, object?>
                {
                    ["total_chamadas"] = result.Summary.TotalCalls,
                    ["chamadas_chave_propria"] = result.Summary.OwnKeyCalls,
                    ["chamadas_chave_sistema"] = result.Summary.SystemKeyCalls,
                },
                ["detalhes"] = result.Details.Select(r => new Dictionary<string, object?>
                {
                    ["provedor"] = r.Provider,
                    ["tipo_chave"] = r.KeyType,
                    ["total_chamadas"] = r.TotalCalls,
                    ["mes_ano"] = r.Month,
                    ["ultima_atualizacao"] = DateText(r.LastUpdate),
                }).ToList(),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.UsageFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.UsageFailed);
        }
    }

    // ------------------------------------------------------------------ 9. GET /logs/<id>

    private static async Task<IResult> LogsAsync(
        HttpContext http, string usuarioId, GetAccessLogs useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var query = http.Request.Query;
            var result = await useCase.ExecuteAsync(
                new AccessLogQuery(
                    userId,
                    query["provedor"].FirstOrDefault(),
                    query.ContainsKey("sucesso") ? query["sucesso"].FirstOrDefault() ?? string.Empty : null,
                    query.ContainsKey("limit") ? query["limit"].FirstOrDefault() ?? string.Empty : null),
                cancellationToken);

            if (result is not AccessLogsResult.Success success)
            {
                return ApiKeysModule.ServerError(ApiKeysMessages.LogsFailed);
            }

            return Envelopes.C.Success(new Dictionary<string, object?>
            {
                ["usuario_id"] = userId,
                ["total"] = success.Logs.Count,
                ["logs"] = success.Logs.Select(l => new Dictionary<string, object?>
                {
                    ["id"] = l.Id,
                    ["provedor"] = l.Provider,
                    ["tipo_chave"] = l.KeyType,
                    ["operacao"] = l.Operation,
                    ["sucesso"] = l.Success,
                    ["mensagem_erro"] = l.ErrorMessage,
                    ["criado_em"] = DateText(l.CreatedAt),
                }).ToList(),
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.LogsFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.LogsFailed);
        }
    }
}
