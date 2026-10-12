using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Google;
using MeuSecretario.Application.Settings;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.Infrastructure.Whatsapp;
using MeuSecretario.UnitTests.Application.Google;
using MeuSecretario.UnitTests.Domain.Whatsapp;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

/// <summary>Gemini com respostas em fila que guarda o que recebeu.</summary>
internal sealed class RecordingLlm : ILlmClient
{
    private readonly Queue<Func<string>> _responses = new();

    public List<LlmRequest> Requests { get; } = [];

    public RecordingLlm Then(string text)
    {
        _responses.Enqueue(() => text);
        return this;
    }

    public RecordingLlm ThenThrow(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue()() : throw new InvalidOperationException("Gemini não roteirizado"));
    }
}

internal sealed class FixedPendingIds(string id) : IPendingIdGenerator
{
    public string NewId() => id;
}

/// <summary>Peças comuns dos testes de calendário: relógio congelado, cliente do Google falso e endereços.</summary>
internal sealed class CalendarRig
{
    public const string Number = "5531999990001";

    public CalendarRig(string now, JsonElement? world = null, int addressCount = 0)
    {
        Time = new FakeTimeProvider();
        Time.SetUtcNow(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
        Clock = new BrazilClock(Time);
        Client = new FixtureCalendarClient(world ?? EmptyWorld(), Time);
        Cache = new InMemoryCache(Time);
        Events = new PendingEventStore(Cache, new FixedPendingIds("ab12cd34"));
        Addresses = Substitute.For<IFavoriteAddressRepository>();
        Addresses.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            [.. Enumerable.Range(1, addressCount).Select(i => new AddressRow(i, i == 1 ? "casa" : "outro", "Rua A, 10", -19.9m, -43.9m))]);
        UserAddresses = new UserAddresses(Addresses);
        Llm = new RecordingLlm();
    }

    public FakeTimeProvider Time { get; }

    public BrazilClock Clock { get; }

    public FixtureCalendarClient Client { get; }

    public InMemoryCache Cache { get; }

    public PendingEventStore Events { get; }

    public IFavoriteAddressRepository Addresses { get; }

    public UserAddresses UserAddresses { get; }

    public RecordingLlm Llm { get; }

    public WhatsAppUser User { get; } = new(7, "Ana");

    public IntentContext Context(string message = "mensagem") => new(User, Number, message, null);

    public static JsonElement EmptyWorld() => JsonDocument.Parse("[]").RootElement.Clone();

    public static GoogleLinks Links(string redirectUri, DateTimeOffset now)
    {
        var settings = GoogleTestSupport.Settings(redirectUri: redirectUri);
        var time = new FakeTimeProvider();
        time.SetUtcNow(now);
        return new GoogleLinks(settings, new ConnectLinkTokenCodec(settings, time));
    }

    public static IGoogleAccountService Account(bool connected)
    {
        var account = Substitute.For<IGoogleAccountService>();
        account.IsConnectedAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(connected);
        return account;
    }

    /// <summary>O link assinado leva <c>?t=...</c>; o Flask não assinava (desvio 053): a comparação ignora o token.</summary>
    public static string WithoutToken(string text) => Regex.Replace(text, @"\?t=[^\s]+", string.Empty);

    /// <summary>Uma chamada do Python (<c>["list", cal, min, max, None, None]</c>) no formato do <see cref="FixtureCalendarClient"/>.</summary>
    public static string CallText(JsonElement call) =>
        string.Join("|", call.EnumerateArray().Select(part => part.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => part.GetString()!,
            _ => part.GetRawText(),
        }));

    /// <summary>O "mundo" com os eventos de <paramref name="history"/> acrescentados ao primeiro calendário (como o Python).</summary>
    public static JsonElement WithHistory(JsonElement world, JsonElement? history)
    {
        if (history is not { } extra || extra.ValueKind != JsonValueKind.Array)
        {
            return world;
        }

        var nodes = JsonNode.Parse(world.GetRawText())!.AsArray();
        var events = nodes[0]!["events"]!.AsArray();
        foreach (var item in extra.EnumerateArray())
        {
            events.Add(JsonNode.Parse(item.GetRawText()));
        }

        return JsonDocument.Parse(nodes.ToJsonString()).RootElement.Clone();
    }

    public static IReadOnlyList<JsonElement> Cases(string section) => WhatsAppFixture.Cases(section);
}
