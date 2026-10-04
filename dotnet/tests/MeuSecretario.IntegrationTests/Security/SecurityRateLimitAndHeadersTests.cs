using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuSecretario.Api.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MeuSecretario.IntegrationTests.Security;

/// <summary>Rate limit por IP real (T7) e headers de segurança (T6), com a API real em memória.</summary>
[Trait("Category", "Docker")]
public sealed class SecurityRateLimitAndHeadersTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Limited = "/teste/limite";

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

    private SecurityTestHost Host(IReadOnlyDictionary<string, string>? settings = null)
    {
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true", settings, fakeRegisteredPaths: [Limited]);
        _hosts.Add(host);
        return host;
    }

    private SecurityTestHost RateLimited(int general = 3, int admin = 2) => Host(new Dictionary<string, string>
    {
        ["RATE_LIMIT_ENABLED"] = "true",
        ["RATE_LIMIT_PER_HOUR"] = general.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RATE_LIMIT_ADMIN_PER_HOUR"] = admin.ToString(System.Globalization.CultureInfo.InvariantCulture),
    });

    [Fact]
    [Trait("AC", "009-18")]
    public async Task FourthRequestFromTheSameRealIp_Is429Json_WithRetryAfter()
    {
        var host = RateLimited();

        for (var i = 0; i < 3; i++)
        {
            (await host.SendFrom("198.51.100.1", Limited)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var (response, body) = await host.SendFrom("198.51.100.1", Limited);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        body.ShouldBe("{\"error\":\"Too many requests\",\"message\":\"Rate limit exceeded. Try again later.\"}");
        var retryAfter = int.Parse(response.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture);
        retryAfter.ShouldBeInRange(1, 3600);
    }

    [Fact]
    [Trait("AC", "009-18")]
    public async Task ASecondRealIp_HasItsOwnBucket()
    {
        var host = RateLimited();
        for (var i = 0; i < 4; i++)
        {
            await host.SendFrom("198.51.100.1", Limited);
        }

        (await host.SendFrom("198.51.100.2", Limited)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    [Trait("AC", "009-18")]
    public async Task InternalCalls_AreNeverLimited()
    {
        var host = RateLimited();

        for (var i = 0; i < 10; i++)
        {
            (await host.Send(Limited, "127.0.0.1")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await host.Send(Limited, "172.18.0.4")).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/api/health")]
    [Trait("AC", "009-18")]
    public async Task HealthRoutes_AreExemptEvenFromExternalIps(string path)
    {
        var host = RateLimited();

        for (var i = 0; i < 10; i++)
        {
            (await host.SendFrom("198.51.100.1", path)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AdminRoutes_HaveTheirOwnSmallerBucket()
    {
        var host = RateLimited(general: 50, admin: 2);

        (await host.SendFrom("198.51.100.1", "/admin/x")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.SendFrom("198.51.100.1", "/admin/y")).Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await host.SendFrom("198.51.100.1", "/admin/z")).Response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        // O balde geral do mesmo IP não foi consumido pelo admin.
        (await host.SendFrom("198.51.100.1", Limited)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RateLimiter_RunsBeforeTheFilter_SoRejectedRequestsAreNotCounted()
    {
        var host = RateLimited();

        for (var i = 0; i < 6; i++)
        {
            await host.SendFrom("198.51.100.1", "/nao-existe");
        }

        var attempts = JsonDocument.Parse(JsonSerializer.Deserialize<string>((await host.Db.StringGetAsync("security:attempts:198.51.100.1")).ToString())!);
        attempts.RootElement.GetArrayLength().ShouldBe(3);
    }

    [Fact]
    public async Task TooManyRequestsResponse_AlsoCarriesTheSecurityHeaders()
    {
        var host = RateLimited(general: 1);
        await host.SendFrom("198.51.100.1", Limited);

        var (response, _) = await host.SendFrom("198.51.100.1", Limited);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
    }

    [Fact]
    public async Task ByDefault_RateLimitIsOff()
    {
        var host = Host();

        for (var i = 0; i < 30; i++)
        {
            (await host.SendFrom("198.51.100.1", Limited)).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    // ---------------------------------------------------------------- headers (T6, AC-009-19)

    private static readonly string[] SecurityHeaderNames =
    [
        "X-Frame-Options", "X-Content-Type-Options", "Referrer-Policy", "Strict-Transport-Security", "Content-Security-Policy",
    ];

    private static void ShouldCarryTheSecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        response.Headers.GetValues("Referrer-Policy").Single().ShouldBe("strict-origin-when-cross-origin");
        response.Headers.GetValues("Strict-Transport-Security").Single().ShouldBe("max-age=31536000; includeSubDomains");
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Regex.IsMatch(
            csp,
            @"^default-src 'self'; script-src 'self' 'nonce-[A-Za-z0-9+/=]{22,}'; style-src 'self' 'unsafe-inline'; " +
            @"img-src 'self' data: https:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'$").ShouldBeTrue(csp);
    }

    [Fact]
    [Trait("AC", "009-19")]
    public async Task AllResponses_CarryTheSecurityHeaders_IncludingFilterAnd404Responses()
    {
        var host = Host();
        await host.Db.StringSetAsync("security:blocked:198.51.100.7", JsonSerializer.Serialize("{\"blocked_until\": \"2999-01-01T00:00:00\", \"count\": 1}"));

        ShouldCarryTheSecurityHeaders((await host.SendFrom("198.51.100.1", "/api/health")).Response);
        ShouldCarryTheSecurityHeaders((await host.SendFrom("198.51.100.1", "/nao-existe")).Response);
        ShouldCarryTheSecurityHeaders((await host.SendFrom("198.51.100.7", "/api/health")).Response);
        ShouldCarryTheSecurityHeaders((await host.Send("/nao-existe", "127.0.0.1")).Response);
        ShouldCarryTheSecurityHeaders((await host.SendFrom("198.51.100.1", "/auth/login")).Response);
    }

    [Fact]
    [Trait("AC", "009-19")]
    public async Task CspNonce_ChangesOnEveryResponse()
    {
        var host = Host();

        var first = (await host.Send("/api/health", "127.0.0.1")).Response.Headers.GetValues("Content-Security-Policy").Single();
        var second = (await host.Send("/api/health", "127.0.0.1")).Response.Headers.GetValues("Content-Security-Policy").Single();

        first.ShouldNotBe(second);
    }

    [Fact]
    [Trait("AC", "009-19")]
    public async Task SecurityHeadersDisabled_AddsNone()
    {
        var host = Host(new Dictionary<string, string> { ["SECURITY_HEADERS_ENABLED"] = "false" });

        var (response, _) = await host.SendFrom("198.51.100.1", "/api/health");

        foreach (var name in SecurityHeaderNames)
        {
            response.Headers.Contains(name).ShouldBeFalse(name);
        }
    }

    // ---------------------------------------------------------------- fiação (T9)

    [Fact]
    public void Options_AreReadFromConfiguration_WithTheDecidedDefaults()
    {
        var defaults = Host().Services.GetRequiredService<IOptions<SecurityOptions>>().Value;
        var configured = RateLimited(7, 5).Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

        defaults.RateLimitEnabled.ShouldBeFalse();
        defaults.BotProtectionEnabled.ShouldBeTrue();
        defaults.SecurityHeadersEnabled.ShouldBeTrue();
        configured.RateLimitEnabled.ShouldBeTrue();
        configured.RateLimitPerHour.ShouldBe(7);
        configured.RateLimitAdminPerHour.ShouldBe(5);
    }

    [Fact]
    [Trait("AC", "009-06")]
    public void ForwardedHeaders_TrustOnlyThePrivateNetworks_AndReadEveryHop()
    {
        var options = Host().Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        options.ForwardedHeaders.ShouldBe(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
        options.KnownProxies.ShouldBeEmpty();
        options.ForwardLimit.ShouldBeNull();
        options.KnownIPNetworks.Select(n => n.ToString()).ShouldBe(
            ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "169.254.0.0/16", "fc00::/7", "fe80::/10"]);
    }
}
