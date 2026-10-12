using System.Globalization;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Api.Admin;

/// <summary>
/// As 8 rotas <c>/admin/*</c> da 009c (25 a 33, menos <c>trigger-agenda-notifications</c> que não tem porta): dependem das
/// portas dos jobs (013) ou das leituras de teste. Os jobs rodam <b>inline</b> (Q-009-8) e com o token de parada da
/// aplicação, não o da requisição: o bot desiste aos 30 s, mas o processamento não deve ficar pela metade (o Flask também
/// continuava). Sem implementação da porta (<see cref="FeatureNotAvailableException"/>) a resposta é 503 (AC-009-39).
/// </summary>
internal static class DependentRoutes
{
    public const string MonthlyReportPath = "/admin/test-monthly-report/{usuarioId}";

    internal static void Map(RouteGroupBuilder admin)
    {
        admin.MapPost("/run-motor-agendamentos", RunMotorAsync);
        admin.MapPost("/trigger-agenda-notifications", TriggerAgenda);
        admin.MapPost("/trigger-bills-notifications", TriggerBillsAsync);
        admin.MapPost("/trigger-daily-briefing", TriggerDailyBriefingAsync);
        admin.MapPost("/trigger-monthly-reports-inicio", (IMonthlyReportJob job, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger) =>
            TriggerMonthlyAsync(job, MonthlyMoment.InicioMes, "trigger-monthly-reports-inicio", lifetime, logger));
        admin.MapPost("/trigger-monthly-reports-fim", (IMonthlyReportJob job, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger) =>
            TriggerMonthlyAsync(job, MonthlyMoment.FimMes, "trigger-monthly-reports-fim", lifetime, logger));
        admin.MapPost("/test-notification", TestNotificationAsync);
        admin.MapPost("/test-monthly-report/{usuarioId}", TestMonthlyReportAsync);
        admin.MapPost("/test-daily-briefing", TestDailyBriefingAsync);
    }

    /// <summary>Mesmo caso das outras rotas com id: outro método com id numérico = 405; senão 404 HTML, antes da autenticação.</summary>
    internal static IResult OtherMethods(string usuarioId) =>
        MeuSecretario.Api.Accounts.NumericIdFilter.IsNumeric(usuarioId)
            ? Results.StatusCode(StatusCodes.Status405MethodNotAllowed)
            : new MeuSecretario.Api.Accounts.FlaskNotFoundResult();

    private static IResult Error(string message, int status) => Envelopes.B.Error(message, status);

    private static IResult ServerError(string message) => Error(message, StatusCodes.Status500InternalServerError);

    private static IResult NotAvailable(ILogger logger, string route)
    {
        AdminLog.FeatureNotAvailable(logger, route);
        return Error(AdminMessages.FeatureNotAvailable, StatusCodes.Status503ServiceUnavailable);
    }

    private static IResult Failed(ILogger logger, string route, Exception exception, string message)
    {
        AdminLog.RouteFailed(logger, route, SafeError.Describe(exception));
        return ServerError(message);
    }

    // ---- 25: motor de agendamentos ---------------------------------------------------------------------------------

