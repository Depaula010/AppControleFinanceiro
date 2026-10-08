using MeuSecretario.Api.Accounts;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Api.Security;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Domain.Security;
using MeuSecretario.Infrastructure.Admin;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.Api.Admin;

/// <summary>
/// Feature 009a: as 9 rotas <c>/admin/*</c> com <c>x-api-key</c> e sem dependência futura (segurança, cache do Gemini,
/// sessão do bot, alertas financeiros). Todo o grupo exige a chave (401 envelope B, corpo nem lido); o filtro de id vem
/// antes dela (404 HTML antes do 401). As sub-entregas 009b e 009c acrescentam suas rotas no mesmo grupo.
/// </summary>
public sealed class AdminModule : IFeatureModule
{
    private const string NotificationConfigPath = "/admin/get-notification-config/{usuarioId}";

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IBotSessionRepository, BotSessionRepository>();
        services.AddScoped<IAdminNotificationConfigRepository, AdminNotificationConfigRepository>();

        services.AddScoped<UnblockIp>();
        services.AddScoped<GetSecurityStats>();
        services.AddScoped<BlacklistIp>();
        services.AddScoped<RemoveBlacklistedIp>();
        services.AddScoped<ClearLlmCache>();
        services.AddScoped<GetLlmCacheStats>();
        services.AddScoped<ClearBotSession>();
        services.AddScoped<GetNotificationConfig>();
        services.AddScoped<SetFinancialAlerts>();
        services.AddScoped<GetOAuthConfigCheck>();
        services.AddScoped<RunCalendarDebug>();

        // 009c: leituras de teste, casos de uso e as portas dos jobs. As portas ficam com TryAdd: a 013 (JobsModule) registra
        // as reais com AddScoped e prevalecem; sem elas as rotas respondem 503 (AC-009-39).
        services.AddScoped<IAdminTestDataRepository, AdminTestDataRepository>();
        services.AddScoped<SendTestNotification>();
        services.AddScoped<SendTestMonthlyReport>();
        services.AddScoped<SendTestDailyBriefing>();
        services.TryAddScoped<IScheduleMotor, UnavailableScheduleMotor>();
        services.TryAddScoped<IBillsNotificationJob, UnavailableBillsNotificationJob>();
        services.TryAddScoped<IDailyBriefingJob, UnavailableDailyBriefingJob>();
        services.TryAddScoped<IMonthlyReportJob, UnavailableMonthlyReportJob>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin");
        admin.AddEndpointFilter<AdminRouteIdFilter>(); // antes da chave: 404 HTML vence o 401
        admin.RequireApiKey(AdminUnauthorized.EnvelopeB);

        MapSecurityRoutes(admin);
        MapCacheRoutes(admin);
        MapBotAndNotificationRoutes(admin);
        SetupRoutes.Map(admin);
        admin.MapGet("/oauth-config-check", OAuthConfigCheckEndpoint);
        admin.MapGet("/debug-calendar", CalendarDebugAsync);
        DependentRoutes.Map(admin);

