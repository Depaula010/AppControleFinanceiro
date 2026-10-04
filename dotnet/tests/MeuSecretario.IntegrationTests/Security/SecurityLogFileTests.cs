using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MeuSecretario.Api.Security;
using MeuSecretario.Application.Security;
using MeuSecretario.Infrastructure.Caching;
using MeuSecretario.Infrastructure.Security;
using MeuSecretario.Infrastructure.Time;
using MeuSecretario.IntegrationTests.Caching;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Serilog;
using Serilog.Extensions.Logging;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Security;

/// <summary>
/// <c>security.log</c> real num diretório temporário (AC-009-17, T9): o sub-logger de <see cref="SecurityLogConfiguration"/>
/// (o mesmo que o <c>Program.cs</c> usa) alimentado pelo <see cref="SecurityFilterMiddleware"/> real e por um Redis real.
/// Fora do host para não depender do <c>Log.Logger</c> estático que o <c>UseSerilog</c> compartilha entre hosts em paralelo.
/// </summary>
[Trait("Category", "Docker")]
public sealed class SecurityLogFileTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime, IDisposable
{
    private const string Ip = "203.0.113.7";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ms-seclog-" + Guid.NewGuid().ToString("N"));
    private Serilog.Core.Logger? _logger;
    private SerilogLoggerFactory? _factory;
    private RedisCache? _cache;
    private ConnectionMultiplexer? _raw;

    private string LogFile => Path.Combine(_directory, SecurityLogConfiguration.FileName);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _cache = new RedisCache(RedisConnectionOptions.Parse(redis.RedisUrl), new FakeTimeProvider(), new CapturingLogger<RedisCache>());
        _raw = await ConnectionMultiplexer.ConnectAsync(redis.ConnectionString + ",allowAdmin=true");
        await _raw.GetServer(_raw.GetEndPoints()[0]).FlushDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _factory?.Dispose();
        _logger?.Dispose();
        _cache?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _raw?.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private SecurityFilterMiddleware Middleware(string? level = null)
    {
        var options = SecurityOptions.Load(key => key switch
        {
            "SECURITY_LOG_FILE" => LogFile,
            "SECURITY_LOG_LEVEL" => level,
            _ => null,
        });
        var clock = new BrazilClock(TimeProvider.System);
        _logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .AddSecurityLog(options, clock.TimeZone, _directory)
            .CreateLogger();
        _factory = new SerilogLoggerFactory(_logger);
        return new SecurityFilterMiddleware(
            ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                return Task.CompletedTask;
            },
            new SecurityFilter(new RedisSecurityStore(_cache!)),
            clock,
            Options.Create(options),
            _factory);
    }

    private static async Task<int> Send(SecurityFilterMiddleware middleware, string path, string? userAgent = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        context.Request.Path = new PathString(path);
        if (userAgent is not null)
        {
            context.Request.Headers.UserAgent = userAgent;
        }

        // O ForwardedHeadersMiddleware já trocou o RemoteIpAddress pelo IP do cliente e deixou o X-Original-For.
        context.Connection.RemoteIpAddress = IPAddress.Parse(Ip);
        context.Request.Headers["X-Original-For"] = "10.0.0.9:4000";
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    private string[] Lines()
    {
        _factory?.Dispose();
        _factory = null;
        _logger?.Dispose();
        _logger = null;
        return File.ReadAllText(LogFile).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [Fact]
    [Trait("AC", "009-17")]
    public async Task Attempt_IsLoggedInTheFlaskFormat_WithoutControlCharacters()
    {
        var middleware = Middleware();

        (await Send(middleware, "/a\nb\rc")).ShouldBe(404);

        Lines().ShouldHaveSingleItem().ShouldMatch(
            @"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] WARNING: \[SECURITY-ATTEMPT\] IP: 203\.0\.113\.7 \| Tentativa: 1/5 \| Razão: Unknown endpoint: /a\?b\?c$");
    }

    [Fact]
    public async Task Timestamp_IsSaoPauloTime()
    {
        var middleware = Middleware();
        await Send(middleware, "/nao-existe");

        var stamp = Regex.Match(Lines()[0], @"^\[(.+?)\]").Groups[1].Value;
        var logged = DateTime.ParseExact(stamp, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var expected = new BrazilClock(TimeProvider.System).Now;

        Math.Abs((logged - expected).TotalMinutes).ShouldBeLessThan(2);
    }

    [Fact]
    public async Task BlockFlow_WritesTheFlaskMessages()
    {
        var middleware = Middleware();

        for (var i = 0; i < 5; i++)
        {
            await Send(middleware, "/.env");
        }

        (await Send(middleware, "/api/health")).ShouldBe(403);
        var text = string.Join('\n', Lines());

        text.ShouldContain(@"[SECURITY-ATTEMPT] IP: 203.0.113.7 | Tentativa: 4/5 | Razão: Suspicious URL pattern: /\.env");
        text.ShouldMatch(@"\[SECURITY-BLOCK\] IP bloqueado: 203\.0\.113\.7 \| Tentativas: 1 \| Bloqueado até: \d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}");
        text.ShouldContain("[SECURITY-AUTO-BLOCK] IP 203.0.113.7 bloqueado automaticamente | Razão: 5 tentativas suspeitas");
        text.ShouldContain("[SECURITY-BLOCKED] IP bloqueado tentou acessar | IP: 203.0.113.7 | Path: /api/health");
    }

    [Fact]
    public async Task Blacklisted_IsLogged()
    {
        var middleware = Middleware();
        await _raw!.GetDatabase().StringSetAsync($"security:blacklist:{Ip}", "\"{}\"");

        (await Send(middleware, "/")).ShouldBe(403);

        string.Join('\n', Lines()).ShouldContain("[SECURITY-BLACKLISTED] IP na blacklist tentou acessar | IP: 203.0.113.7 | Path: /");
    }

    [Fact]
    public async Task Log_NeverContainsTheFullUserAgent_OrSecrets()
    {
        var middleware = Middleware();

        await Send(middleware, "/rota", "Mozilla/5.0 sqlmap/1.7 chave-secreta-no-ua");
        await Send(middleware, "/outra");

        var text = string.Join('\n', Lines());

        text.ShouldContain("Suspicious User-Agent: sqlmap");
        text.ShouldNotContain("chave-secreta-no-ua");
        text.ShouldNotContain("Mozilla");
    }

    [Fact]
    public async Task LogLevel_FiltersTheSecurityLog()
    {
        var middleware = Middleware(level: "ERROR");

        await Send(middleware, "/nao-existe");

        Lines().ShouldBeEmpty();
    }

    [Fact]
    public async Task OtherCategories_DoNotGoToTheSecurityLog()
    {
        var middleware = Middleware();
        _logger!.ForContext("SourceContext", "MeuSecretario.Api.Outra").Warning("mensagem de outra categoria");
        _logger.ForContext<SecurityLogFileTests>().Warning("mensagem do teste");
        await Send(middleware, "/nao-existe");

        Lines().ShouldAllBe(l => l.Contains("[SECURITY-", StringComparison.Ordinal));
    }
}

