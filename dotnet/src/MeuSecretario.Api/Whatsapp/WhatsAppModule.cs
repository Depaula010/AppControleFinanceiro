using System.Text.Json;
using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.Infrastructure.Automation;
using MeuSecretario.Infrastructure.Configuration;
using MeuSecretario.Infrastructure.Whatsapp;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Feature 012: <c>POST /webhook-whatsapp</c> (envelope E). A ordem das verificações é a do Flask: banco, Gemini configurado,
/// assinatura HMAC (opcional), <c>x-api-key</c>, corpo. O corpo é lido como bytes brutos uma única vez (a assinatura vale
/// sobre os bytes) e só então interpretado como JSON.
/// </summary>
public sealed class WhatsAppModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.TryAddSingleton<IPendingIdGenerator, UuidPendingIdGenerator>();
        services.AddScoped<IWhatsAppUserLookup, WhatsAppUserLookup>();
        services.AddScoped<IPendingTransactionState, PendingTransactionState>();
        services.AddScoped<IPendingEventStore, PendingEventStore>();
        services.AddScoped<ICheckinSession, CheckinSessionState>();
        services.AddScoped<IRouteOriginChoice, RouteOriginChoiceStore>();
        services.TryAddScoped<INightlyCheckinResponder, NullNightlyCheckinResponder>();
        services.AddSingleton<IWhatsAppDiagnostics, WhatsAppDiagnostics>();

        services.AddScoped<IntentClassifier>();
        services.AddScoped<IntentRouter>();
        services.AddScoped<HandleWhatsAppMessage>();

        services.AddScoped<IWhatsAppStep, CheckinStep>();
        services.AddScoped<IWhatsAppStep, BareCancelStep>();

        services.AddScoped<IWhatsAppIntent, HelpIntent>();

        FinancialWhatsAppModule.AddFinancialServices(services); // 012a, 012b, 012c
        FixedBillsWhatsAppModule.AddFixedBillsServices(services); // 012d
        ReportsWhatsAppModule.AddReportsServices(services); // desvio 256
    }

    public void MapEndpoints(IEndpointRouteBuilder app) => app.MapPost("/webhook-whatsapp", WebhookAsync);

    private static async Task<IResult> WebhookAsync(
        HttpRequest request,
        IDatabaseHealth database,
        IIntegrationSettings settings,
        AppSettings appSettings,
        IApiKeyValidator apiKeys,
        HandleWhatsAppMessage useCase,
        ILogger<WhatsAppModule> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!(await database.CheckAsync(cancellationToken)).Connected)
            {
                WhatsAppLog.Unavailable(logger, "banco de dados indisponível");
                return Envelopes.E.Error(WhatsAppMessages.DatabaseUnavailable, StatusCodes.Status503ServiceUnavailable);
            }

            if (string.IsNullOrEmpty(settings.GeminiApiKey))
            {
                WhatsAppLog.Unavailable(logger, "GEMINI_API_KEY do sistema vazia");
                return Envelopes.B.Error(WhatsAppMessages.NotConfigured, StatusCodes.Status503ServiceUnavailable);
            }

            var raw = await ReadBytesAsync(request, cancellationToken);
            WhatsAppLog.Received(logger, raw.Length);

            var signature = request.Headers["X-Webhook-Signature"].FirstOrDefault()?.Trim();
            if (string.IsNullOrEmpty(signature))
            {
                WhatsAppLog.NoSignature(logger);
            }
            else if (!WebhookSignature.Verify(raw, signature, appSettings.WebhookSignatureKey))
            {
                WhatsAppLog.InvalidSignature(logger);
                return Envelopes.E.Error(WhatsAppMessages.InvalidSignature, StatusCodes.Status401Unauthorized);
            }

            if (!apiKeys.IsValid(request.Headers["x-api-key"].FirstOrDefault()?.Trim()))
            {
                WhatsAppLog.Unauthorized(logger);
                return Envelopes.E.Error(WhatsAppMessages.Unauthorized, StatusCodes.Status401Unauthorized);
            }

            return Parse(raw) switch
            {
                InputParse.Missing => MissingDataResult(logger, raw),
                InputParse.WrongType => Envelopes.E.Error(WhatsAppMessages.ProcessingFailed, StatusCodes.Status500InternalServerError),
                InputParse.Valid valid => await ProcessedResultAsync(useCase, valid, logger, cancellationToken),
                _ => throw new InvalidOperationException("Resultado do parse desconhecido."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            WhatsAppLog.WebhookFailed(logger, SafeError.Describe(ex));
            if (!WhatsAppTexts.IsQuotaMessage(ex.Message))
            {
                return Envelopes.E.Error(WhatsAppMessages.ProcessingFailed, StatusCodes.Status500InternalServerError);
            }

            WhatsAppLog.QuotaExceeded(logger);
            return Envelopes.E.Error(WhatsAppTexts.AiLimit, StatusCodes.Status429TooManyRequests);
        }
    }

    private static async Task<IResult> ProcessedResultAsync(
        HandleWhatsAppMessage useCase,
        InputParse.Valid valid,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var reply = await useCase.ExecuteAsync(valid.Input, cancellationToken);
        WhatsAppLog.Processed(logger);
        return Envelopes.E.Success(reply);
    }

    /// <summary>400 "Dados faltando": registra só os NOMES das chaves recebidas (nunca valores) para diagnosticar contrato do bot.</summary>
    private static IResult MissingDataResult(ILogger logger, byte[] raw)
    {
        var keys = "(corpo vazio ou inválido)";
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                keys = string.Join(",", doc.RootElement.EnumerateObject().Take(12).Select(p => p.Name.Length > 40 ? p.Name[..40] : p.Name));
            }
        }
        catch (JsonException)
        {
            // corpo não é JSON: mantém o texto padrão.
        }

        WhatsAppLog.MissingFields(logger, keys);
        return Envelopes.E.BadRequest(WhatsAppMessages.MissingData);
    }

    private static async Task<byte[]> ReadBytesAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// Corpo ausente, inválido ou que não seja objeto, ou <c>texto</c>/<c>numero_remetente</c> falsos ⇒ 400 (desvio 024);
    /// verdadeiros mas que não são texto ⇒ 500 (o Flask quebra em <c>sanitize_input</c>/<c>split</c>).
    /// </summary>
    internal static InputParse Parse(byte[] raw)
    {
        JsonElement body;
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new InputParse.Missing();
            }

            body = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new InputParse.Missing();
        }

        var text = Property(body, "texto");
        var number = Property(body, "numero_remetente");
        if (!PyJsonValue.IsTruthy(text) || !PyJsonValue.IsTruthy(number))
        {
            return new InputParse.Missing();
        }

        if (text!.Value.ValueKind != JsonValueKind.String || number!.Value.ValueKind != JsonValueKind.String)
        {
            return new InputParse.WrongType();
        }

        var data = Property(body, "media_data");
        var type = Property(body, "media_type");
        var fileName = body.TryGetProperty("media_filename", out var name) ? TextOf(name) : "arquivo";
        var media = new MediaPayload(
            PyJsonValue.IsTruthy(data),
            data is { ValueKind: JsonValueKind.String } d ? d.GetString() : null,
            type is { } t && PyJsonValue.IsTruthy(t) ? PyJsonValue.Str(t) : null,
            fileName);

        return new InputParse.Valid(new WebhookInput(text.Value.GetString()!, number.Value.GetString()!, media));
    }

    private static JsonElement? Property(JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) ? value : null;

    private static string? TextOf(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Null => null,
            _ => PyJsonValue.Str(element),
        };

    internal abstract record InputParse
    {
        public sealed record Missing : InputParse;

        public sealed record WrongType : InputParse;

        public sealed record Valid(WebhookInput Input) : InputParse;
    }
}
