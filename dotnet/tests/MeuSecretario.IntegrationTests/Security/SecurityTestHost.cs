using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Security;

/// <summary>
/// <c>IStartupFilter</c> de teste (T5): fixa o <c>RemoteIpAddress</c> a partir do header <c>X-Test-Remote</c> (o
/// <c>TestServer</c> não tem IP de origem) e simula rotas "registradas" com o endpoint já resolvido, que o
/// <c>EndpointRoutingMiddleware</c> respeita. Roda antes de todo o pipeline do app.
/// </summary>
internal sealed class TestRemoteIpStartupFilter(IReadOnlyCollection<string> fakeRegisteredPaths) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            if (context.Request.Headers.TryGetValue("X-Test-Remote", out var remote))
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remote.ToString());
                context.Request.Headers.Remove("X-Test-Remote");
            }

            if (fakeRegisteredPaths.Contains(context.Request.Path.Value ?? string.Empty))
            {
                context.SetEndpoint(new Endpoint(
                    ctx => ctx.Response.WriteAsync("alcancou o handler"),
                    EndpointMetadataCollection.Empty,
                    "rota-registrada-de-teste"));
            }

            await nextMiddleware();
        });
        next(app);
    };
}

/// <summary>API real em memória com Redis de teste, IP de origem controlável e banco substituído por um fake.</summary>
internal sealed class SecurityTestHost : IAsyncDisposable
{
    public const string Public = "203.0.113.50";
    public const string Private = "10.0.0.9";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly ConnectionMultiplexer _redis;

    public SecurityTestHost(
        string redisUrl,
        string redisConnection,
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? services = null,
        IReadOnlyCollection<string>? fakeRegisteredPaths = null)
    {
        _redis = ConnectionMultiplexer.Connect(redisConnection);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DATABASE_URL", "postgresql://u:p@localhost:5432/db");
            builder.UseSetting("API_SECRET_KEY", AuthFixtures.JwtSecret);
            builder.UseSetting("ENCRYPTION_KEY", AuthFixtures.FernetKey);
            builder.UseSetting("GEMINI_API_KEY", "gemini-de-teste");
            builder.UseSetting("REDIS_URL", redisUrl);
            builder.UseSetting("LOG_TO_FILE", "false");
            foreach (var (key, value) in settings ?? new Dictionary<string, string>())
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(collection =>
            {
                collection.RemoveAll<IDatabaseHealth>();
                collection.AddSingleton<IDatabaseHealth>(new UpDatabase());
                collection.AddSingleton<IStartupFilter>(new TestRemoteIpStartupFilter(fakeRegisteredPaths ?? []));
                services?.Invoke(collection);
            });
        });
    }

    public IServiceProvider Services => _factory.Services;

    public IDatabase Db => _redis.GetDatabase();

    public async Task Flush() => await _redis.GetServer(_redis.GetEndPoints()[0]).FlushDatabaseAsync();

    public async Task<(HttpResponseMessage Response, string Body)> Send(
        string path,
        string? remote = null,
        string? forwardedFor = null,
        string? userAgent = null,
        HttpMethod? method = null)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));
        if (remote is not null)
        {
            request.Headers.Add("X-Test-Remote", remote);
        }

        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        if (userAgent is not null)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        var response = await client.SendAsync(request);
        return (response, await response.Content.ReadAsStringAsync());
    }

    /// <summary>Requisição externa: o proxy privado do nginx entrega com <c>X-Forwarded-For: ip</c>.</summary>
    public Task<(HttpResponseMessage Response, string Body)> SendFrom(
        string clientIp, string path, string? userAgent = null, HttpMethod? method = null) =>
        Send(path, Private, clientIp, userAgent, method);

    /// <summary>Chaves <c>security:*</c> existentes.</summary>
    public Task<IReadOnlyList<string>> SecurityKeys()
    {
        var server = _redis.GetServer(_redis.GetEndPoints()[0]);
        return Task.FromResult<IReadOnlyList<string>>([.. server.Keys(pattern: "security:*").Select(k => k.ToString()).Order(StringComparer.Ordinal)]);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _redis.DisposeAsync();
    }

    private sealed class UpDatabase : IDatabaseHealth
    {
        public Task<DatabaseStatus> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(new DatabaseStatus(true));
    }
}

/// <summary><see cref="ISecurityStore"/> que sempre falha, para o teste de fail-open do filtro.</summary>
internal sealed class ThrowingSecurityStore : ISecurityStore
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<bool> IsBlacklistedAsync(string ip, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("segredo-do-store");

    public Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<bool> RemoveTempBlockAsync(string ip, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");

    public Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken cancellationToken) => throw new InvalidOperationException("segredo-do-store");
}


