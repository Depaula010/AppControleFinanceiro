using System.Net;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Admin;
using MeuSecretario.Application.Security;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>Autenticação <c>x-api-key</c> nas 33 rotas (009a, 009b e 009c) e roteamento do id (T11, T23, T30).</summary>
[Trait("Category", "Docker")]
public sealed class AdminAuthTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    public static TheoryData<string, string, string?> Routes => new()
    {
        { "POST", "/admin/security-unblock-ip", """{"ip":"1.2.3.4"}""" },
        { "GET", "/admin/security-stats", null },
        { "POST", "/admin/security-blacklist-add", """{"ip":"1.2.3.4"}""" },
        { "POST", "/admin/security-blacklist-remove", """{"ip":"1.2.3.4"}""" },
        { "POST", "/admin/gemini-cache-clear", "{}" },
        { "GET", "/admin/gemini-cache-stats", null },
        { "POST", "/admin/clear-bot-session", null },
        { "GET", "/admin/get-notification-config/1", null },
        { "POST", "/admin/config-alertas-financeiros", """{"usuario_id":1,"ativo":true}""" },
        { "GET", "/admin/setup-database", null },
        { "GET", "/admin/populate-global-categories", null },
        { "GET", "/admin/setup-user-data", null },
        { "GET", "/admin/setup-calendar-table", null },
        { "GET", "/admin/setup-monthly-reports-table", null },
        { "POST", "/admin/setup-api-keys-tables", null },
        { "GET", "/admin/setup-resumo-matinal", null },
        { "GET", "/admin/setup-checkin-noturno", null },
        { "GET", "/admin/setup-potes-alerts", null },
        { "GET", "/admin/setup-alertas-financeiros", null },
        { "GET", "/admin/setup-reserva-emergencia", null },
        { "GET", "/admin/cleanup-deprecated-notification-fields", null },
        { "GET", "/admin/setup-budget-violations", null },
        { "GET", "/admin/oauth-config-check", null },
        { "GET", "/admin/debug-calendar", null },
        { "POST", "/admin/run-motor-agendamentos", "{}" },
        { "POST", "/admin/trigger-agenda-notifications", null },
        { "POST", "/admin/trigger-bills-notifications", null },
        { "POST", "/admin/trigger-daily-briefing", null },
        { "POST", "/admin/trigger-monthly-reports-inicio", null },
        { "POST", "/admin/trigger-monthly-reports-fim", null },
        { "POST", "/admin/test-notification", """{"tipo":"contas","usuario_id":1}""" },
        { "POST", "/admin/test-monthly-report/1", null },
        { "POST", "/admin/test-daily-briefing", """{"usuario_id":1}""" },
    };

    private readonly List<SecurityTestHost> _hosts = [];
    private readonly BodyReadProbe _probe = new();
    private readonly DependentFakes _fakes = new();

    public async Task InitializeAsync()
    {
        await using var host = Host();
        await host.Flush();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    private SecurityTestHost Host(IReadOnlyDictionary<string, string>? settings = null)
    {
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            settings,
            services =>
            {
                // As rotas da 009c rodam contra portas falsas: nenhum job real, banco ou bot é tocado pelo teste.
                _fakes.Install(services);
                services.AddSingleton<IStartupFilter>(_probe);
            });
        _hosts.Add(host);
        return host;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    [Trait("AC", "009-21")]
    public async Task MissingEmptyOrWrongKey_Is401_WithoutReadingTheBody(string method, string path, string? body)
    {
        var host = Host();

        foreach (var key in new string?[] { null, string.Empty, "chave-errada", AdminHttp.ApiKey + "x" })
        {
            var (status, text) = await host.Admin(new HttpMethod(method), path, body ?? (method == "POST" ? "{}" : null), key);

            status.ShouldBe(HttpStatusCode.Unauthorized, $"{method} {path} com chave '{key}'");
            text.ShouldBe(AdminHttp.Error("Não autorizado"));
        }

        _probe.WasRead.ShouldBeFalse($"{method} {path} leu o corpo antes de autenticar");
    }

    [Fact]
    public async Task TheProbeDoesDetectBodyReads_WhenTheKeyIsValid()
    {
        var host = Host();

        await host.Post("/admin/security-unblock-ip", """{"ip":"1.2.3.4"}""");

        _probe.WasRead.ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Routes))]
    [Trait("AC", "009-21")]
    public async Task ValidKey_ReachesTheRoute(string method, string path, string? body)
    {
        var host = Host();

        var (status, text) = await host.Admin(new HttpMethod(method), path, body);

        status.ShouldNotBe(HttpStatusCode.Unauthorized);
        text.ShouldNotBe(AdminHttp.Error("Não autorizado"));
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task WrongMethod_Is405(string method, string path, string? _)
    {
        var host = Host();
        var other = method == "GET" ? HttpMethod.Post : HttpMethod.Get;

        var (status, _) = await host.Admin(other, path, other == HttpMethod.Post ? "{}" : null, key: null);

        status.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Theory]
    [InlineData("/admin/security-stats/")]
    [InlineData("/admin/inexistente")]
    [InlineData("/admin/security-stats/extra")]
    public async Task UnknownAdminPath_IsFlask404Html_AndNeverReaches401(string path)
    {
        var host = Host();

        var (response, body) = await host.Call(HttpMethod.Get, path);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        body.ShouldContain("<h1>Not Found</h1>");
    }

    [Fact]
    public async Task TheKeyHeaderNameIsCaseInsensitive()
    {
        var host = Host();

        var (response, _) = await host.Call(
            HttpMethod.Get, "/admin/gemini-cache-stats", headers: new Dictionary<string, string> { ["x-api-key"] = AdminHttp.ApiKey });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

/// <summary>Nenhum 500 das 9 rotas devolve nem loga a mensagem da exceção (AC-009-30, desvio 008) (T19).</summary>
[Trait("Category", "Docker")]
public sealed class AdminErrorLeakTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Sentinel = "SEGREDO-SENTINELA-9f3a";

    public static TheoryData<string, string, string?, string> Failures => new()
    {
        { "POST", "/admin/security-unblock-ip", """{"ip":"1.2.3.4"}""", "Erro ao desbloquear IP. Tente novamente." },
        { "GET", "/admin/security-stats", null, "Erro ao obter estatísticas de segurança. Tente novamente." },
        { "POST", "/admin/security-blacklist-add", """{"ip":"1.2.3.4"}""", "Erro ao adicionar IP à blacklist. Tente novamente." },
        { "POST", "/admin/security-blacklist-remove", """{"ip":"1.2.3.4"}""", "Erro ao remover IP da blacklist. Tente novamente." },
        { "POST", "/admin/gemini-cache-clear", "{}", "Erro ao limpar o cache do Gemini. Tente novamente." },
        { "GET", "/admin/gemini-cache-stats", null, "Erro ao obter estatísticas do cache. Tente novamente." },
        { "POST", "/admin/clear-bot-session", null, "Erro ao limpar a sessão do bot. Tente novamente." },
        { "GET", "/admin/get-notification-config/1", null, "Erro ao obter as configurações de notificação. Tente novamente." },
        { "POST", "/admin/config-alertas-financeiros", """{"usuario_id":1,"ativo":true}""", "Erro ao atualizar a configuração de alertas financeiros. Tente novamente." },
    };

    private readonly List<SecurityTestHost> _hosts = [];

    public async Task InitializeAsync()
    {
        await using var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true");
        await host.Flush();
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [Theory]
    [MemberData(nameof(Failures))]
    [Trait("AC", "009-30")]
    public async Task UnexpectedError_Is500WithTheFixedText_WithoutTheExceptionMessage(string method, string path, string? json, string expected)
    {
        var logs = new CapturedLogs();
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            services: services =>
            {
                services.AddCapturedLogs(logs);
                services.RemoveAll<ISecurityStore>();
                services.AddSingleton<ISecurityStore, SentinelSecurityStore>();
                services.RemoveAll<ILlmCacheAdmin>();
                services.AddSingleton<ILlmCacheAdmin, SentinelLlmCacheAdmin>();
                services.RemoveAll<IBotSessionRepository>();
                services.AddScoped<IBotSessionRepository, SentinelBotSessionRepository>();
                services.RemoveAll<IAdminNotificationConfigRepository>();
                services.AddScoped<IAdminNotificationConfigRepository, SentinelNotificationConfigRepository>();
            });
        _hosts.Add(host);

        var (status, body) = await host.Admin(new HttpMethod(method), path, json);

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error(expected));
        body.ShouldNotContain(Sentinel);
        logs.All.ShouldNotContain(Sentinel);
        logs.Entries.ShouldContain(e => e.Message.Contains("InvalidOperationException") && e.Message.Contains("Erro na rota admin"));
    }

    [Fact]
    [Trait("AC", "009-30")]
    public async Task DatabaseDown_Is500WithTheFixedText_WithoutConnectionDetails()
    {
        var logs = new CapturedLogs();
        var host = new SecurityTestHost(
            redis.RedisUrl,
            redis.ConnectionString + ",allowAdmin=true",
            new Dictionary<string, string> { ["DATABASE_URL"] = $"postgresql://usuario-sentinela:{Sentinel}@127.0.0.1:1/banco" },
            services => services.AddCapturedLogs(logs));
        _hosts.Add(host);

        var (status, body) = await host.Post("/admin/clear-bot-session");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Erro ao limpar a sessão do bot. Tente novamente."));
        body.ShouldNotContain(Sentinel);
        logs.All.ShouldNotContain(Sentinel);
        logs.All.ShouldNotContain("usuario-sentinela");
    }

    private sealed class SentinelSecurityStore : ISecurityStore
    {
        private static InvalidOperationException Fail() => new(Sentinel);

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => throw Fail();

        public Task<bool> IsBlacklistedAsync(string ip, CancellationToken cancellationToken) => throw Fail();

        public Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken cancellationToken) => throw Fail();

        public Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken cancellationToken) => throw Fail();

        public Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken cancellationToken) => throw Fail();

        public Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken cancellationToken) => throw Fail();

        public Task<bool> RemoveTempBlockAsync(string ip, CancellationToken cancellationToken) => throw Fail();

        public Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken cancellationToken) => throw Fail();
    }

    private sealed class SentinelLlmCacheAdmin : ILlmCacheAdmin
    {
        public Task<int> InvalidateUserAsync(int userId, string? pattern, CancellationToken cancellationToken) => throw new InvalidOperationException(Sentinel);

        public Task<int> InvalidatePatternAsync(string pattern, CancellationToken cancellationToken) => throw new InvalidOperationException(Sentinel);

        public LlmCacheMetrics GetMetrics() => throw new InvalidOperationException(Sentinel);
    }

    private sealed class SentinelBotSessionRepository : IBotSessionRepository
    {
        public Task<int> ClearAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(Sentinel);
    }

    private sealed class SentinelNotificationConfigRepository : IAdminNotificationConfigRepository
    {
        public Task<NotificationConfigRow?> GetOrCreateAsync(int userId, CancellationToken cancellationToken) => throw new InvalidOperationException(Sentinel);

        public Task<bool?> SetFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken) => throw new InvalidOperationException(Sentinel);
    }
}
