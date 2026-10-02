using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Domain.ApiKeys;

namespace MeuSecretario.Api.ApiKeys;

/// <summary>Rotas 10 a 15 (LGPD: consentimentos, exportação e deleção da conta).</summary>
internal static class LgpdEndpoints
{
    private const string Id = @"^\d+$";

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/lgpd/consentimento", RegisterConsentAsync);
        group.MapGet("/lgpd/consentimentos/{usuarioId:regex(" + Id + ")}", ListConsentsAsync);
        group.MapGet("/lgpd/consentimentos-iniciais/{usuarioId:regex(" + Id + ")}", InitialConsentsAsync);
        group.MapGet("/lgpd/exportar/{usuarioId:regex(" + Id + ")}", ExportAsync);
        group.MapDelete("/lgpd/deletar-conta/{usuarioId:regex(" + Id + ")}", DeleteAccountAsync);
        group.MapGet("/lgpd/verificar-consentimento/{usuarioId:regex(" + Id + ")}/{tipoConsentimento}", CheckConsentAsync);
    }

    private static string? DateText(DateTime? value) => PythonDateFormat.Iso(value);

    private static Dictionary<string, object?> ConsentJson(ConsentRow row) => new()
    {
        ["id"] = row.Id,
        ["tipo_consentimento"] = row.Type,
        ["descricao"] = ConsentCatalog.Description(row.Type),
        ["consentimento_dado"] = row.Given,
        ["versao_termos"] = row.Version,
        ["data_consentimento"] = DateText(row.ConsentedAt),
    };

    // ------------------------------------------------------------------ 10. POST /lgpd/consentimento

    private static async Task<IResult> RegisterConsentAsync(
        HttpContext http, RegisterConsent useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(http.Request, cancellationToken);
            if (!body.IntOrAbsent("usuario_id") || !body.TextOrAbsent("tipo_consentimento")
                || !body.BoolOrAbsent("consentimento_dado") || !body.TextOrAbsent("versao_termos"))
            {
                return ApiKeysModule.Incomplete(ApiKeysMessages.RegisterConsentMissing);
            }

            var result = await useCase.ExecuteAsync(
                new RegisterConsentInput(
                    body.GetInt("usuario_id"), body.GetText("tipo_consentimento"), body.GetBool("consentimento_dado"), body.GetText("versao_termos")),
                cancellationToken);

            return result switch
            {
                RegisterConsentResult.Success success => Envelopes.C.Success(
                    new Dictionary<string, object?>
                    {
                        ["status"] = "sucesso",
                        ["consentimento_id"] = success.Id,
                        ["mensagem"] = ApiKeysMessages.ConsentRegistered,
                        ["tipo"] = success.Type,
                        ["consentimento_dado"] = success.Given,
                    },
                    StatusCodes.Status201Created),
                RegisterConsentResult.MissingFields => ApiKeysModule.Incomplete(ApiKeysMessages.RegisterConsentMissing),
                RegisterConsentResult.InvalidType invalid => Envelopes.C.Error(
                    ApiKeysMessages.ValidationTitle, invalid.Message, StatusCodes.Status400BadRequest),
                RegisterConsentResult.UserNotFound missing => ApiKeysModule.UserNotFound(missing.UserId),
                _ => throw new InvalidOperationException("Resultado de consentimento desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.RegisterConsentFailed(logger, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.RegisterConsentFailed);
        }
    }

    // ------------------------------------------------------------------ 11. GET /lgpd/consentimentos/<id>

    private static async Task<IResult> ListConsentsAsync(
        HttpContext http, string usuarioId, ListConsents useCase, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        // O caso de uso devolve lista vazia em erro de banco (como o serviço do Flask) e já registra a falha.
        var rows = await useCase.ExecuteAsync(userId, cancellationToken);
        return Envelopes.C.Success(new Dictionary<string, object?>
        {
            ["usuario_id"] = userId,
            ["total"] = rows.Count,
            ["consentimentos"] = rows.Select(ConsentJson).ToList(),
        });
    }

    // ------------------------------------------------------------------ 12. GET /lgpd/consentimentos-iniciais/<id>

    private static async Task<IResult> InitialConsentsAsync(HttpContext http, string usuarioId)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        return Envelopes.C.Success(new Dictionary<string, object?>
        {
            ["usuario_id"] = userId,
            ["consentimentos_necessarios"] = ConsentCatalog.All.Select(c => new Dictionary<string, object?>
            {
                ["tipo"] = c.Type,
                ["descricao"] = c.Description,
                ["obrigatorio"] = c.Required,
                ["versao_termos"] = ConsentCatalog.CurrentVersion,
            }).ToList(),
            ["mensagem"] = ApiKeysMessages.InitialConsentsMessage,
        });
    }

    // ------------------------------------------------------------------ 13. GET /lgpd/exportar/<id>

    private static async Task<IResult> ExportAsync(
        HttpContext http, string usuarioId, ExportUserData useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        // A resposta leva chaves em claro: nunca pode ser guardada por proxy ou navegador (desvio 006).
        http.Response.Headers.CacheControl = "no-store";

        try
        {
            var data = await useCase.ExecuteAsync(userId, cancellationToken);
            ApiKeysLog.DataExported(logger, userId);

            return Envelopes.C.Success(new Dictionary<string, object?>
            {
                ["usuario_id"] = data.UserId,
                ["data_exportacao"] = PythonDateFormat.IsoLocal(data.ExportedAt),
                ["dados"] = new Dictionary<string, object?>
                {
                    ["chaves_api"] = data.Keys.Select(k => new Dictionary<string, object?>
                    {
                        ["id"] = k.Id,
                        ["provedor"] = k.Provider,
                        ["chave_api"] = k.Key,
                        ["ativo"] = k.Active,
                        ["ultimo_uso_em"] = DateText(k.LastUsedAt),
                        ["criado_em"] = DateText(k.CreatedAt),
                        ["atualizado_em"] = DateText(k.UpdatedAt),
                    }).ToList(),
                    ["preferencias"] = data.Preferences.Select(p => new Dictionary<string, object?>
                    {
                        ["id"] = p.Id,
                        ["provedor"] = p.Provider,
                        ["usar_chave_propria"] = p.UseOwnKey,
                        ["atualizado_em"] = DateText(p.UpdatedAt),
                    }).ToList(),
                    ["uso_mensal"] = data.Usage.Select(u => new Dictionary<string, object?>
                    {
                        ["provedor"] = u.Provider,
                        ["tipo_chave"] = u.KeyType,
                        ["quantidade_chamadas"] = u.Calls,
                        ["mes_ano"] = u.Month,
                    }).ToList(),
                    ["logs_acesso"] = data.Logs.Select(l => new Dictionary<string, object?>
                    {
                        ["provedor"] = l.Provider,
                        ["tipo_chave"] = l.KeyType,
                        ["sucesso"] = l.Success,
                        ["mensagem_erro"] = l.ErrorMessage,
                        ["data_acesso"] = DateText(l.AccessedAt),
                    }).ToList(),
                    ["consentimentos"] = data.Consents.Select(ConsentJson).ToList(),
                    ["assinatura"] = data.Subscription is { } s ? SubscriptionJson(s) : null,
                },
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nunca a exceção inteira: ela poderia carregar o token ou a chave.
            ApiKeysLog.ExportFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.ExportFailed);
        }
    }

    private static Dictionary<string, object?> SubscriptionJson(ExportedSubscription s) => new()
    {
        ["id"] = s.Id,
        ["plano"] = s.PlanName,
        // Python: float(x) if x else 0 (zero sai como inteiro 0; 29.90 como 29.9).
        ["preco_mensal"] = s.MonthlyPrice == 0 ? 0 : (object)(double)s.MonthlyPrice,
        ["limites"] = new Dictionary<string, object?>
        {
            ["gemini"] = s.GeminiLimit,
            ["weather"] = s.WeatherLimit,
            ["openroute"] = s.OpenRouteLimit,
        },
        ["data_inicio"] = DateText(s.StartDate),
        ["data_fim"] = DateText(s.EndDate),
        ["ativo"] = s.Active,
    };

    // ------------------------------------------------------------------ 14. DELETE /lgpd/deletar-conta/<id>

    private static async Task<IResult> DeleteAccountAsync(
        HttpContext http, string usuarioId, DeleteAccount useCase, ILogger<ApiKeysModule> logger, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        try
        {
            var body = await JsonBody.ReadObjectAsync(http.Request, cancellationToken);
            var result = await useCase.ExecuteAsync(new DeleteAccountInput(userId, body.GetText("confirmacao")), cancellationToken);

            switch (result)
            {
                case DeleteAccountResult.NotConfirmed:
                    return Envelopes.C.Error(
                        ApiKeysMessages.DeleteConfirmationTitle, ApiKeysMessages.DeleteConfirmationMessage, StatusCodes.Status400BadRequest);
                case DeleteAccountResult.UserNotFound:
                    return ApiKeysModule.UserNotFound(userId);
                case DeleteAccountResult.Success success:
                    ApiKeysLog.AccountDeleted(logger, userId);
                    var c = success.Counts;
                    return Envelopes.C.Success(new Dictionary<string, object?>
                    {
                        ["status"] = "sucesso",
                        ["usuario_id"] = userId,
                        ["registros_deletados"] = new Dictionary<string, object?>
                        {
                            ["chaves_api"] = c.ApiKeys,
                            ["preferencias"] = c.Preferences,
                            ["logs"] = c.Logs,
                            ["uso"] = c.Usage,
                            ["consentimentos"] = c.Consents,
                            ["assinaturas"] = c.Subscriptions,
                            ["transacoes"] = c.Transactions,
                            ["agendamentos"] = c.Schedules,
                            ["contas"] = c.Accounts,
                            ["potes"] = c.Pots,
                            ["subcategorias"] = c.Subcategories,
                            ["macrocategorias"] = c.Macrocategories,
                            ["usuarios"] = c.Users,
                        },
                        ["total_registros"] = c.Total,
                        ["mensagem"] = ApiKeysMessages.AccountDeleted(userId),
                    });
                default:
                    throw new InvalidOperationException("Resultado de deleção de conta desconhecido.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ApiKeysLog.DeleteAccountFailed(logger, userId, SafeError.Describe(ex));
            return ApiKeysModule.ServerError(ApiKeysMessages.DeleteAccountFailed);
        }
    }

    // ------------------------------------------------------------------ 15. GET /lgpd/verificar-consentimento/<id>/<tipo>

    private static async Task<IResult> CheckConsentAsync(
        HttpContext http, string usuarioId, string tipoConsentimento, CheckConsent useCase, CancellationToken cancellationToken)
    {
        if (!ApiKeysModule.TryId(usuarioId, out var userId))
        {
            return await ApiKeysModule.NotFoundHtml(http);
        }

        var active = await useCase.ExecuteAsync(userId, tipoConsentimento, cancellationToken);
        return Envelopes.C.Success(new Dictionary<string, object?>
        {
            ["usuario_id"] = userId,
            ["tipo_consentimento"] = tipoConsentimento,
            ["consentimento_ativo"] = active,
        });
    }
}
