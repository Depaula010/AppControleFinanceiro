using System.Globalization;
using System.Text.Json;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Transactions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Infrastructure.Automation;

namespace MeuSecretario.Api.Automation;

/// <summary>
/// Feature 011: <c>POST /webhook-automate</c>, <c>POST /api/transacao</c>, <c>POST /webhook-sms-payment</c>,
/// <c>PATCH /api/agendamento/{id}/reserva</c>, <c>GET /api/agendamentos/reserva</c> (chave do usuário no corpo ou na query, sem JWT)
/// e <c>POST /api/fatura/{id}/reprocessar</c> (<c>x-api-key</c>, desvio 003). Envelope B, com as exceções da §3.
/// </summary>
/// <remarks>
/// O módulo vem antes de <c>TransactionsModule</c> na ordem por nome: o populador real registrado aqui vence o
/// <c>TryAddScoped</c> do populador vazio da 004 (RN-011-14).
/// </remarks>
public sealed class AutomationModule : IFeatureModule
{
    private const string IdPattern = @"^\d+$";

    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IUserKeyReader, UserKeyReader>();
        services.AddScoped<IUserApiKeyAuthenticator, UserApiKeyAuthenticator>();
        services.AddScoped<IAutomationLookups, AutomationLookups>();
        services.AddScoped<IBudgetReader, BudgetReader>();
        services.AddScoped<IReserveScheduleStore, ReserveScheduleStore>();
        services.AddScoped<IInvoiceScheduleUnitOfWork, InvoiceScheduleUnitOfWork>();
        services.AddScoped<IInvoiceScheduleProcessor, InvoiceScheduleProcessor>();
        services.AddScoped<IInvoiceSchedulePopulator, InvoiceSchedulePopulator>();
        services.AddSingleton<IPendingIdGenerator, UuidPendingIdGenerator>();
        services.AddScoped<IPendingTransactionStore, PendingTransactionStore>();

        services.AddScoped<TransactionCategorizer>();
        services.AddScoped<CurrentInvoiceResolver>();
        services.AddScoped<BudgetValidator>();
        services.AddScoped<DirectPoster>();

        services.AddScoped<ProcessAutomateNotification>();
        services.AddScoped<RegisterApiTransaction>();
        services.AddScoped<ProcessSmsPayment>();
        services.AddScoped<ToggleReserveSchedule>();
        services.AddScoped<ListReserveSchedules>();
        services.AddScoped<ReprocessInvoice>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/webhook-automate", AutomateAsync);
        app.MapPost("/api/transacao", ApiTransactionAsync);
        app.MapPost("/webhook-sms-payment", SmsAsync);

