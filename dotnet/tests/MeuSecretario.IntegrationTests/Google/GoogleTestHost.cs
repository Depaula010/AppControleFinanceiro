using System.Net;
using System.Text;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using GoogleHttp = global::Google.Apis.Http;

namespace MeuSecretario.IntegrationTests.Google;

public sealed record SentRequest(HttpMethod Method, string Url, string? Body);

/// <summary>Backend falso do Google: enfileira respostas e registra cada requisição (método, URL, corpo).</summary>
public sealed class FakeGoogleBackend : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
    private readonly List<SentRequest> _requests = [];
    private readonly object _gate = new();

    public IReadOnlyList<SentRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return [.. _requests];
            }
        }
    }

    public FakeGoogleBackend Enqueue(HttpStatusCode status, string body)
    {
        lock (_gate)
        {
            _responses.Enqueue((status, body));
        }

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        (HttpStatusCode Status, string Body) response;
        lock (_gate)
        {
            _requests.Add(new SentRequest(request.Method, request.RequestUri!.ToString(), body));
            response = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
        }

        return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Body, Encoding.UTF8, "application/json") };
    }
}

public sealed class FakeGoogleFactory(FakeGoogleBackend backend) : GoogleHttp.IHttpClientFactory
{
    public GoogleHttp.ConfigurableHttpClient CreateHttpClient(GoogleHttp.CreateHttpClientArgs args)
    {
        var client = new GoogleHttp.ConfigurableHttpClient(new GoogleHttp.ConfigurableMessageHandler(backend));
        foreach (var initializer in args.Initializers)
        {
            initializer.Initialize(client);
        }

        return client;
    }
}

public sealed class FakeBot : IBotClient
{
    private readonly List<(string Number, string Message)> _sent = [];

    public bool Succeeds { get; set; } = true;

    public IReadOnlyList<(string Number, string Message)> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add((number, message));
        }

        return Task.FromResult(Succeeds);
    }

    public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken) =>
        Task.FromResult(Succeeds);
}

/// <summary>Cache neutro (como o Redis fora do ar): as rotas do Google só o usam na notificação de token revogado.</summary>
public sealed class NeutralCache : ICache
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) => Task.FromResult(default(T));

    public Task<System.Text.Json.JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult<System.Text.Json.JsonElement?>(null);

    public Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

/// <summary>API real em memória com PostgreSQL de teste e fakes de Google, bot e cache.</summary>
public sealed class GoogleTestHost : IAsyncDisposable
{
    public const string RedirectUri = "https://h.exemplo/oauth2callback";
    public const string Calendar = "https://www.googleapis.com/auth/calendar";
    public const string Drive = "https://www.googleapis.com/auth/drive.file";

    private readonly WebApplicationFactory<Program> _factory;

    public GoogleTestHost(
        PostgresFixture postgres,
        bool oauthConfigured = true,
        string? databaseUrl = null,
        string? legacyUntil = null)
    {
        _factory = postgres.Api.WithWebHostBuilder(builder =>
        {
            if (databaseUrl is not null)
            {
                builder.UseSetting("DATABASE_URL", databaseUrl);
            }

            if (oauthConfigured)
            {
                builder.UseSetting("GOOGLE_CLIENT_ID", "client-id-teste");
                builder.UseSetting("GOOGLE_CLIENT_SECRET", "client-secret-teste");
            }

            builder.UseSetting("GOOGLE_REDIRECT_URI", RedirectUri);
            if (legacyUntil is not null)
            {
                builder.UseSetting("GOOGLE_CONNECT_LEGACY_UNTIL", legacyUntil);
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<GoogleHttp.IHttpClientFactory>();
                services.AddSingleton<GoogleHttp.IHttpClientFactory>(new FakeGoogleFactory(Google));
                services.RemoveAll<IBotClient>();
                services.AddSingleton<IBotClient>(Bot);
                // O Serilog do host ignora provedores extras: o ILogger<T> é trocado pelo coletor (como na 008).
                services.AddSingleton(Logs);
                services.AddSingleton(typeof(ILogger<>), typeof(CapturingLogger<>));
                services.RemoveAll<ICache>();
                services.AddSingleton<ICache, NeutralCache>();
            });
        });
    }

    public FakeGoogleBackend Google { get; } = new();

    public FakeBot Bot { get; } = new();

    public LogSink Logs { get; } = new();

    public IServiceProvider Services => _factory.Services;

    public HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public async Task<(HttpResponseMessage Response, string Body)> Get(string path)
    {
        using var client = CreateClient();
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        return (response, await response.Content.ReadAsStringAsync());
    }

    public async ValueTask DisposeAsync()
    {
        Google.Dispose();
        await _factory.DisposeAsync();
    }
}

internal static class GoogleDb
{
    public static async Task<int> SeedUser(PostgresFixture postgres, string name = "Maria", string? whatsapp = "5511999990001")
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp) VALUES (@name, @whatsapp) RETURNING id", new { name, whatsapp });
    }

    public static async Task SeedTokens(
        PostgresFixture postgres,
        int userId,
        bool withDrive = true,
        string access = "acc-1",
        string? refresh = "ref-1",
        TimeSpan? expiresIn = null,
        bool needsReconnect = false)
    {
        var scopes = withDrive
            ? $"[\"{GoogleTestHost.Calendar}\", \"{GoogleTestHost.Drive}\"]"
            : $"[\"{GoogleTestHost.Calendar}\"]";
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            INSERT INTO googlecalendartokens (usuario_id, access_token, refresh_token, token_expiry, scopes, needs_reconnect)
            VALUES (@userId, @access, @refresh, @expiry, @scopes, @needsReconnect)
            """,
            new { userId, access, refresh, expiry = DateTime.UtcNow + (expiresIn ?? TimeSpan.FromHours(1)), scopes, needsReconnect });
    }

    public static async Task<T> Scalar<T>(PostgresFixture postgres, string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    public static Task<int> RowCount(PostgresFixture postgres) =>
        Scalar<int>(postgres, "SELECT count(*) FROM googlecalendartokens");
}

/// <summary>Guarda cada mensagem de log (com a exceção em texto) para o teste procurar segredos (AC-010-89).</summary>
public sealed class LogSink
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    internal void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }
    }
}

public sealed class CapturingLogger<T>(LogSink sink) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        sink.Add($"{typeof(T).Name} [{eventId.Id}] {formatter(state, exception)} {exception}");
}
