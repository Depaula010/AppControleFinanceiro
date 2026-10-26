using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Llm;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.Application.Whatsapp;

/// <summary>Contexto de um passo do pipeline (texto já sanitizado).</summary>
/// <param name="Lower"><c>texto.strip().lower()</c>.</param>
public sealed record PipelineContext(WhatsAppUser User, string Number, string Message, string Lower, MediaPayload? Media);

/// <summary>
/// Um passo do pipeline da §4 da spec (eventos, rota, check-in, confirmação de transação, cancelar solto, pagamento). Devolve a
/// resposta ou <c>null</c> para seguir ao próximo. Os passos rodam na ordem de <see cref="Order"/> (a do Flask).
/// </summary>
public interface IWhatsAppStep
{
    int Order { get; }

    Task<string?> HandleAsync(PipelineContext context, CancellationToken cancellationToken);
}

/// <summary>Posições dos passos no pipeline (<c>whatsapp_handler.py</c>).</summary>
public static class StepOrder
{
    public const int EventReply = 30;
    public const int RouteRequest = 40;
    public const int Checkin = 50;
    public const int Reports = 55;
    public const int Help = 56;
    public const int TransactionReply = 60;
    public const int BareCancel = 70;
    public const int Payment = 80;
    public const int CoachKey = 57;
}

/// <summary>Dados que uma intenção recebe.</summary>
public sealed record IntentContext(WhatsAppUser User, string Number, string Message, MediaPayload? Media);

/// <summary>
/// Intenção do Gemini. <see cref="Swallows"/> = intenção "template" (<c>BaseIntent.handle</c>): qualquer exceção vira
/// <see cref="WhatsAppTexts.IntentFailed"/>; as "inline" deixam a exceção subir (cota do Gemini ⇒ 429).
/// </summary>
public interface IWhatsAppIntent
{
    string Name { get; }

    bool Swallows { get; }

    Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken);
}

/// <summary>Despacho por nome exato (acentos e maiúsculas contam); desconhecida ⇒ "não entendi".</summary>
public sealed class IntentRouter
{
    private readonly Dictionary<string, IWhatsAppIntent> _intents;
    private readonly IWhatsAppDiagnostics _diagnostics;

    public IntentRouter(IEnumerable<IWhatsAppIntent> intents, IWhatsAppDiagnostics diagnostics)
    {
        _intents = intents.ToDictionary(intent => intent.Name, StringComparer.Ordinal);
        _diagnostics = diagnostics;
    }

    public IReadOnlyCollection<string> Names => _intents.Keys;

    public async Task<string> DispatchAsync(string? intentName, IntentContext context, CancellationToken cancellationToken)
    {
        if (intentName is null || !_intents.TryGetValue(intentName, out var intent))
        {
            return WhatsAppTexts.NotUnderstood;
        }

        if (!intent.Swallows)
        {
            return await intent.HandleAsync(context, cancellationToken);
        }

        try
        {
            return await intent.HandleAsync(context, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _diagnostics.IntentFailed(intent.Name, ex.GetType().Name);
            return WhatsAppTexts.IntentFailed;
        }
    }
}

/// <summary><c>gemini_service.get_message_intent</c> (cache <c>intent</c>, 7 dias, global).</summary>
public sealed class IntentClassifier(ILlmClient llm)
{
    public async Task<string?> ClassifyAsync(string message, int userId, CancellationToken cancellationToken)
    {
        var text = await llm.GenerateTextAsync(
            new LlmRequest(WhatsAppPrompts.Intent(message), userId, LlmCachePolicies.Intent), cancellationToken);
        if (string.IsNullOrEmpty(text))
        {
            throw new LlmException("Falha na classificação da intenção: Resposta vazia do Gemini.");
        }

        var json = LlmJson.Parse(text);
        if (json.ValueKind != JsonValueKind.Object)
        {
            // intent_data.get(...) num valor que não é dicionário quebra no Python.
            throw new InvalidOperationException("Resposta de intenção não é um objeto.");
        }

        return json.TryGetProperty("intent", out var intent) && intent.ValueKind == JsonValueKind.String ? intent.GetString() : null;
    }
}

/// <summary>
/// Orquestrador do webhook (spec 012 §4): sanitiza, identifica o usuário, roda os passos em ordem e, se nenhum respondeu,
/// classifica a intenção e a executa. Exceções sobem para o endpoint (429 pela cota, 500 pelo resto).
/// </summary>
public sealed class HandleWhatsAppMessage(
    IWhatsAppUserLookup users,
    IEnumerable<IWhatsAppStep> steps,
    IntentClassifier classifier,
    IntentRouter router)
{
    private readonly IReadOnlyList<IWhatsAppStep> _steps = [.. steps.OrderBy(step => step.Order)];

    public async Task<string> ExecuteAsync(WebhookInput input, CancellationToken cancellationToken)
    {
        var message = InputSanitizer.Sanitize(input.Text, 1000, allowSpecialChars: true) ?? string.Empty;
        var number = InputSanitizer.Sanitize(input.RawNumber.Split('@')[0], 20, allowSpecialChars: false) ?? string.Empty;

        var user = await users.FindByNumberAsync(number, cancellationToken);
        if (user is null)
        {
            return WhatsAppTexts.NotRegistered;
        }

        var context = new PipelineContext(user, number, message, MessageIntent.Lower(message), input.Media);
        foreach (var step in _steps)
        {
            var reply = await step.HandleAsync(context, cancellationToken);
            if (reply is not null)
            {
                return reply;
            }
        }

        var intent = await classifier.ClassifyAsync(message, user.Id, cancellationToken);
        return await router.DispatchAsync(intent, new IntentContext(user, number, message, input.Media), cancellationToken);
    }
}