        // <int:id> do Flask: só dígitos; o resto nem casa a rota (404 do UnmatchedRoute) e outro método dá 405.
        app.MapMethods("/api/agendamento/{id:regex(" + IdPattern + ")}/reserva", ["PATCH"], ToggleAsync);
        app.MapGet("/api/agendamentos/reserva", ListAsync);
        app.MapPost("/api/fatura/{id:regex(" + IdPattern + ")}/reprocessar", ReprocessAsync)
            .RequireApiKey(AutomationResponses.Unauthorized);
    }

    private static async Task<IResult> AutomateAsync(
        HttpRequest request, ProcessAutomateNotification useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            var text = Field(body, "texto");
            var key = Field(body, "user_api_key");
            var input = new AutomateInput(
                !PyJsonValue.IsTruthy(text) || !PyJsonValue.IsTruthy(key), TextOf(key), TextOf(text));

            return await useCase.ExecuteAsync(input, cancellationToken) switch
            {
                AutomateResult.NotConfigured => AutomationResponses.BError(AutomationMessages.NotConfigured, StatusCodes.Status503ServiceUnavailable),
                AutomateResult.MissingData => AutomationResponses.BError(AutomationMessages.MissingData, StatusCodes.Status400BadRequest),
                AutomateResult.Unauthorized => AutomationResponses.BError(AutomationMessages.InvalidApiKey, StatusCodes.Status401Unauthorized),
                AutomateResult.Invalid invalid => AutomationResponses.BError(invalid.Message, StatusCodes.Status400BadRequest),
                AutomateResult.Saved => AutomationResponses.AutomateSaved(),
                AutomateResult.Pending pending => AutomationResponses.AutomatePending(pending.TransactionId),
                _ => throw new InvalidOperationException("Resultado do webhook-automate desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.AutomateFailed(logger, SafeError.Describe(ex));
            return AutomationResponses.Fail(FailureText(ex, AutomationMessages.AutomateFailed));
        }
    }

    private static async Task<IResult> ApiTransactionAsync(
        HttpRequest request, RegisterApiTransaction useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            return await useCase.ExecuteAsync(body, cancellationToken) switch
            {
                ApiTransactionResult.NotConfigured => AutomationResponses.BError(AutomationMessages.NotConfigured, StatusCodes.Status503ServiceUnavailable),
                ApiTransactionResult.InvalidJson => AutomationResponses.ApiInvalidJson(),
                ApiTransactionResult.Missing missing => AutomationResponses.BError(AutomationMessages.MissingFields(missing.Fields), StatusCodes.Status400BadRequest),
                ApiTransactionResult.InvalidValue invalid => AutomationResponses.BError(AutomationMessages.InvalidValue(invalid.Reason), StatusCodes.Status400BadRequest),
                ApiTransactionResult.InvalidPaymentType => AutomationResponses.BError(AutomationMessages.InvalidPaymentType(), StatusCodes.Status400BadRequest),
                ApiTransactionResult.Unauthorized => AutomationResponses.BError(AutomationMessages.InvalidApiKey, StatusCodes.Status401Unauthorized),
                ApiTransactionResult.Invalid invalid => AutomationResponses.BError(invalid.Message, StatusCodes.Status400BadRequest),
                ApiTransactionResult.AccountNotFound notFound => AutomationResponses.BError(AutomationMessages.AccountNotFound(notFound.Name), StatusCodes.Status400BadRequest),
                ApiTransactionResult.SavedWithoutRedis saved => AutomationResponses.ApiSuccess(
                    AutomationMessages.ApiSavedWithoutRedis, new Dictionary<string, object?> { ["categoria_sugerida"] = saved.CategoryLabel }),
                ApiTransactionResult.SavedAfterRedisError => AutomationResponses.ApiSuccess(
                    AutomationMessages.ApiSavedAfterRedisError, new Dictionary<string, object?> { ["transaction_id"] = null }),
                ApiTransactionResult.Pending pending => AutomationResponses.ApiSuccess(
                    AutomationMessages.ApiPending,
                    new Dictionary<string, object?>
                    {
                        ["transaction_id"] = pending.TransactionId,
                        ["categoria_sugerida"] = pending.CategoryLabel,
                        ["conta_utilizada"] = pending.AccountName,
                        ["vinculado_fatura"] = pending.LinkedToInvoice,
                    }),
                _ => throw new InvalidOperationException("Resultado de /api/transacao desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.ApiTransactionFailed(logger, SafeError.Describe(ex));
            return AutomationResponses.Fail(FailureText(ex, AutomationMessages.ApiTransactionFailed));
        }
    }

    private static async Task<IResult> SmsAsync(
        HttpRequest request, ProcessSmsPayment useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            return await useCase.ExecuteAsync(body, cancellationToken) switch
            {
                SmsPaymentResult.DatabaseUnavailable => AutomationResponses.SmsDatabaseUnavailable(),
                SmsPaymentResult.MissingData => AutomationResponses.BError(AutomationMessages.MissingData, StatusCodes.Status400BadRequest),
                SmsPaymentResult.Unauthorized => AutomationResponses.BError(AutomationMessages.InvalidApiKey, StatusCodes.Status401Unauthorized),
                SmsPaymentResult.Invalid invalid => AutomationResponses.BError(invalid.Message, StatusCodes.Status400BadRequest),
                SmsPaymentResult.Settled settled => AutomationResponses.SmsSettled(settled.BillDescription, settled.TransactionId),
                SmsPaymentResult.Registered => AutomationResponses.SmsRegistered(),
                _ => throw new InvalidOperationException("Resultado do webhook-sms-payment desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.SmsFailed(logger, SafeError.Describe(ex));
            return AutomationResponses.Fail(FailureText(ex, AutomationMessages.SmsFailed));
        }
    }

    private static async Task<IResult> ToggleAsync(
        string id, HttpRequest request, ToggleReserveSchedule useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        var scheduleId = ParseId(id);
        try
        {
            var body = await JsonBody.ReadObjectAsync(request, cancellationToken);
            var parsed = ReserveToggleInput.Parse(body);

            return await useCase.ExecuteAsync(parsed, scheduleId, cancellationToken) switch
            {
                ToggleReserveResult.MissingFields => AutomationResponses.BError(AutomationMessages.ToggleMissingFields, StatusCodes.Status400BadRequest),
                ToggleReserveResult.Unauthorized => AutomationResponses.BError(AutomationMessages.InvalidApiKeyAccented, StatusCodes.Status401Unauthorized),
                ToggleReserveResult.InvalidInclude => AutomationResponses.BError(InputMessages.IncludeNotBoolean, StatusCodes.Status400BadRequest),
                ToggleReserveResult.NotFound => AutomationResponses.BError(AutomationMessages.ScheduleNotFound, StatusCodes.Status404NotFound),
                ToggleReserveResult.Updated updated => AutomationResponses.ToggleUpdated(updated, scheduleId),
                _ => throw new InvalidOperationException("Resultado do PATCH da reserva desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.ToggleFailed(logger, scheduleId, SafeError.Describe(ex));
            return AutomationResponses.Fail(AutomationMessages.ToggleFailed);
        }
    }

    private static async Task<IResult> ListAsync(
        HttpRequest request, ListReserveSchedules useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        try
        {
            var input = new ListReserveInput(
                Query(request, "api_key"), Query(request, "incluir_na_reserva"), Query(request, "periodicidade"),
                Query(request, "categoria"), Query(request, "limit"), Query(request, "offset"));

            return await useCase.ExecuteAsync(input, cancellationToken) switch
            {
                ListReserveResult.MissingKey => AutomationResponses.BError(AutomationMessages.ListMissingKey, StatusCodes.Status400BadRequest),
                ListReserveResult.Unauthorized => AutomationResponses.BError(AutomationMessages.InvalidApiKeyAccented, StatusCodes.Status401Unauthorized),
                ListReserveResult.InvalidPeriodicity => AutomationResponses.BError(ReserveListQuery.PeriodicityMessage, StatusCodes.Status400BadRequest),
                ListReserveResult.InvalidPaging paging => AutomationResponses.BError(ReserveListQuery.PagingMessage(paging.Field), StatusCodes.Status400BadRequest),
                ListReserveResult.Listed listed => AutomationResponses.Listed(listed),
                _ => throw new InvalidOperationException("Resultado da listagem da reserva desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.ListFailed(logger, SafeError.Describe(ex));
            return AutomationResponses.Fail(AutomationMessages.ListFailed);
        }
    }

    private static async Task<IResult> ReprocessAsync(
        string id, ReprocessInvoice useCase, ILogger<AutomationModule> logger, CancellationToken cancellationToken)
    {
        var invoiceId = ParseId(id);
        try
        {
            return await useCase.ExecuteAsync(invoiceId, cancellationToken) switch
            {
                ReprocessInvoiceResult.NotFound notFound => AutomationResponses.BError(AutomationMessages.InvoiceNotFound(notFound.InvoiceId), StatusCodes.Status404NotFound),
                ReprocessInvoiceResult.Processed processed => AutomationResponses.Reprocessed(processed),
                _ => throw new InvalidOperationException("Resultado do reprocessamento desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AutomationLog.ReprocessFailed(logger, invoiceId, SafeError.Describe(ex));
            return AutomationResponses.Fail(AutomationMessages.ReprocessFailed);
        }
    }

    /// <summary>Texto de falha do Gemini já escrito para o usuário final (o Flask o deixa subir); o resto é a mensagem fixa (RN-011-05).</summary>
    private static string FailureText(Exception exception, string fixedMessage) => exception switch
    {
        LlmKeyUnavailableException or LlmQuotaExceededException or LlmPermissionDeniedException => exception.Message,
        _ => fixedMessage,
    };

    private static JsonElement? Field(JsonElement? body, string name) =>
        body is { } root && root.TryGetProperty(name, out var value) ? value : null;

    private static string? TextOf(JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    /// <summary>Valor da query como o <c>request.args.get</c>: <c>null</c> se a chave não veio, vazio se veio sem valor.</summary>
    private static string? Query(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var values) ? values.FirstOrDefault() ?? string.Empty : null;

    /// <summary>Só dígitos casam a rota; número acima de <see cref="int.MaxValue"/> nunca é um id existente: vira 0 (404 do caso de uso).</summary>
    private static int ParseId(string id) =>
        int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
