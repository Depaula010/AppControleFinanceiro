using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.IntegrationTests.Settings;

/// <summary>Cliente HTTP, semeadura e leitura direta do banco para os testes da feature 007.</summary>
internal sealed class SettingsEnv(PostgresFixture postgres, WebApplicationFactory<Program>? factory = null)
{
    public const string Sentinel = "SENTINELA-segredo-7741";

    public static string Token(int userId) => new JwtTokenService(AuthFixtures.JwtSecret, TimeProvider.System).Issue(userId);

    public static string Error(string message) => $$"""{"status":"error","message":"{{message}}"}""";

    public static string Success(string message) => $$"""{"status":"success","message":"{{message}}"}""";

    public static string Data(string json) => $$"""{"status":"success","data":{{json}}}""";

    public static string DataMsg(string message, string json) => $$"""{"status":"success","message":"{{message}}","data":{{json}}}""";

    public async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, int? userId = null, string? authorization = null)
    {
        using var client = (factory ?? postgres.Api).CreateClient();
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }
        else if (userId is { } id)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(id));
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public Task<(HttpStatusCode Status, string Body)> Get(int userId, string path) => SendAsync(HttpMethod.Get, path, userId: userId);

    public Task<(HttpStatusCode Status, string Body)> Put(int userId, string path, string? json) => SendAsync(HttpMethod.Put, path, json, userId);

    public Task<(HttpStatusCode Status, string Body)> Post(int userId, string path, string? json) => SendAsync(HttpMethod.Post, path, json, userId);

    public Task<(HttpStatusCode Status, string Body)> Delete(int userId, string path) => SendAsync(HttpMethod.Delete, path, userId: userId);

    public async Task<int> InsertUserAsync(string whatsapp = "5511900000001", string nome = "Ana Souza", string? email = null, bool ativo = true)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return await connection.ExecuteScalarAsync<int>(
            "INSERT INTO usuarios (nome, numero_whatsapp, email, ativo) VALUES (@nome, @whatsapp, @email, @ativo) RETURNING id",
            new { nome, whatsapp, email, ativo });
    }

    public async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, args);
    }

    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args = null)
    {
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        return (await connection.QueryAsync<T>(sql, args)).AsList();
    }

    public IEncryptionService Encryption => postgres.Api.Services.GetRequiredService<IEncryptionService>();
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Messages)
        {
            Messages.Add(formatter(state, exception));
        }
    }
}
