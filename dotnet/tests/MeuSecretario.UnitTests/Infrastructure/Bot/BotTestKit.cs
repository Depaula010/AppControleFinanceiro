using System.Net;
using System.Text;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.UnitTests.Infrastructure.Bot;

internal sealed class FakeIntegrationSettings : IIntegrationSettings
{
    public string GeminiApiKey { get; set; } = string.Empty;

    public string GeminiModel { get; set; } = string.Empty;

    public string ApiSecretKey { get; set; } = "segredo-da-api";

    public string BotWhatsappUrl { get; set; } = "http://bot.test:3000";

    public string BotApiKey { get; set; } = "chave-do-bot";

    public string BotSessionName { get; set; } = "sessao-principal";

    public string BotSessionId { get; set; } = string.Empty;

    public string GoogleClientId { get; set; } = string.Empty;

    public string GoogleClientSecret { get; set; } = string.Empty;

    public string GoogleRedirectUri { get; set; } = string.Empty;

    public string WeatherApiKey { get; set; } = string.Empty;

    public string OpenRouteApiKey { get; set; } = string.Empty;

    public string OpenRouteBaseUrl { get; set; } = string.Empty;

    public int TravelTimeDailyLimit { get; set; }
}

/// <summary>Fábrica que devolve sempre o mesmo handler fake e registra o nome do cliente pedido.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public List<string> ClientNames { get; } = [];

    public HttpClient CreateClient(string name)
    {
        lock (ClientNames)
        {
            ClientNames.Add(name);
        }

        return new HttpClient(handler, disposeHandler: false);
    }
}

internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.. _messages];
            }
        }
    }

    public string All => string.Join('\n', Messages);

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_messages)
        {
            _messages.Add(formatter(state, exception) + (exception is null ? string.Empty : " | " + exception));
        }
    }
}

/// <summary>Monta o bot com um servidor fake roteado por método e caminho.</summary>
internal sealed class BotRig
{
    public const string SessionsPath = "/api/v1/sessions";

    public BotRig()
    {
        Factory = new StubHttpClientFactory(Handler);
        Resolver = new BotSessionResolver(Factory, Cache, Settings, ResolverLog);
        Client = new BotClient(Factory, Resolver, Cache, Settings, ClientLog);
    }

    public FakeHttpHandler Handler { get; } = new();

    public InMemoryCache Cache { get; } = new();

    public FakeIntegrationSettings Settings { get; } = new();

    public StubHttpClientFactory Factory { get; }

    public ListLogger<BotSessionResolver> ResolverLog { get; } = new();

    public ListLogger<BotClient> ClientLog { get; } = new();

    public BotSessionResolver Resolver { get; }

    public BotClient Client { get; }

    public string Logs => ResolverLog.All + "\n" + ClientLog.All;

    public IEnumerable<RecordedRequest> Gets => Handler.Requests.Where(r => r.Method == HttpMethod.Get);

    public IEnumerable<RecordedRequest> Posts => Handler.Requests.Where(r => r.Method == HttpMethod.Post);

    public static string SessionsBody(params (string Name, string Id)[] sessions) =>
        "{\"status\":\"success\",\"data\":[" + string.Join(",", sessions.Select(s => $"{{\"session_id\":\"{s.Id}\",\"session_name\":\"{s.Name}\"}}")) + $"],\"count\":{sessions.Length}}}";

    /// <summary>Servidor fake: responde às sessões e ao POST pelo mapa de status (a última resposta de cada rota se repete).</summary>
    public BotRig Serve(Func<RecordedRequest, HttpResponseMessage> route)
    {
        Handler.Enqueue(route);
        return this;
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