    private static async Task<IResult> RunMotorAsync(IScheduleMotor motor, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger)
    {
        try
        {
            AdminLog.Motor(logger, "Processamento de agendamentos iniciado pela rota admin");
            var result = await motor.RunAsync(lifetime.ApplicationStopping);
            AdminLog.Motor(logger, result == MotorRunResult.AlreadyRunning ? "Já em processamento" : "Processamento concluído");
            return Envelopes.B.Success(result == MotorRunResult.AlreadyRunning ? AdminMessages.MotorAlreadyRunning : AdminMessages.MotorProcessed);
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, "run-motor-agendamentos");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "run-motor-agendamentos", ex, AdminMessages.MotorFailed);
        }
    }

    // ---- 26 a 30: trigger-* ----------------------------------------------------------------------------------------

    /// <summary>Desvio 028: a agenda diária foi substituída pelo resumo matinal; sem porta, sem I/O, sem 500.</summary>
    private static IResult TriggerAgenda() =>
        Envelopes.B.Success(AdminMessages.AgendaReplaced, new Dictionary<string, object?>
        {
            ["usuarios_processados"] = 0,
            ["notificacoes_enviadas"] = 0,
            ["erros"] = 0,
        });

    private static async Task<IResult> TriggerBillsAsync(IBillsNotificationJob job, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger)
    {
        try
        {
            var result = await job.RunAsync(lifetime.ApplicationStopping);
            return Envelopes.B.Success(AdminMessages.BillsTriggered, new Dictionary<string, object?>
            {
                ["usuarios_processados"] = result.UsersProcessed,
                ["notificacoes_enviadas"] = result.NotificationsSent,
                ["erros"] = result.Errors,
                ["hora_processamento"] = result.ProcessedAt,
            });
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, "trigger-bills-notifications");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "trigger-bills-notifications", ex, AdminMessages.BillsTriggerFailed);
        }
    }

    private static async Task<IResult> TriggerDailyBriefingAsync(IDailyBriefingJob job, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger)
    {
        try
        {
            var result = await job.RunForCurrentMinuteAsync(lifetime.ApplicationStopping);
            if (result.NoUsers)
            {
                return Envelopes.B.Success(AdminMessages.NoBriefingUsers(result.Time), new Dictionary<string, object?>
                {
                    ["usuarios_processados"] = 0,
                    ["enviados_sucesso"] = 0,
                    ["horario"] = result.Time,
                });
            }

            return Envelopes.B.Success(AdminMessages.BriefingTriggered, new Dictionary<string, object?>
            {
                ["usuarios_processados"] = result.UsersProcessed,
                ["enviados_sucesso"] = result.Sent,
                ["erros"] = result.Errors,
                ["horario"] = result.Time,
            });
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, "trigger-daily-briefing");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "trigger-daily-briefing", ex, AdminMessages.BriefingTriggerFailed);
        }
    }

    private static async Task<IResult> TriggerMonthlyAsync(
        IMonthlyReportJob job, MonthlyMoment moment, string route, IHostApplicationLifetime lifetime, ILogger<AdminModule> logger)
    {
        try
        {
            var result = await job.RunAsync(moment, lifetime.ApplicationStopping);
            return Envelopes.B.Success(AdminMessages.MonthlyTriggered(result.Moment), new Dictionary<string, object?>
            {
                ["momento_envio"] = result.Moment,
                ["horario_processamento"] = result.ProcessedAt,
                ["total_usuarios"] = result.TotalUsers,
                ["enviados_sucesso"] = result.Sent,
                ["enviados_erro"] = result.Failed,
                ["usuarios_processados"] = result.Users.Select(UserEntry).ToList(),
                ["erros"] = result.Errors.Select(ErrorEntry).ToList(),
                ["duracao_segundos"] = result.DurationSeconds,
            });
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, route);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, route, ex, AdminMessages.MonthlyTriggerFailed);
        }
    }

    private static Dictionary<string, object?> UserEntry(MonthlyReportUserEntry entry)
    {
        // Chaves na ordem do Flask; hora configurada e gráfico só existem em "enviado".
        var item = new Dictionary<string, object?> { ["usuario_id"] = entry.UserId, ["nome"] = entry.Name };
        if (entry.ConfiguredHour is not null)
        {
            item["hora_configurada"] = entry.ConfiguredHour;
        }

        item["status"] = entry.Status;
        item["mensagem"] = entry.Message;
        if (entry.ChartSent is { } chart)
        {
            item["grafico_enviado"] = chart;
        }

        return item;
    }

    private static Dictionary<string, object?> ErrorEntry(MonthlyReportErrorEntry entry) =>
        entry.Type is not null
            ? new Dictionary<string, object?> { ["tipo"] = entry.Type, ["erro"] = entry.Error }
            : new Dictionary<string, object?> { ["usuario_id"] = entry.UserId, ["nome"] = entry.Name, ["erro"] = entry.Error };

    // ---- 31: test-notification -------------------------------------------------------------------------------------

    private static async Task<IResult> TestNotificationAsync(
        HttpRequest request, SendTestNotification useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            return await useCase.ExecuteAsync(AdminBody.Field(body, "tipo"), AdminBody.Field(body, "usuario_id"), cancellationToken) switch
            {
                SendTestNotificationResult.InvalidUserId => Error(AdminMessages.UserIdNotInteger, StatusCodes.Status400BadRequest),
                SendTestNotificationResult.UserNotFound => Error(AdminMessages.UserNotFound, StatusCodes.Status404NotFound),
                SendTestNotificationResult.NotConnected => Error(AdminMessages.TestNotConnected, StatusCodes.Status400BadRequest),
                SendTestNotificationResult.InvalidType => Error(AdminMessages.TestInvalidType, StatusCodes.Status400BadRequest),
                SendTestNotificationResult.SendFailed => ServerError(AdminMessages.TestSendFailed),
                SendTestNotificationResult.Sent sent => Envelopes.B.Success(
                    AdminMessages.TestNotificationSent(sent.UserName),
                    new Dictionary<string, object?>
                    {
                        ["tipo"] = sent.Type,
                        ["usuario_id"] = sent.UserId,
                        ["numero_whatsapp"] = sent.WhatsappNumber,
                    }),
                _ => throw new InvalidOperationException("Resultado de notificação de teste desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "test-notification", ex, AdminMessages.TestNotificationFailed);
        }
    }

    // ---- 32: test-monthly-report -----------------------------------------------------------------------------------

    private static async Task<IResult> TestMonthlyReportAsync(
        string usuarioId, HttpRequest request, SendTestMonthlyReport useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        // O filtro do grupo só deixa passar dígitos que cabem em int.
        var id = int.Parse(usuarioId, NumberStyles.None, CultureInfo.InvariantCulture);
        try
        {
            // request.args.get: primeiro valor; ausente = padrão (INICIO_MES); vazio é valor (inválido).
            string? moment = request.Query.TryGetValue("momento", out var values) ? values.FirstOrDefault() ?? string.Empty : null;
            return await useCase.ExecuteAsync(id, moment, cancellationToken) switch
            {
                SendTestMonthlyReportResult.InvalidMoment => Error(AdminMessages.MonthlyMomentInvalid, StatusCodes.Status400BadRequest),
                SendTestMonthlyReportResult.Done { Report: { Success: true } report } => Envelopes.B.Success(
                    AdminMessages.MonthlyReportSent,
                    SuccessExtras(report)),
                SendTestMonthlyReportResult.Done { Report: var report } => ManualFailure(report),
                _ => throw new InvalidOperationException("Resultado de relatório de teste desconhecido."),
            };
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, "test-monthly-report");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "test-monthly-report", ex, AdminMessages.MonthlyReportFailed);
        }
    }

    private static Dictionary<string, object?> SuccessExtras(ManualReportResult report)
    {
        var extras = new Dictionary<string, object?>
        {
            ["sucesso"] = true,
            ["usuario_id"] = report.UserId,
            ["nome"] = report.Name,
            ["mensagem_enviada"] = report.MessageSent,
            ["grafico_enviado"] = report.ChartSent,
        };
        if (report.Data is not null)
        {
            extras["dados"] = MonthlyReportJson.Data(report.Data);
        }

        return extras;
    }

    /// <summary>
    /// 400 com a <c>mensagem</c> genérica (o <c>resultado.get('mensagem')</c> do Flask nunca encontra a chave) e o motivo em
    /// <c>erro</c>; <c>dados</c> só quando o relatório foi calculado.
    /// </summary>
    private static IResult ManualFailure(ManualReportResult report)
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = "erro",
            ["mensagem"] = AdminMessages.MonthlyReportError,
            ["sucesso"] = false,
            ["erro"] = report.Error,
        };
        if (report.Data is not null)
        {
            body["dados"] = MonthlyReportJson.Data(report.Data);
        }

        return Envelopes.Json(body, StatusCodes.Status400BadRequest);
    }

    // ---- 33: test-daily-briefing -----------------------------------------------------------------------------------

    private static async Task<IResult> TestDailyBriefingAsync(
        HttpRequest request, SendTestDailyBriefing useCase, ILogger<AdminModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await AdminBody.ReadObjectAsync(request, cancellationToken);
            return await useCase.ExecuteAsync(AdminBody.Field(body, "usuario_id"), cancellationToken) switch
            {
                SendTestDailyBriefingResult.InvalidUserId => Error(AdminMessages.UserIdNotInteger, StatusCodes.Status400BadRequest),
                SendTestDailyBriefingResult.UserNotFound missing => Error(AdminMessages.BriefingUserNotFound(missing.UserId), StatusCodes.Status404NotFound),
                SendTestDailyBriefingResult.NoWhatsapp none => Error(AdminMessages.BriefingNoWhatsapp(none.UserId), StatusCodes.Status400BadRequest),
                SendTestDailyBriefingResult.PrepareFailed => ServerError(AdminMessages.BriefingPrepareFailed),
                SendTestDailyBriefingResult.EmptyMessage => ServerError(AdminMessages.BriefingEmptyMessage),
                SendTestDailyBriefingResult.SendFailed => ServerError(AdminMessages.BriefingSendFailed),
                SendTestDailyBriefingResult.Sent sent => Envelopes.B.Success(
                    AdminMessages.BriefingSent,
                    new Dictionary<string, object?>
                    {
                        ["usuario_id"] = sent.UserId,
                        ["numero_whatsapp"] = sent.WhatsappNumber,
                        ["total_eventos"] = sent.TotalEvents,
                        ["preview_mensagem"] = sent.Preview,
                    }),
                _ => throw new InvalidOperationException("Resultado de resumo matinal de teste desconhecido."),
            };
        }
        catch (FeatureNotAvailableException)
        {
            return NotAvailable(logger, "test-daily-briefing");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(logger, "test-daily-briefing", ex, AdminMessages.BriefingTestFailed);
        }
    }
}

