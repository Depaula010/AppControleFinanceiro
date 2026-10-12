using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary>As rotas <c>security-*</c> da 009a com a API real em memória e Redis em container (T12, T13).</summary>
[Trait("Category", "Docker")]
public sealed class SecurityAdminEndpointsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private const string Ip = "198.51.100.20";

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

    private SecurityTestHost Host(IReadOnlyDictionary<string, string>? settings = null, Action<IServiceCollection>? services = null)
    {
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true", settings, services);
        _hosts.Add(host);
        return host;
    }

    private SecurityTestHost RedisDownHost() =>
        Host(new Dictionary<string, string> { ["REDIS_URL"] = AdminHttp.RedisDown });

    private static JsonElement Json(string body) => JsonDocument.Parse(body).RootElement;

    private static string Interior(string text) => JsonSerializer.Serialize(text);

    private static async Task SeedRaw(SecurityTestHost host, string key, string value) =>
        await host.Db.StringSetAsync(key, Interior(value));

    // ---- 1. security-unblock-ip -------------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-22")]
    public async Task Unblock_RemovesBlockAttemptsAndBlacklist_AndTrimsTheIp()
    {
        var logs = new CapturedLogs();
        var host = Host(services: s => s.AddCapturedLogs(logs));
        await SeedRaw(host, $"security:blocked:{Ip}", "{}");
        await SeedRaw(host, $"security:attempts:{Ip}", "[]");
        await SeedRaw(host, $"security:blacklist:{Ip}", "{}");
        await SeedRaw(host, "security:blocked:198.51.100.21", "{}");

        var (status, body) = await host.Post("/admin/security-unblock-ip", $$"""{"ip":"  {{Ip}} "}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(AdminHttp.Success($"IP {Ip} desbloqueado"));
        (await host.SecurityKeys()).ShouldBe(["security:blocked:198.51.100.21"]);
        logs.Entries.ShouldContain(e => e.Category == "MeuSecretario.Security" && e.Level == LogLevel.Warning
            && e.Message == $"[SECURITY-UNBLOCK] Bloqueio temporário removido manualmente: {Ip}");
        logs.Entries.ShouldContain(e => e.Category == "MeuSecretario.Security"
            && e.Message == $"[SECURITY-BLACKLIST] IP removido da blacklist: {Ip}");
    }

    [Fact]
    [Trait("AC", "009-22")]
    public async Task Unblock_IpThatWasNeverBlocked_StillSucceeds()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-unblock-ip", """{"ip":"203.0.113.1"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(AdminHttp.Success("IP 203.0.113.1 desbloqueado"));
    }

    [Fact]
    [Trait("AC", "009-22")]
    public async Task Unblock_WithoutRedis_StillSucceeds()
    {
        var logs = new CapturedLogs();
        var host = Host(new Dictionary<string, string> { ["REDIS_URL"] = AdminHttp.RedisDown }, s => s.AddCapturedLogs(logs));

        var (status, body) = await host.Post("/admin/security-unblock-ip", """{"ip":"203.0.113.1"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(AdminHttp.Success("IP 203.0.113.1 desbloqueado"));
        logs.All.ShouldNotContain("[SECURITY-UNBLOCK]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"ip":""}""")]
    [InlineData("""{"ip":"   "}""")]
    [InlineData("""{"ip":null}""")]
    [InlineData("""{"ip":5}""")]
    [InlineData("""{"ip":["1.2.3.4"]}""")]
    [InlineData("[1]")]
    [InlineData("isto não é json")]
    [Trait("AC", "009-22")]
    public async Task Unblock_MissingOrNonTextIp_Is400(string? json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-unblock-ip", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'ip' obrigatório"));
    }

    [Theory]
    [InlineData("não-é-ip")]
    [InlineData("1.2.3")]
    [InlineData("999.1.1.1")]
    [Trait("Desvio", "078")]
    public async Task Unblock_TextThatIsNotAnIp_Is400(string ip)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-unblock-ip", JsonSerializer.Serialize(new { ip }));

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("IP inválido"));
    }

    // ---- 2. security-stats ------------------------------------------------------------------------------------------

    private const string EmptyStats =
        """{"status":"sucesso","mensagem":"Estatísticas de segurança obtidas","blacklisted_ips":[],"blocked_ips":[],"suspicious_activity":[],"total_blacklisted":0,"total_blocked":0,"total_suspicious":0,"redis_connected":true}""";

    [Fact]
    [Trait("AC", "009-23")]
    public async Task Stats_WithAnEmptyRedis_HasEmptyListsAndRedisConnected()
    {
        var host = Host();

        var (status, body) = await host.Get("/admin/security-stats");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(EmptyStats);
    }

    [Fact]
    [Trait("AC", "009-23")]
    public async Task Stats_ListsEveryKind_OrderedByIp_IgnoringCorruptedEntries()
    {
        var host = Host();
        var store = host.Services.GetRequiredService<ISecurityStore>();
        var now = host.Services.GetRequiredService<IClock>().Now;

        await store.BlacklistAsync("9.9.9.9", "Manual block via API", now, CancellationToken.None);
        await store.BlacklistAsync("1.1.1.1", null, now, CancellationToken.None);
        for (var i = 0; i < 5; i++)
        {
            await store.TrackAttemptAsync("7.7.7.7", "Unknown endpoint: /x", now, CancellationToken.None);
        }

        await store.TrackAttemptAsync("5.5.5.5", "Unknown endpoint: /a", now, CancellationToken.None);
        await store.TrackAttemptAsync("5.5.5.5", "Suspicious URL pattern: /\\.env", now, CancellationToken.None);
        await store.TrackAttemptAsync("5.5.5.5", "r3", now, CancellationToken.None);
        await store.TrackAttemptAsync("5.5.5.5", "r4", now, CancellationToken.None);
        await host.Db.StringSetAsync("security:blocked:3.3.3.3", "isto não é json");
        await SeedRaw(host, "security:blacklist:4.4.4.4", "[1,2]");
        await SeedRaw(host, "security:attempts:6.6.6.6", "{\"x\":1}");

        var (status, body) = await host.Get("/admin/security-stats");

        status.ShouldBe(HttpStatusCode.OK);
        var json = Json(body);
        json.EnumerateObject().Select(p => p.Name).ShouldBe(
        [
            "status", "mensagem", "blacklisted_ips", "blocked_ips", "suspicious_activity",
            "total_blacklisted", "total_blocked", "total_suspicious", "redis_connected",
        ]);

        var blacklist = json.GetProperty("blacklisted_ips").EnumerateArray().ToList();
        blacklist.Select(e => e.GetProperty("ip").GetString()).ShouldBe(["1.1.1.1", "9.9.9.9"]);
        blacklist[0].GetProperty("reason").ValueKind.ShouldBe(JsonValueKind.Null);
        blacklist[1].GetProperty("reason").GetString().ShouldBe("Manual block via API");
        blacklist[1].GetProperty("permanent").GetBoolean().ShouldBeTrue();
        blacklist[1].GetProperty("blacklisted_at").GetString()!.ShouldMatch(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{6}$");
        blacklist[0].EnumerateObject().Select(p => p.Name).ShouldBe(["ip", "reason", "blacklisted_at", "permanent"]);

        var blocked = json.GetProperty("blocked_ips").EnumerateArray().Single();
        blocked.EnumerateObject().Select(p => p.Name).ShouldBe(["ip", "attempts", "blocked_until", "time_remaining"]);
        blocked.GetProperty("ip").GetString().ShouldBe("7.7.7.7");
        blocked.GetProperty("attempts").GetInt32().ShouldBe(1);
        blocked.GetProperty("time_remaining").GetString()!.ShouldMatch(@"^0:59:\d{2}$");

        var suspicious = json.GetProperty("suspicious_activity").EnumerateArray().Single();
        suspicious.EnumerateObject().Select(p => p.Name).ShouldBe(["ip", "recent_attempts", "last_attempt", "reasons"]);
        suspicious.GetProperty("ip").GetString().ShouldBe("5.5.5.5");
        suspicious.GetProperty("recent_attempts").GetInt32().ShouldBe(4);
        suspicious.GetProperty("reasons").EnumerateArray().Select(e => e.GetString()).ShouldBe(["Suspicious URL pattern: /\\.env", "r3", "r4"]);

        json.GetProperty("total_blacklisted").GetInt32().ShouldBe(2);
        json.GetProperty("total_blocked").GetInt32().ShouldBe(1);
        json.GetProperty("total_suspicious").GetInt32().ShouldBe(1);
        json.GetProperty("redis_connected").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    [Trait("AC", "009-23")]
    public async Task Stats_WithoutRedis_HasTheErrorFieldAndNoRedisConnected()
    {
        var host = RedisDownHost();

        var (status, body) = await host.Get("/admin/security-stats");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Estatísticas de segurança obtidas","error":"Redis não disponível","blacklisted_ips":[],"blocked_ips":[],"suspicious_activity":[],"total_blacklisted":0,"total_blocked":0,"total_suspicious":0}""");
    }

    // ---- 3. security-blacklist-add ----------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_DefaultReason_WritesTheDoubleEncodedKey_AndBlocksTheIpForGood()
    {
        var logs = new CapturedLogs();
        var host = Host(services: s => s.AddCapturedLogs(logs));

        var (status, body) = await host.Post("/admin/security-blacklist-add", """{"ip":"198.51.100.30"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"IP 198.51.100.30 adicionado à blacklist permanente","ip":"198.51.100.30","reason":"Manual block via API"}""");

        var raw = (await host.Db.StringGetAsync("security:blacklist:198.51.100.30")).ToString();
        raw.ShouldStartWith("\"");
        var inner = JsonSerializer.Deserialize<string>(raw)!;
        var data = Json(inner);
        data.GetProperty("ip").GetString().ShouldBe("198.51.100.30");
        data.GetProperty("reason").GetString().ShouldBe("Manual block via API");
        data.GetProperty("permanent").GetBoolean().ShouldBeTrue();
        var ttl = (await host.Db.KeyTimeToLiveAsync("security:blacklist:198.51.100.30"))!.Value.TotalSeconds;
        ttl.ShouldBeInRange(31_535_000, 31_536_000);

        var (blockedStatus, blockedBody) = await host.FromExternal("198.51.100.30", HttpMethod.Get, "/");
        blockedStatus.ShouldBe(HttpStatusCode.Forbidden);
        blockedBody.ShouldBe("""{"error":"Access permanently denied","message":"Your IP has been permanently blocked"}""");

        logs.Entries.ShouldContain(e => e.Category == "MeuSecretario.Security" && e.Level == LogLevel.Warning
            && e.Message == "[SECURITY-BLACKLIST] IP adicionado à blacklist permanente: 198.51.100.30 | Razão: Manual block via API");
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_CustomAndNullReason()
    {
        var host = Host();

        (await host.Post("/admin/security-blacklist-add", """{"ip":"198.51.100.31","reason":"scanner"}""")).Body.ShouldBe(
            """{"status":"sucesso","mensagem":"IP 198.51.100.31 adicionado à blacklist permanente","ip":"198.51.100.31","reason":"scanner"}""");
        (await host.Post("/admin/security-blacklist-add", """{"ip":"198.51.100.32","reason":null}""")).Body.ShouldBe(
            """{"status":"sucesso","mensagem":"IP 198.51.100.32 adicionado à blacklist permanente","ip":"198.51.100.32","reason":null}""");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("""{"ip":""}""")]
    [InlineData("""{"ip":null}""")]
    [InlineData("""{"ip":123}""")]
    [InlineData("""{"ip":true}""")]
    [InlineData("[1]")]
    [InlineData("{quebrado")]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_MissingOrNonTextIp_Is400(string? json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-blacklist-add", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campos obrigatórios faltando: ip"));
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "078")]
    public async Task BlacklistAdd_TextThatIsNotAnIp_Is400()
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-blacklist-add", """{"ip":"não-é-ip"}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("IP inválido"));
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"ip":"198.51.100.33","reason":5}""")]
    [InlineData("""{"ip":"198.51.100.33","reason":["x"]}""")]
    [InlineData("""{"ip":"198.51.100.33","reason":true}""")]
    [Trait("Desvio", "079")]
    public async Task BlacklistAdd_NonTextReason_Is400(string json)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-blacklist-add", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'reason' deve ser texto"));
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistAdd_WithoutRedis_Is500WithTheFixedText_AndLogsAnError()
    {
        var logs = new CapturedLogs();
        var host = Host(new Dictionary<string, string> { ["REDIS_URL"] = AdminHttp.RedisDown }, s => s.AddCapturedLogs(logs));

        var (status, body) = await host.Post("/admin/security-blacklist-add", """{"ip":"198.51.100.34"}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Falha ao adicionar IP à blacklist (Redis indisponível)"));
        logs.Entries.ShouldContain(e => e.Level == LogLevel.Error
            && e.Message == "[SECURITY-BLACKLIST] Redis indisponível, não foi possível adicionar IP à blacklist: 198.51.100.34");
    }

    // ---- 4. security-blacklist-remove -------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistRemove_DeletesTheKey_AndSucceedsEvenIfItWasNotThere()
    {
        var host = Host();
        await SeedRaw(host, "security:blacklist:198.51.100.40", "{}");

        var (status, body) = await host.Post("/admin/security-blacklist-remove", """{"ip":"198.51.100.40"}""");
        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"IP 198.51.100.40 removido da blacklist permanente","ip":"198.51.100.40"}""");
        (await host.SecurityKeys()).ShouldBeEmpty();

        (await host.Post("/admin/security-blacklist-remove", """{"ip":"198.51.100.40"}""")).Status.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null, "Campos obrigatórios faltando: ip")]
    [InlineData("""{"ip":""}""", "Campos obrigatórios faltando: ip")]
    [InlineData("""{"ip":7}""", "Campos obrigatórios faltando: ip")]
    [InlineData("""{"ip":"nada"}""", "IP inválido")]
    [Trait("AC", "009-24")]
    public async Task BlacklistRemove_InvalidInput_Is400(string? json, string message)
    {
        var host = Host();

        var (status, body) = await host.Post("/admin/security-blacklist-remove", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error(message));
    }

    [Fact]
    [Trait("AC", "009-24")]
    public async Task BlacklistRemove_WithoutRedis_Is500WithTheFixedText()
    {
        var host = RedisDownHost();

        var (status, body) = await host.Post("/admin/security-blacklist-remove", """{"ip":"198.51.100.40"}""");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldBe(AdminHttp.Error("Falha ao remover IP da blacklist (Redis indisponível ou IP não estava na blacklist)"));
    }

    // ---- 009-0 × 009a: filtro e chave ------------------------------------------------------------------------------

    [Fact]
    [Trait("AC", "009-08")]
    public async Task RegisteredAdminRoute_WithSuspiciousSegment_ReachesTheHandler_FromAnExternalIp()
    {
        var host = Host();

        // /admin/config-alertas-financeiros casa com o padrão "/config" do Flask; agora é rota registrada (desvio 005).
        var (status, body) = await host.FromExternal("203.0.113.9", HttpMethod.Post, "/admin/config-alertas-financeiros", "{}");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campos obrigatórios faltando: usuario_id"));
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task WrongKey_FromAnExternalIp_CountsAsAttempt_AndTheFifthBlocksFor1h()
    {
        var host = Host();
        const string external = "203.0.113.60";

        for (var i = 1; i <= 5; i++)
        {
            var (status, body) = await host.FromExternal(external, HttpMethod.Post, "/admin/security-unblock-ip", "{}", key: "errada");
            status.ShouldBe(HttpStatusCode.Unauthorized);
            body.ShouldBe(AdminHttp.Error("Não autorizado"));
        }

        (await host.SecurityKeys()).ShouldBe([$"security:blocked:{external}"]);
        var ttl = (await host.Db.KeyTimeToLiveAsync($"security:blocked:{external}"))!.Value.TotalSeconds;
        ttl.ShouldBeInRange(3590, 3600);

        var (blockedStatus, blockedBody) = await host.FromExternal(external, HttpMethod.Post, "/admin/security-unblock-ip", """{"ip":"1.2.3.4"}""");
        blockedStatus.ShouldBe(HttpStatusCode.Forbidden);
        blockedBody.ShouldBe("""{"error":"Access denied","message":"Your IP has been temporarily blocked due to suspicious activity"}""");
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task WrongKey_RecordsTheReason_AndMissingKeyAlsoCounts()
    {
        var host = Host();
        const string external = "203.0.113.61";

        await host.FromExternal(external, HttpMethod.Get, "/admin/security-stats", key: null);
        await host.FromExternal(external, HttpMethod.Get, "/admin/security-stats", key: "");

        var raw = (await host.Db.StringGetAsync($"security:attempts:{external}")).ToString();
        var attempts = Json(JsonSerializer.Deserialize<string>(raw)!).EnumerateArray().ToList();
        attempts.Count.ShouldBe(2);
        attempts.ShouldAllBe(a => a.GetProperty("reason").GetString() == "Invalid API key");
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task WrongKey_FromAnInternalCall_NeverCounts()
    {
        var host = Host();

        for (var i = 0; i < 7; i++)
        {
            (await host.Post("/admin/security-unblock-ip", "{}", key: "errada")).Status.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task ValidKey_FromAnExternalIp_NeverCounts()
    {
        var host = Host();

        for (var i = 0; i < 7; i++)
        {
            (await host.FromExternal("203.0.113.62", HttpMethod.Get, "/admin/security-stats")).Status.ShouldBe(HttpStatusCode.OK);
        }

        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task WrongKey_WithBotProtectionOff_DoesNotCount_AndStillReturns401()
    {
        var host = Host(new Dictionary<string, string> { ["BOT_PROTECTION_ENABLED"] = "false" });

        var (status, _) = await host.FromExternal("203.0.113.63", HttpMethod.Get, "/admin/security-stats", key: "errada");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.SecurityKeys()).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "083")]
    public async Task WrongKey_WhenTheStoreFails_StillReturns401()
    {
        var host = Host(services: s =>
        {
            s.RemoveAllSecurityStores();
            s.AddSingleton<ISecurityStore, ThrowingSecurityStore>();
        });

        var (status, body) = await host.FromExternal("203.0.113.64", HttpMethod.Get, "/admin/security-stats", key: "errada");

        // O filtro (fail-open) e a contagem (fail-open) falham em silêncio; a chave errada continua 401.
        status.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldBe(AdminHttp.Error("Não autorizado"));
    }

    [Fact]
    [Trait("AC", "009-21")]
    public async Task WrongKey_DoesNotRunTheRoute()
    {
        var host = Host();

        var (status, _) = await host.Post("/admin/security-blacklist-add", """{"ip":"198.51.100.50"}""", key: "errada");

        status.ShouldBe(HttpStatusCode.Unauthorized);
        (await host.SecurityKeys()).ShouldBeEmpty();
    }
}

internal static class SecurityStoreServiceExtensions
{
    public static void RemoveAllSecurityStores(this IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(ISecurityStore)).ToList())
        {
            services.Remove(descriptor);
        }
    }
}
