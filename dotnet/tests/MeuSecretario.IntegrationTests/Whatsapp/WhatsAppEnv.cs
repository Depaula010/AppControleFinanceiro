using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Automation;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.IntegrationTests.Admin;
using MeuSecretario.IntegrationTests.Automation;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Cache em memória com glob (<c>KEYS</c>), liga e desliga de disponibilidade e de gravação.</summary>
internal sealed class PatternCache : ICache
{
    private readonly Dictionary<string, string> _values = [];

    public bool Available { get; set; } = true;

    public bool FailWrites { get; set; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public void Clear()
    {
        _values.Clear();
        Available = true;
        FailWrites = false;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Available);

    public Task<bool> SetJsonAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
    {
        if (!Available || FailWrites)
        {
            return Task.FromResult(false);
        }

        _values[key] = JsonSerializer.Serialize(value);
        return Task.FromResult(true);
    }

    public Task<T?> GetJsonAsync<T>(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(key, out var raw) ? JsonSerializer.Deserialize<T>(raw) : default);

    public Task<JsonElement?> GetJsonElementAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_values.TryGetValue(key, out var raw) ? (JsonElement?)JsonDocument.Parse(raw).RootElement : null);

    public Task<bool> SetIfNotExistsAsync(string key, string rawValue, TimeSpan ttl, CancellationToken cancellationToken = default) =>
        Task.FromResult(Available && _values.TryAdd(key, rawValue));

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.ContainsKey(key));

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.Remove(key));

    public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default)
    {
        var regex = new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$");
        return Task.FromResult<IReadOnlyList<string>>([.. _values.Keys.Where(key => regex.IsMatch(key))]);
    }
}

/// <summary>Gemini com respostas roteirizadas (uma por chamada) que guarda o que recebeu e pode ser zerado entre cenários.</summary>
internal sealed class ScriptedGemini : ILlmClient
{
    private readonly Queue<Func<LlmRequest, string>> _responses = new();

    public List<LlmRequest> Requests { get; } = [];

    public ScriptedGemini Then(string text)
    {
        _responses.Enqueue(_ => text);
        return this;
    }

    public ScriptedGemini ThenThrow(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    public void Reset()
    {
        _responses.Clear();
        Requests.Clear();
    }

    public Task<string> GenerateTextAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue()(request) : throw new InvalidOperationException("Gemini não roteirizado"));
    }
}

internal sealed class ToggleDatabaseHealth : IDatabaseHealth
{
    public bool Connected { get; set; } = true;

    public Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(new DatabaseStatus(Connected));
}

internal sealed class ScriptedCheckinResponder : INightlyCheckinResponder
{
    private readonly ICache _cache;

    public ScriptedCheckinResponder(ICache cache) => _cache = cache;

    public string Reply { get; set; } = "RESPOSTA-DO-CHECKIN";

    public List<string> Calls { get; } = [];

    public async Task<string?> RespondAsync(string number, string message, string checkinId, CancellationToken cancellationToken)
    {
        Calls.Add("NightlyCheckinService.process_response");
        await _cache.DeleteAsync("nightly_checkin_active:" + number, cancellationToken);
        return Reply;
    }
}

/// <summary>A API real com Gemini, bot, Redis, saúde do banco e check-in falsos e o banco do contêiner; dados semeados por SQL.</summary>
internal sealed class WhatsAppEnv
{
    public const string Number = "5531999990001";

    private readonly PostgresFixture _postgres;

    public WhatsAppEnv(PostgresFixture postgres)
    {
        _postgres = postgres;
        Data = new TransactionsTestData(postgres.DataSource);
        Factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddCapturedLogs(Logs);
            services.RemoveAll<ILlmClient>();
            services.AddSingleton<ILlmClient>(Llm);
            services.RemoveAll<IBotClient>();
            services.AddSingleton<IBotClient>(Bot);
            services.RemoveAll<ICache>();
            services.AddSingleton<ICache>(Cache);
            services.RemoveAll<IDatabaseHealth>();
            services.AddSingleton<IDatabaseHealth>(Health);
            services.RemoveAll<INightlyCheckinResponder>();
            services.AddSingleton<INightlyCheckinResponder>(Checkin);
            services.RemoveAll<IPendingIdGenerator>();
            services.AddSingleton<IPendingIdGenerator>(new FixedPendingId("ab12cd34"));
        }));
    }

    public CapturedLogs Logs { get; } = new();

    public ScriptedGemini Llm { get; } = new();

    public RecordingBot Bot { get; } = new();

    public PatternCache Cache { get; } = new();

    public ToggleDatabaseHealth Health { get; } = new();

    public ScriptedCheckinResponder Checkin => _checkin ??= new ScriptedCheckinResponder(Cache);

    private ScriptedCheckinResponder? _checkin;

    public TransactionsTestData Data { get; }

    public WebApplicationFactory<Program> Factory { get; }

    public DateOnly Today => Factory.Services.GetRequiredService<IClock>().Today;

    public static readonly string ApiKey = AuthFixtures.JwtSecret;

    /// <summary>Corpo como bytes exatos; cabeçalhos informados são enviados como estão.</summary>
    public async Task<(HttpStatusCode Status, string Body)> PostAsync(
        byte[] body, IReadOnlyDictionary<string, string>? headers = null, string contentType = "application/json")
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<(HttpStatusCode Status, string Body)> PostJsonAsync(object payload, bool withKey = true) =>
        PostAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)),
            withKey ? new Dictionary<string, string> { ["x-api-key"] = ApiKey } : null);

    public async Task<int> SeedUserAsync(string whatsapp = Number, string name = "Ana")
    {
        await using var connection = await _postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp, ativo) VALUES (@name, @whatsapp, true) RETURNING id", new { name, whatsapp });
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) => Data.QueryAsync<T>(sql, parameters);

    public Task<T> ScalarAsync<T>(string sql, object? parameters = null) => Data.ScalarAsync<T>(sql, parameters);
}