/// <summary>
/// <c>dados</c> do relatório mensal com as chaves do Flask (<c>generate_monthly_report_data</c>). As datas de
/// <c>periodo</c> saem como o <c>jsonify</c> do Flask serializa <c>date</c> (RFC 822, <c>Sat, 01 Aug 2026 00:00:00 GMT</c>).
/// </summary>
internal static class MonthlyReportJson
{
    public static string HttpDate(DateOnly date) =>
        date.ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture) + " 00:00:00 GMT";

    public static Dictionary<string, object?> Data(MonthlyReportData data) => new()
    {
        ["mes"] = data.Month,
        ["ano"] = data.Year,
        ["periodo"] = new Dictionary<string, object?>
        {
            ["inicio"] = HttpDate(data.PeriodStart),
            ["fim"] = HttpDate(data.PeriodEnd),
        },
        ["totais"] = new Dictionary<string, object?>
        {
            ["total_despesas"] = data.Totals.TotalExpenses,
            ["total_rendas"] = data.Totals.TotalIncome,
            ["saldo_periodo"] = data.Totals.PeriodBalance,
            ["total_transacoes"] = data.Totals.TotalTransactions,
        },
        ["top_categorias"] = data.TopCategories.Select(c => new Dictionary<string, object?>
        {
            ["categoria"] = c.Category,
            ["valor"] = c.Value,
            ["percentual"] = c.Percent,
        }).ToList(),
        ["comparacao"] = new Dictionary<string, object?>
        {
            ["mes_atual"] = data.Comparison.CurrentMonth,
            ["mes_anterior"] = data.Comparison.PreviousMonth,
            ["variacao_valor"] = data.Comparison.VariationValue,
            ["variacao_percentual"] = data.Comparison.VariationPercent,
        },
        ["potes"] = data.Pots.Select(p => new Dictionary<string, object?>
        {
            ["nome"] = p.Name,
            ["limite"] = p.Limit,
            ["usado"] = p.Used,
            ["saldo"] = p.Balance,
            ["percentual"] = p.Percent,
        }).ToList(),
        ["contas"] = new Dictionary<string, object?>
        {
            ["pagas"] = data.Accounts.Paid,
            ["valor_pago"] = data.Accounts.PaidValue,
            ["pendentes"] = data.Accounts.Pending,
            ["valor_pendente"] = data.Accounts.PendingValue,
        },
    };
}
