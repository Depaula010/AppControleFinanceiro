using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Security;

/// <summary>Filtro de segurança de ponta a ponta, com a API real em memória e Redis em container (T5, T8, T9).</summary>
[Trait("Category", "Docker")]
public sealed class SecurityFilterEndpointsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Ip = "203.0.113.7";
    private const string Unknown = "/rota-que-nao-existe";

    private readonly List<SecurityTestHost> _hosts = [];

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

    private SecurityTestHost Host(
        IReadOnlyDictionary<string, string>? settings = null,
        Action<IServiceCollection>? services = null,
        IReadOnlyCollection<string>? fakeRegistered = null)
    {
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true", settings, services, fakeRegistered);
        _hosts.Add(host);
        return host;
    }

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;

    private static string[] InnerReasons(RedisValue raw) =>
        [.. JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw.ToString())!).RootElement.EnumerateArray()
            .Select(e => e.GetProperty("reason").GetString()!)];

    private static async Task SeedBlocked(SecurityTestHost host, string ip, DateTime until) =>
        await host.Db.StringSetAsync(
            $"security:blocked:{ip}",
            JsonSerializer.Serialize(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["blocked_until"] = until.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture),
                ["count"] = 1,
                ["first_blocked"] = until.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture),
            })));

    private static async Task SeedBlacklist(SecurityTestHost host, string ip) =>
        await host.Db.StringSetAsync(
            $"security:blacklist:{ip}",
            JsonSerializer.Serialize("{\"ip\": \"" + ip + "\", \"reason\": \"teste\", \"blacklisted_at\": \"2026-10-04T10:00:00\", \"permanent\": true}"));

    [Fact]
    [Trait("AC", "009-01")]
    public async Task UnknownRoute_FromExternalIp_Is404Json_AndCountsOneAttempt()
    {
        var host = Host();

        var (response, body) = await host.SendFrom(Ip, Unknown);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        body.ShouldBe("{\"error\":\"Not found\"}");
        InnerReasons(await host.Db.StringGetAsync($"security:attempts:{Ip}")).ShouldBe([$"Unknown endpoint: {Unknown}"]);
    }

    [Fact]
    [Trait("AC", "009-02")]
    [Trait("AC", "009-03")]
    public async Task FifthSuspiciousRequest_BlocksTheIp_AndEveryRouteBecomes403()
    {
        var host = Host();

        for (var i = 0; i < 4; i++)
        {
            (await host.SendFrom(Ip, Unknown)).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        var (response, body) = await host.SendFrom(Ip, Unknown);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Json(body).GetProperty("error").GetString().ShouldBe("Access denied");
        Json(body).GetProperty("message").GetString().ShouldBe("Too many suspicious requests. IP blocked.");
        (await host.Db.KeyExistsAsync($"security:attempts:{Ip}")).ShouldBeFalse();
        var inner = JsonDocument.Parse(JsonSerializer.Deserialize<string>((await host.Db.StringGetAsync($"security:blocked:{Ip}")).ToString())!).RootElement;
        inner.GetProperty("count").GetInt32().ShouldBe(1);

        var (next, nextBody) = await host.SendFrom(Ip, "/api/health");
        next.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Json(nextBody).GetProperty("message").GetString().ShouldBe("Your IP has been temporarily blocked due to suspicious activity");
    }

    [Fact]
    [Trait("AC", "009-04")]
    public async Task Blacklist_ComesBeforeTheTemporaryBlock()
    {
        var host = Host();
        await SeedBlocked(host, Ip, DateTime.UtcNow.AddHours(1));
        await SeedBlacklist(host, Ip);

        var (response, body) = await host.SendFrom(Ip, "/");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        Json(body).GetProperty("error").GetString().ShouldBe("Access permanently denied");
        Json(body).GetProperty("message").GetString().ShouldBe("Your IP has been permanently blocked");
    }

    [Fact]
    [Trait("AC", "009-13")]
    public async Task BlacklistWrittenByPython_BlocksThroughTheFilter()
    {
        var host = Host();
        var fixture = PythonFixture.Case("blacklist");
        await host.Db.StringSetAsync(fixture.Key, fixture.Raw);

        var (response, _) = await host.SendFrom("192.0.2.10", "/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    [Trait("AC", "009-11")]
    public async Task CorruptedBlock_IsDeleted_AndTheRequestProceeds()
    {
        var host = Host();
        await host.Db.StringSetAsync($"security:blocked:{Ip}", "isto nao e json");

        var (response, _) = await host.SendFrom(Ip, "/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await host.Db.KeyExistsAsync($"security:blocked:{Ip}")).ShouldBeFalse();
    }

    [Fact]
    public async Task ExpiredBlock_LetsTheRequestThrough()
    {
        var host = Host();
        await SeedBlocked(host, Ip, DateTime.UtcNow.AddYears(-1));

        (await host.SendFrom(Ip, "/api/health")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.9")]
    [InlineData("::1")]
    [Trait("AC", "009-05")]
    public async Task InternalCall_WithoutForwardedFor_BypassesTheFilter_AndGetsFlaskHtml404(string remote)
    {
        var host = Host();

        var (response, body) = await host.Send(Unknown, remote);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        body.ShouldContain("<title>404 Not Found</title>");
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-05")]
    public async Task InternalCall_IsNotBlocked_EvenIfTheLoopbackIpWasBlacklisted()
    {
        var host = Host();
        await SeedBlacklist(host, "127.0.0.1");

        (await host.Send("/api/health", "127.0.0.1")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "009-06")]
    public async Task PrivatePeerWithForwardedFor_RunsTheFilterForTheForwardedIp()
    {
        var host = Host();

        await host.Send(Unknown, SecurityTestHost.Private, "198.51.100.9");

        (await host.SecurityKeys()).ShouldBe(["security:attempts:198.51.100.9"]);
    }

    [Fact]
    [Trait("AC", "009-06")]
    public async Task ForgedLeftmostForwardedFor_IsIgnored_RightmostUntrustedWins()
    {
        var host = Host();

        await host.Send(Unknown, SecurityTestHost.Private, "1.1.1.1, 198.51.100.9");

        (await host.SecurityKeys()).ShouldBe(["security:attempts:198.51.100.9"]);
    }

    [Fact]
    [Trait("AC", "009-06")]
    public async Task ChainOfPrivateProxies_ResolvesTheFirstUntrustedFromTheRight()
    {
        var host = Host();

        await host.Send(Unknown, SecurityTestHost.Private, "9.9.9.9, 198.51.100.9, 172.18.0.5");

        (await host.SecurityKeys()).ShouldBe(["security:attempts:198.51.100.9"]);
    }

    [Fact]
    [Trait("AC", "009-06")]
    public async Task PublicPeer_CannotSpoofTheForwardedFor()
    {
        var host = Host();

        await host.Send(Unknown, SecurityTestHost.Public, "198.51.100.9");

        (await host.SecurityKeys()).ShouldBe([$"security:attempts:{SecurityTestHost.Public}"]);
    }

    [Fact]
    public async Task IPv4MappedPeer_IsNormalizedBeforeTheKeyIsBuilt()
    {
        var host = Host();

        await host.Send(Unknown, "::ffff:203.0.113.60");

        (await host.SecurityKeys()).ShouldBe(["security:attempts:203.0.113.60"]);
    }

    [Fact]
    [Trait("AC", "009-07")]
    public async Task SuspiciousUserAgent_OnAValidRoute_Is404Json_WithTheReasonRecorded()
    {
        var host = Host();

        var (response, body) = await host.SendFrom(Ip, "/", "sqlmap/1.7");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldBe("{\"error\":\"Not found\"}");
        InnerReasons(await host.Db.StringGetAsync($"security:attempts:{Ip}")).ShouldBe(["Suspicious User-Agent: sqlmap"]);
    }

    [Fact]
    [Trait("AC", "009-08")]
    public async Task RegisteredRoute_WithSuspiciousSegment_ReachesTheHandler_WithoutAttempt()
    {
        var host = Host(fakeRegistered: ["/admin/config-alertas-financeiros"]);

        var (response, body) = await host.SendFrom(Ip, "/admin/config-alertas-financeiros");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("alcancou o handler");
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-08")]
    public async Task SameSuspiciousSegment_OnAnUnregisteredPath_IsCounted()
    {
        var host = Host(fakeRegistered: ["/admin/config-alertas-financeiros"]);

        var (response, _) = await host.SendFrom(Ip, "/admin/config-outra");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        InnerReasons(await host.Db.StringGetAsync($"security:attempts:{Ip}")).ShouldBe([@"Suspicious URL pattern: /config"]);
    }

    [Theory]
    [InlineData(false, 404)]
    [InlineData(true, 200)]
    [Trait("AC", "009-09")]
    public async Task ApiKeysRoutes_DependOnTheFlag(bool enabled, int expected)
    {
        var host = Host(new Dictionary<string, string> { ["API_KEYS_ROUTES_ENABLED"] = enabled ? "true" : "false" });

        var (response, _) = await host.SendFrom(Ip, "/api-keys/health");

        ((int)response.StatusCode).ShouldBe(expected);
        (await host.SecurityKeys()).Count.ShouldBe(enabled ? 0 : 1);
    }

    [Fact]
    [Trait("AC", "009-15")]
    public async Task WrongMethodOnARegisteredRoute_Is405_AndIsNotCounted()
    {
        var host = Host();

        var (response, _) = await host.SendFrom(Ip, "/connect-calendar/1", method: HttpMethod.Post);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-15")]
    public async Task WrongMethod_FromInternalCaller_StillGets405NotTheFallback()
    {
        var host = Host();

        (await host.Send("/connect-calendar/1", "127.0.0.1", method: HttpMethod.Post)).Response.StatusCode
            .ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    [Trait("AC", "009-16")]
    public async Task InternalCall_ToUnknownPathsAndTrailingSlash_GetsFlaskHtml404()
    {
        var host = Host();

        foreach (var path in new[] { "/nao-existe/a/b/c", "/api/health/", "/admin/inexistente" })
        {
            var (response, body) = await host.Send(path, "127.0.0.1");

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
            response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html", path);
            body.ShouldContain("Not Found");
        }
    }

    [Fact]
    [Trait("AC", "009-20")]
    public async Task BotProtectionDisabled_FilterDoesNothing_AndFallbackReturnsHtml()
    {
        var host = Host(new Dictionary<string, string> { ["BOT_PROTECTION_ENABLED"] = "false" });

        var (response, body) = await host.SendFrom(Ip, "/.env");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        body.ShouldContain("<title>404 Not Found</title>");
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-10")]
    public async Task RedisDown_SuspiciousRequestIs404Json_WithoutBlocking_OrThrowing()
    {
        var host = Host(new Dictionary<string, string> { ["REDIS_URL"] = "redis://127.0.0.1:1" });

        for (var i = 0; i < 6; i++)
        {
            var (response, body) = await host.SendFrom(Ip, Unknown);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            body.ShouldBe("{\"error\":\"Not found\"}");
        }

        (await host.SendFrom(Ip, "/api/health")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "009-10")]
    public async Task StoreFailure_IsFailOpen_AndDoesNotLeakTheExceptionMessage()
    {
        var host = Host(services: s => s.Replace(ServiceDescriptor.Singleton<ISecurityStore>(new ThrowingSecurityStore())));

        var (ok, _) = await host.SendFrom(Ip, "/api/health");
        var (missing, body) = await host.SendFrom(Ip, Unknown);

        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldNotContain("segredo-do-store");
    }

    [Fact]
    [Trait("Category", "Docker")]
    public async Task PipelineOrder_FilterRunsAfterRouting_SoRegisteredRoutesAreNeverSuspicious()
    {
        // Risco do plan §8: se o filtro rodasse antes do roteamento, GetEndpoint() seria nulo e "/" viraria desconhecido.
        var host = Host();

        foreach (var path in new[] { "/", "/api/health", "/auth/login" })
        {
            var (response, _) = await host.SendFrom(Ip, path);

            // 200 nas rotas GET reais; 405 em /auth/login (existe só para POST): nunca o 404 JSON do filtro.
            response.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.MethodNotAllowed);
        }

        (await host.SecurityKeys()).ShouldBeEmpty();
        (await host.SendFrom(Ip, "/")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task FallbackIsAMiddleware_NotAMapFallbackEndpoint()
    {
        var host = Host();

        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => e.RoutePattern.RawText ?? string.Empty)
            .ToList();

        endpoints.ShouldNotBeEmpty();
        endpoints.ShouldNotContain(p => p.Contains("{*", StringComparison.Ordinal) || p.Contains("{**", StringComparison.Ordinal));
        await Task.CompletedTask;
    }
}

