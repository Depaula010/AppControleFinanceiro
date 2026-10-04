using System.Collections.Concurrent;
using System.Net;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>Chamadas e respostas exatas das rotas <c>/admin/*</c> (envelope B).</summary>
internal static class AdminHttp
{
    public static readonly string ApiKey = AuthFixtures.JwtSecret;

    /// <summary>Marcador de "use a chave válida" (valor padrão de parâmetro precisa ser constante).</summary>
    public const string Valid = "<chave-válida>";

    public const string RedisDown = "redis://127.0.0.1:1";

    public static string Error(string mensagem) => $$"""{"status":"erro","mensagem":"{{mensagem}}"}""";

    public static string Success(string mensagem) => $$"""{"status":"sucesso","mensagem":"{{mensagem}}"}""";

    /// <summary>Chamada interna (sem IP de origem), com a chave informada (<c>null</c> = sem header).</summary>
    public static async Task<(HttpStatusCode Status, string Body)> Admin(
        this SecurityTestHost host,
        HttpMethod method,
        string path,
        string? json = null,
        string? key = Valid,
        string? remote = null,
        string? forwardedFor = null,
        string contentType = "application/json")
    {
        var (response, body) = await host.Call(method, path, json, Headers(key, remote, forwardedFor), contentType);
        return (response.StatusCode, body);
    }

    public static Task<(HttpStatusCode Status, string Body)> Post(
        this SecurityTestHost host, string path, string? json = null, string? key = Valid) =>
        host.Admin(HttpMethod.Post, path, json, key);

    public static Task<(HttpStatusCode Status, string Body)> Get(
        this SecurityTestHost host, string path, string? key = Valid) =>
        host.Admin(HttpMethod.Get, path, null, key);

    /// <summary>Chamada vinda de fora: o proxy privado do nginx entrega com <c>X-Forwarded-For: ip</c>.</summary>
    public static Task<(HttpStatusCode Status, string Body)> FromExternal(
        this SecurityTestHost host, string ip, HttpMethod method, string path, string? json = null, string? key = Valid) =>
        host.Admin(method, path, json, key, SecurityTestHost.Private, ip);

    private static Dictionary<string, string> Headers(string? key, string? remote, string? forwardedFor)
    {
        var headers = new Dictionary<string, string>();
        if (key is not null)
        {
            headers["X-API-KEY"] = key == Valid ? ApiKey : key;
        }

        if (remote is not null)
        {
            headers["X-Test-Remote"] = remote;
        }

        if (forwardedFor is not null)
        {
            headers["X-Forwarded-For"] = forwardedFor;
        }

        return headers;
    }
}

/// <summary>Mensagens de log capturadas (categoria, nível e texto formatado) para provar o que vai e o que não vai ao log.</summary>
internal sealed class CapturedLogs : ILoggerFactory
{
    private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries => [.. _entries];

    public string All => string.Join("\n", _entries.Select(e => e.Message));

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            entries.Enqueue((category, logLevel, exception is null ? text : text + " | " + exception));
        }
    }
}

/// <summary>Registra se algum byte do corpo foi lido (AC-009-21: a autenticação roda antes de ler o corpo).</summary>
internal sealed class BodyReadProbe : IStartupFilter
{
    private int _reads;

    public bool WasRead => Volatile.Read(ref _reads) > 0;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Request.Body = new CountingStream(context.Request.Body, () => Interlocked.Increment(ref _reads));
            await nextMiddleware();
        });
        next(app);
    };

    private sealed class CountingStream(Stream inner, Action onRead) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            onRead();
            return inner.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onRead();
            return inner.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            onRead();
            return inner.ReadAsync(buffer, offset, count, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

internal static class AdminServices
{
    public static void AddCapturedLogs(this IServiceCollection services, CapturedLogs logs) =>
        services.AddSingleton<ILoggerFactory>(logs);
}
