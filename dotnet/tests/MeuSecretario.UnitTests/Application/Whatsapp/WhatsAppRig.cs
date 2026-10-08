using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>Gemini com respostas roteirizadas (uma por chamada) que guarda o que recebeu.</summary>
internal sealed class ScriptedLlm : ILlmClient
{
    private readonly Queue<Func<LlmRequest, string>> _responses = new();

    public List<LlmRequest> Requests { get; } = [];

    public Action<string>? OnCall { get; set; }

    public ScriptedLlm Then(string text)
    {
        _responses.Enqueue(_ => text);
        return this;
    }

    public ScriptedLlm ThenThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        OnCall?.Invoke("gemini_service.get_message_intent");
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue()(request) : throw new InvalidOperationException("Gemini não roteirizado"));
    }
}

internal sealed class FakeUsers(bool exists, List<string> calls) : IWhatsAppUserLookup
{
    public Task<WhatsAppUser?> FindByNumberAsync(string number, CancellationToken cancellationToken)
    {
        calls.Add("user_service.check_user_exists:" + number);
        return Task.FromResult(exists ? new WhatsAppUser(7, "Ana") : null);
    }
}

internal sealed class RecordingCheckinResponder(InMemoryCache cache, List<string> calls, string reply) : INightlyCheckinResponder
{
    public async Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken)
    {
        calls.Add("NightlyCheckinService.process_response");
        await cache.DeleteAsync("nightly_checkin_active:" + number, cancellationToken);
        return reply;
    }
}

internal sealed class NullDiagnostics : IWhatsAppDiagnostics
{
    public List<(string Intent, string Error)> Failures { get; } = [];

    public void IntentFailed(string intent, string errorType) => Failures.Add((intent, errorType));
}

/// <summary>Intenção que só responde um texto fixo (para testar o despacho).</summary>
internal sealed class FixedIntent(string name, string reply, bool swallows = true) : IWhatsAppIntent
{
    public string Name => name;

    public bool Swallows => swallows;

    public Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) => Task.FromResult(reply);
}

internal sealed class ThrowingIntent(string name, Exception exception, bool swallows) : IWhatsAppIntent
{
    public string Name => name;

    public bool Swallows => swallows;

    public Task<string> HandleAsync(IntentContext context, CancellationToken cancellationToken) => throw exception;
}

/// <summary>Pipeline montado com o cache em memória e as lojas reais da Infrastructure; os passos e as intenções entram por parâmetro.</summary>
internal sealed class WhatsAppRig
{
    public const string Number = "5531999990001";

    public WhatsAppRig(bool userExists = true, IEnumerable<IWhatsAppIntent>? extraIntents = null, string checkinReply = "RESPOSTA-DO-CHECKIN")
    {
        Cache = new InMemoryCache(new FakeTimeProvider());
        Users = new FakeUsers(userExists, Calls);
        Diagnostics = new NullDiagnostics();
        Llm = new ScriptedLlm { OnCall = Calls.Add };
        TransactionState = new PendingTransactionState(Cache);
        Events = new PendingEventStore(Cache, new FixedIds());
        CheckinState = new CheckinSessionState(Cache);

        var steps = new List<IWhatsAppStep>
        {
            new CheckinStep(CheckinState, new RecordingCheckinResponder(Cache, Calls, checkinReply)),
            new BareCancelStep(TransactionState, Events),
        };
        var intents = new List<IWhatsAppIntent> { new HelpIntent() };
        intents.AddRange(extraIntents ?? []);

        Router = new IntentRouter(intents, Diagnostics);
        UseCase = new HandleWhatsAppMessage(Users, steps, new IntentClassifier(Llm), Router);
    }

    public List<string> Calls { get; } = [];

    public InMemoryCache Cache { get; }

    public FakeUsers Users { get; }

    public NullDiagnostics Diagnostics { get; }

    public ScriptedLlm Llm { get; }

    public PendingTransactionState TransactionState { get; }

    public PendingEventStore Events { get; }

    public CheckinSessionState CheckinState { get; }

    public IntentRouter Router { get; }

    public HandleWhatsAppMessage UseCase { get; }

    public Task<string> SendAsync(string text, string rawNumber = Number + "@s.whatsapp.net", MediaPayload? media = null) =>
        UseCase.ExecuteAsync(new WebhookInput(text, rawNumber, media), CancellationToken.None);

    public async Task SeedAsync(string key, JsonNode? value) =>
        await Cache.SetJsonAsync(key, value, TimeSpan.FromSeconds(300));

    private sealed class FixedIds : IPendingIdGenerator
    {
        public string NewId() => "ab12cd34";
    }
}

/// <summary>Cenários do harness (tests/Fixtures/whatsapp_handler_harness.py): o handler real do Flask com serviços roteirizados.</summary>
internal static class PipelineScenarios
{
    private static readonly JsonElement Root = Load();

    public static IEnumerable<JsonElement> All(string layer, params string[] groups) =>
        Root.GetProperty("scenarios").EnumerateArray()
            .Where(s => s.GetProperty("layer").GetString() == layer && groups.Contains(s.GetProperty("group").GetString()));

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-pipeline-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}
