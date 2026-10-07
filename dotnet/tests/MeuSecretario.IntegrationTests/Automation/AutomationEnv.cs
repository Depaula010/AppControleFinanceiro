using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.IntegrationTests.Admin;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.IntegrationTests.Transactions;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MeuSecretario.Application.Automation;

namespace MeuSecretario.IntegrationTests.Automation;

/// <summary>Gemini com respostas roteirizadas (uma por chamada) que guarda o que recebeu.</summary>
internal sealed class ScriptedLlm : ILlmClient
{
    private readonly Queue<Func<LlmRequest, string>> _responses = new();

    public List<LlmRequest> Requests { get; } = [];

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
        return Task.FromResult(_responses.Count > 0 ? _responses.Dequeue()(request) : throw new InvalidOperationException("Gemini não roteirizado"));
    }
}

internal sealed class RecordingBot : IBotClient
{
    public List<(string Number, string Message)> Sent { get; } = [];

    public Task<bool> SendTextAsync(string number, string message, CancellationToken cancellationToken)
    {
        Sent.Add((number, message));
        return Task.FromResult(true);
    }

    public Task<bool> SendImageAsync(string number, ReadOnlyMemory<byte> image, string? caption, CancellationToken cancellationToken) =>
        Task.FromResult(true);
}

/// <summary>Cache em memória com liga e desliga da disponibilidade e da gravação (o Redis do teste nunca é tocado).</summary>
internal sealed class MemoryCache : ICache
{
    private readonly Dictionary<string, string> _values = [];

    public bool Available { get; set; } = true;

    public bool FailWrites { get; set; }

    public IReadOnlyDictionary<string, string> Values => _values;

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

    public Task<IReadOnlyList<string>> KeysByPatternAsync(string pattern, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class FixedPendingId(string id) : IPendingIdGenerator
{
    public string NewId() => id;
}

/// <summary>A API real com Gemini, bot e Redis falsos e o banco do contêiner; dados semeados por SQL.</summary>
internal sealed class AutomationEnv
{
    public const string UserKey = "chave-do-atalho-0001";
    public const string Whatsapp = "5511999991072";
    public const string PendingId = "ab12cd34";

    public static readonly string ApiKey = AuthFixtures.JwtSecret;

    private readonly PostgresFixture _postgres;

    public AutomationEnv(PostgresFixture postgres)
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
            services.RemoveAll<IPendingIdGenerator>();
            services.AddSingleton<IPendingIdGenerator>(new FixedPendingId(PendingId));
        }));
    }

    public CapturedLogs Logs { get; } = new();

    public ScriptedLlm Llm { get; } = new();

    public RecordingBot Bot { get; } = new();

    public MemoryCache Cache { get; } = new();

    public TransactionsTestData Data { get; }

    public WebApplicationFactory<Program> Factory { get; }

    public DateOnly Today => Factory.Services.GetRequiredService<IClock>().Today;

    /// <summary>Corpo como texto exato; <paramref name="apiKey"/> vai no cabeçalho <c>X-API-KEY</c> quando informado.</summary>
    public async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, string? apiKey = null, string contentType = "application/json")
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, contentType);
        }

        if (apiKey is not null)
        {
            request.Headers.TryAddWithoutValidation("X-API-KEY", apiKey);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<(HttpStatusCode Status, string Body)> Post(string path, string? json) => SendAsync(HttpMethod.Post, path, json);

    public async Task<int> SeedUserAsync(string whatsapp = Whatsapp, string? key = UserKey, bool active = true, int months = 6)
    {
        await using var connection = await _postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO usuarios (nome, numero_whatsapp, api_key_automate, ativo, meses_reserva_emergencia)
            VALUES ('Ana', @whatsapp, @key, @active, @months) RETURNING id
            """,
            new { whatsapp, key, active, months });
    }

    public async Task<int> InsertScheduleAsync(
        int userId,
        int accountId,
        string description,
        string type = "FIXO",
        string periodicity = "MENSAL",
        decimal? amount = 100.00m,
        int day = 10,
        bool active = true,
        bool? includeInReserve = true,
        int subcategoryId = TransactionsTestData.ExpenseSubcategory,
        int? total = null,
        int executed = 0,
        int? month = null)
    {
        await using var connection = await _postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            """
            INSERT INTO agendamentos (usuario_id, conta_id, subcategoria_id, descricao, valor_previsto, tipo_agendamento, periodicidade,
                                      data_inicio, dia_execucao, mes_execucao, total_parcelas, parcelas_executadas, incluir_na_reserva, ativo)
            VALUES (@userId, @accountId, @subcategoryId, @description, @amount, @type, @periodicity,
                    DATE '2026-01-01', @day, @month, @total, @executed, @includeInReserve, @active)
            RETURNING id
            """,
            new { userId, accountId, subcategoryId, description, amount, type, periodicity, day, month, total, executed, includeInReserve, active });
    }

    public async Task<int> InsertPotAsync(int userId, string name, decimal limit, string periodicity, params int[] subcategories)
    {
        await using var connection = await _postgres.DataSource.OpenConnectionAsync();
        var id = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO potesdegastos (usuario_id, nome_pote, valor_limite, periodicidade) VALUES (@userId, @name, @limit, @periodicity) RETURNING id",
            new { userId, name, limit, periodicity });
        foreach (var subcategoryId in subcategories)
        {
            await connection.ExecuteAsync(
                "INSERT INTO potesubcategorias (pote_id, subcategoria_id) VALUES (@id, @subcategoryId)", new { id, subcategoryId });
        }

        return id;
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? parameters = null) => Data.QueryAsync<T>(sql, parameters);

    public Task<T> ScalarAsync<T>(string sql, object? parameters = null) => Data.ScalarAsync<T>(sql, parameters);

    public static string Json(object value) => JsonSerializer.Serialize(value);
}