        // Outros métodos na rota com id: numérico => 405 (a rota existe para GET); senão 404 HTML, antes da autenticação.
        app.Map(NotificationConfigPath, OtherMethods);
        app.Map(DependentRoutes.MonthlyReportPath, DependentRoutes.OtherMethods);
    }

    private static void MapSecurityRoutes(RouteGroupBuilder admin)
    {
        admin.MapPost("/security-unblock-ip", UnblockAsync);
        admin.MapGet("/security-stats", StatsAsync);
        admin.MapPost("/security-blacklist-add", BlacklistAddAsync);
        admin.MapPost("/security-blacklist-remove", BlacklistRemoveAsync);
    }

    private static void MapCacheRoutes(RouteGroupBuilder admin)
    {
        admin.MapPost("/gemini-cache-clear", CacheClearAsync);
        admin.MapGet("/gemini-cache-stats", CacheStats);
    }

    private static void MapBotAndNotificationRoutes(RouteGroupBuilder admin)
    {
        admin.MapPost("/clear-bot-session", ClearBotSessionAsync);
        admin.MapGet("/get-notification-config/{usuarioId}", GetNotificationConfigAsync);
        admin.MapPost("/config-alertas-financeiros", SetFinancialAlertsAsync);
    }

    private static IResult Error(string message, int status) => Envelopes.B.Error(message, status);

    private static IResult BadRequest(string message) => Error(message, StatusCodes.Status400BadRequest);

    private static IResult ServerError(string message) => Error(message, StatusCodes.Status500InternalServerError);

    private static IResult IpProblem(IpInputProblem problem, string missingMessage) =>
        BadRequest(problem == IpInputProblem.Missing ? missingMessage : AdminMessages.IpInvalid);

    private static IResult OtherMethods(string usuarioId) =>
        NumericIdFilter.IsNumeric(usuarioId)
            ? Results.StatusCode(StatusCodes.Status405MethodNotAllowed)
            : new FlaskNotFoundResult();

    // ---- 1 a 4: segurança -------------------------------------------------------------------------------------------

    private static async Task<IResult> UnblockAsync(
        HttpRequest request, UnblockIp useCase, ILoggerFactory loggers, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            switch (await useCase.ExecuteAsync(AdminBody.Field(body, "ip"), cancellationToken))
            {
                case UnblockIpResult.BadInput bad:
                    return IpProblem(bad.Problem, AdminMessages.IpRequired);
                case UnblockIpResult.Done done:
                    var security = loggers.CreateLogger(SecurityLog.Category);
                    if (done.TempBlockRemoved)
                    {
                        SecurityLog.Unblocked(security, done.Ip);
                    }

                    if (done.BlacklistRemoved)
                    {
                        SecurityLog.BlacklistRemoved(security, done.Ip);
                    }

                    return Envelopes.B.Success(AdminMessages.IpDesbloqueado(done.Ip));
                default:
                    throw new InvalidOperationException("Resultado de desbloqueio desconhecido.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "security-unblock-ip", SafeError.Describe(ex));
            return ServerError(AdminMessages.UnblockFailed);
        }
    }

    private static async Task<IResult> StatsAsync(
        GetSecurityStats useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var stats = await useCase.ExecuteAsync(cancellationToken);
            var extras = new Dictionary<string, object?>();
            if (!stats.RedisAvailable)
            {
                extras["error"] = AdminMessages.RedisUnavailableField;
            }

            extras["blacklisted_ips"] = stats.Blacklisted.Select(b => new Dictionary<string, object?>
            {
                ["ip"] = b.Ip,
                ["reason"] = b.Reason,
                ["blacklisted_at"] = b.BlacklistedAt,
                ["permanent"] = b.Permanent,
            }).ToList();
            extras["blocked_ips"] = stats.Blocked.Select(b => new Dictionary<string, object?>
            {
                ["ip"] = b.Ip,
                ["attempts"] = b.Attempts,
                ["blocked_until"] = b.BlockedUntil,
                ["time_remaining"] = b.TimeRemaining,
            }).ToList();
            extras["suspicious_activity"] = stats.Suspicious.Select(s => new Dictionary<string, object?>
            {
                ["ip"] = s.Ip,
                ["recent_attempts"] = s.RecentAttempts,
                ["last_attempt"] = s.LastAttempt,
                ["reasons"] = s.Reasons,
            }).ToList();
            extras["total_blacklisted"] = stats.Blacklisted.Count;
            extras["total_blocked"] = stats.Blocked.Count;
            extras["total_suspicious"] = stats.Suspicious.Count;
            if (stats.RedisAvailable)
            {
                extras["redis_connected"] = true;
            }

            return Envelopes.B.Success(AdminMessages.StatsObtained, extras);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "security-stats", SafeError.Describe(ex));
            return ServerError(AdminMessages.StatsFailed);
        }
    }

    private static async Task<IResult> BlacklistAddAsync(
        HttpRequest request, BlacklistIp useCase, ILoggerFactory loggers, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            var security = loggers.CreateLogger(SecurityLog.Category);
            switch (await useCase.ExecuteAsync(AdminBody.Field(body, "ip"), AdminBody.Field(body, "reason"), cancellationToken))
            {
                case BlacklistIpResult.BadIp bad:
                    return IpProblem(bad.Problem, AdminMessages.IpMissingInList);
                case BlacklistIpResult.ReasonNotText:
                    return BadRequest(AdminMessages.ReasonNotText);
                case BlacklistIpResult.RedisUnavailable:
                    SecurityLog.BlacklistAddWithoutRedis(security, ClientIpText(body));
                    return ServerError(AdminMessages.BlacklistAddRedisDown);
                case BlacklistIpResult.Added added:
                    SecurityLog.BlacklistAdded(security, added.Ip, added.Reason);
                    return Envelopes.B.Success(
                        AdminMessages.BlacklistAdded(added.Ip),
                        new Dictionary<string, object?> { ["ip"] = added.Ip, ["reason"] = added.Reason });
                default:
                    throw new InvalidOperationException("Resultado de blacklist desconhecido.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "security-blacklist-add", SafeError.Describe(ex));
            return ServerError(AdminMessages.BlacklistAddFailed);
        }
    }

    /// <summary>IP do corpo só para o log de "Redis indisponível"; nesse ponto já foi validado como texto.</summary>
    private static string ClientIpText(System.Text.Json.JsonElement? body) =>
        IpClassifier.TryParseInput(AdminBody.Field(body, "ip").Text, out var normalized) ? normalized : string.Empty;

    private static async Task<IResult> BlacklistRemoveAsync(
        HttpRequest request, RemoveBlacklistedIp useCase, ILoggerFactory loggers, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            switch (await useCase.ExecuteAsync(AdminBody.Field(body, "ip"), cancellationToken))
            {
                case RemoveBlacklistedIpResult.BadIp bad:
                    return IpProblem(bad.Problem, AdminMessages.IpMissingInList);
                case RemoveBlacklistedIpResult.RedisUnavailable:
                    return ServerError(AdminMessages.BlacklistRemoveRedisDown);
                case RemoveBlacklistedIpResult.Removed removed:
                    SecurityLog.BlacklistRemoved(loggers.CreateLogger(SecurityLog.Category), removed.Ip);
                    return Envelopes.B.Success(
                        AdminMessages.BlacklistRemoved(removed.Ip),
                        new Dictionary<string, object?> { ["ip"] = removed.Ip });
                default:
                    throw new InvalidOperationException("Resultado de remoção da blacklist desconhecido.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "security-blacklist-remove", SafeError.Describe(ex));
            return ServerError(AdminMessages.BlacklistRemoveFailed);
        }
    }

    // ---- 5 e 6: cache do Gemini -------------------------------------------------------------------------------------

    private static async Task<IResult> CacheClearAsync(
        HttpRequest request, ClearLlmCache useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            // Corpo vazio = limpar tudo (desvio 077); corpo não vazio e inválido = 400.
            var optional = await AdminBody.ReadOptionalObjectAsync(request, cancellationToken);
            if (optional.Kind == OptionalBodyKind.Invalid)
            {
                return BadRequest(AdminMessages.BodyNotObject);
            }

            var body = optional.Value;
            switch (await useCase.ExecuteAsync(AdminBody.Field(body, "usuario_id"), AdminBody.Field(body, "pattern"), cancellationToken))
            {
                case ClearLlmCacheResult.InvalidUserId:
                    return BadRequest(AdminMessages.UserIdNotInteger);
                case ClearLlmCacheResult.InvalidPattern:
                    return BadRequest(AdminMessages.PatternNotText);
                case ClearLlmCacheResult.Cleared cleared:
                    var message = cleared.Plan switch
                    {
                        CacheClearPlan.ForUser user => AdminMessages.CacheClearedForUser(cleared.Deleted, user.UserId),
                        CacheClearPlan.ForPattern pattern => AdminMessages.CacheClearedForPattern(cleared.Deleted, pattern.Value),
                        _ => AdminMessages.CacheClearedAll(cleared.Deleted),
                    };
                    return Envelopes.B.Success(message, new Dictionary<string, object?> { ["keys_deleted"] = cleared.Deleted });
                default:
                    throw new InvalidOperationException("Resultado de limpeza do cache desconhecido.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "gemini-cache-clear", SafeError.Describe(ex));
            return ServerError(AdminMessages.CacheClearFailed);
        }
    }

    private static IResult CacheStats(GetLlmCacheStats useCase, ILogger<AdminModule> logger)
    {
        try
        {
            var metrics = useCase.Execute();
            return Envelopes.B.Success(AdminMessages.CacheStatsObtained, new Dictionary<string, object?>
            {
                ["total_requests"] = metrics.TotalRequests,
                ["cache_hits"] = metrics.CacheHits,
                ["cache_misses"] = metrics.CacheMisses,
                ["cache_saves"] = metrics.CacheSaves,
                ["cache_errors"] = metrics.CacheErrors,
                ["hit_rate"] = metrics.HitRate,
                ["breakdown_by_type"] = metrics.BreakdownByType.ToDictionary(
                    pair => pair.Key,
                    pair => (object?)new Dictionary<string, object?>
                    {
                        ["hits"] = pair.Value.Hits,
                        ["misses"] = pair.Value.Misses,
                        ["total"] = pair.Value.Total,
                        ["hit_rate"] = pair.Value.HitRate,
                    }),
                ["estimated_savings"] = new Dictionary<string, object?>
                {
                    ["calls_saved"] = metrics.EstimatedSavings.CallsSaved,
                    ["quota_saved_pct"] = metrics.EstimatedSavings.QuotaSavedPct,
                },
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "gemini-cache-stats", SafeError.Describe(ex));
            return ServerError(AdminMessages.CacheStatsFailed);
        }
    }

    // ---- 7 a 9: sessão do bot e alertas financeiros -----------------------------------------------------------------

    private static async Task<IResult> ClearBotSessionAsync(
        ClearBotSession useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var rows = await useCase.ExecuteAsync(cancellationToken);
            var message = AdminMessages.BotSessionCleared(rows);
            AdminLog.AdminFix(logger, message);
            return Envelopes.B.Success(message, new Dictionary<string, object?> { ["linhas_deletadas"] = rows });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "clear-bot-session", SafeError.Describe(ex));
            return ServerError(AdminMessages.BotSessionFailed);
        }
    }

    private static async Task<IResult> GetNotificationConfigAsync(
        string usuarioId, GetNotificationConfig useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        // O filtro do grupo só deixa passar dígitos que cabem em int.
        var id = int.Parse(usuarioId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            return await useCase.ExecuteAsync(id, cancellationToken) switch
            {
                GetNotificationConfigResult.Found found => Envelopes.B.Success(
                    AdminMessages.NotificationConfigObtained,
                    new Dictionary<string, object?>
                    {
                        ["usuario_id"] = id,
                        ["configuracoes"] = new Dictionary<string, object?>
                        {
                            ["resumo_matinal_ativo"] = found.Row.ResumoMatinalAtivo,
                            ["resumo_matinal_hora"] = found.Row.ResumoMatinalHora,
                            ["alertas_financeiros_ativos"] = found.Row.AlertasFinanceirosAtivos,
                        },
                    }),
                GetNotificationConfigResult.UserNotFound => Error(AdminMessages.UserNotFound, StatusCodes.Status404NotFound),
                _ => throw new InvalidOperationException("Resultado de configuração de notificação desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "get-notification-config", SafeError.Describe(ex));
            return ServerError(AdminMessages.NotificationConfigFailed);
        }
    }

    private static async Task<IResult> SetFinancialAlertsAsync(
        HttpRequest request, SetFinancialAlerts useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            return await useCase.ExecuteAsync(AdminBody.Field(body, "usuario_id"), AdminBody.Field(body, "ativo"), cancellationToken) switch
            {
                SetFinancialAlertsResult.MissingUserId => BadRequest(AdminMessages.UserIdMissing),
                SetFinancialAlertsResult.MissingActive => BadRequest(AdminMessages.ActiveRequired),
                SetFinancialAlertsResult.UserNotFound => Error(AdminMessages.UserNotFound, StatusCodes.Status404NotFound),
                SetFinancialAlertsResult.Updated updated => Envelopes.B.Success(
                    updated.Active ? AdminMessages.AlertsEnabled : AdminMessages.AlertsDisabled,
                    new Dictionary<string, object?>
                    {
                        ["configuracao"] = new Dictionary<string, object?> { ["alertas_financeiros_ativos"] = updated.Active },
                    }),
                _ => throw new InvalidOperationException("Resultado de alertas financeiros desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "config-alertas-financeiros", SafeError.Describe(ex));
            return ServerError(AdminMessages.FinancialAlertsFailed);
        }
    }

    // ---- 23 e 24: diagnóstico (009b) --------------------------------------------------------------------------------

    private static IResult OAuthConfigCheckEndpoint(GetOAuthConfigCheck useCase, ILogger<AdminModule> logger)
    {
        try
        {
            var check = useCase.Execute();
            return Envelopes.B.Success(AdminMessages.OAuthConfigChecked, new Dictionary<string, object?>
            {
                ["client_id_configured"] = check.ClientIdConfigured,
                ["client_secret_configured"] = check.ClientSecretConfigured,
                ["redirect_uri"] = check.RedirectUri,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "oauth-config-check", SafeError.Describe(ex));
            return ServerError(AdminMessages.OAuthConfigFailed);
        }
    }

    private static async Task<IResult> CalendarDebugAsync(
        RunCalendarDebug useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var report = await useCase.ExecuteAsync(cancellationToken);
            return Results.Content(report.Html, "text/html; charset=utf-8");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AdminLog.RouteFailed(logger, "debug-calendar", SafeError.Describe(ex));
            return ServerError(AdminMessages.CalendarDebugFailed);
        }
    }
}
