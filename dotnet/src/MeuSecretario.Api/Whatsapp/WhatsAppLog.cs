using MeuSecretario.Application.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>
/// Logs da feature 012. Só o tipo da exceção, o nome da intenção e o número mascarado: nunca o texto da mensagem, a chave,
/// a assinatura, o corpo, prompt, resposta do Gemini nem valores do usuário (desvio 008).
/// </summary>
internal static partial class WhatsAppLog
{
    [LoggerMessage(EventId = 12001, Level = LogLevel.Error, Message = "Erro no /webhook-whatsapp: {Error}")]
    public static partial void WebhookFailed(ILogger logger, string error);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Error, Message = "Erro na intenção '{Intent}' do WhatsApp: {Error}")]
    public static partial void IntentFailed(ILogger logger, string intent, string error);

    [LoggerMessage(EventId = 12003, Level = LogLevel.Warning, Message = "Webhook do WhatsApp com assinatura HMAC inválida")]
    public static partial void InvalidSignature(ILogger logger);

    [LoggerMessage(EventId = 12004, Level = LogLevel.Warning, Message = "Webhook do WhatsApp sem x-api-key válida")]
    public static partial void Unauthorized(ILogger logger);

    [LoggerMessage(EventId = 12005, Level = LogLevel.Information, Message = "Webhook do WhatsApp sem assinatura HMAC (modo compatibilidade)")]
    public static partial void NoSignature(ILogger logger);

    [LoggerMessage(EventId = 12006, Level = LogLevel.Warning, Message = "Limite do Gemini atingido no /webhook-whatsapp")]
    public static partial void QuotaExceeded(ILogger logger);
}

/// <summary>Implementa a porta de diagnóstico da Application com o logger da Api.</summary>
internal sealed class WhatsAppDiagnostics(ILogger<WhatsAppModule> logger) : IWhatsAppDiagnostics
{
    public void IntentFailed(string intent, string errorType) => WhatsAppLog.IntentFailed(logger, intent, errorType);
}
