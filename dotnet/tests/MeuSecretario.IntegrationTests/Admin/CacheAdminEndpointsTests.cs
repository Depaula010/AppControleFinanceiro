using System.Net;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.IntegrationTests.Security;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.IntegrationTests.Admin;

/// <summary><c>gemini-cache-clear</c> e <c>gemini-cache-stats</c> com o <c>LlmCacheAdmin</c> real e Redis em container (T14, T15).</summary>
[Trait("Category", "Docker")]
public sealed class CacheAdminEndpointsTests(RedisFixture redis) : IClassFixture<RedisFixture>, IAsyncLifetime
{
    private static readonly string[] AllKeys =
    [
        "gemini_cache:category:7:aaa",
        "gemini_cache:intent:global:bbb",
        "gemini_cache:intent:7:ccc",
    ];

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
        var host = new SecurityTestHost(redis.RedisUrl, redis.ConnectionString + ",allowAdmin=true", settings);
        _hosts.Add(host);
        return host;
    }

    private static async Task Seed(SecurityTestHost host)
    {
        await host.Flush();
        foreach (var key in AllKeys)
        {
            await host.Db.StringSetAsync(key, "\"valor\"");
        }
    }

    private static async Task<string[]> Remaining(SecurityTestHost host) =>
        [.. AllKeys.Where(key => host.Db.KeyExists(key)).Order(StringComparer.Ordinal)];

    [Fact]
    [Trait("AC", "009-25")]
    public async Task UserWithPureType_DeletesOnlyThatTypeOfThatUser()
    {
        var host = Host();
        await Seed(host);

        var (status, body) = await host.Post("/admin/gemini-cache-clear", """{"usuario_id":7,"pattern":"category"}""");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"1 chaves deletadas para usuário 7","keys_deleted":1}""");
        (await Remaining(host)).ShouldBe(["gemini_cache:intent:7:ccc", "gemini_cache:intent:global:bbb"]);
    }

    [Fact]
    [Trait("AC", "009-25")]
    public async Task UserWithTheTypeWildcard_DeletesNothing_ReproducingTheFlaskFormat()
    {
        var host = Host();
        await Seed(host);

        var (_, body) = await host.Post("/admin/gemini-cache-clear", """{"usuario_id":7,"pattern":"category:*"}""");

        body.ShouldBe("""{"status":"sucesso","mensagem":"0 chaves deletadas para usuário 7","keys_deleted":0}""");
        (await Remaining(host)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task UserWithoutPattern_DeletesEveryTypeOfThatUser()
    {
        var host = Host();
        await Seed(host);

        var (_, body) = await host.Post("/admin/gemini-cache-clear", """{"usuario_id":7}""");

        body.ShouldBe("""{"status":"sucesso","mensagem":"2 chaves deletadas para usuário 7","keys_deleted":2}""");
        (await Remaining(host)).ShouldBe(["gemini_cache:intent:global:bbb"]);
    }

    [Theory]
    [InlineData("intent:*")]
    [InlineData("intent")]
    [Trait("AC", "009-25")]
    public async Task PatternWithoutUser_AcceptsBothForms(string pattern)
    {
        var host = Host();
        await Seed(host);

        var (_, body) = await host.Post("/admin/gemini-cache-clear", $$"""{"pattern":"{{pattern}}"}""");

        body.ShouldBe($$"""{"status":"sucesso","mensagem":"2 chaves deletadas (pattern: {{pattern}})","keys_deleted":2}""");
        (await Remaining(host)).ShouldBe(["gemini_cache:category:7:aaa"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"usuario_id":0,"pattern":""}""")]
    [InlineData("""{"usuario_id":null,"pattern":null}""")]
    [Trait("AC", "009-25")]
    [Trait("Desvio", "077")]
    public async Task EmptyBodyOrNoFields_ClearsEverything(string? json)
    {
        var host = Host();
        await Seed(host);

        var (status, body) = await host.Post("/admin/gemini-cache-clear", json);

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"Cache completo limpo: 3 chaves deletadas","keys_deleted":3}""");
        (await Remaining(host)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Desvio", "077")]
    public async Task EmptyBody_WithoutAContentType_AlsoClearsEverything()
    {
        var host = Host();
        await Seed(host);

        var (status, _) = await host.Admin(HttpMethod.Post, "/admin/gemini-cache-clear", string.Empty, contentType: "text/plain");

        status.ShouldBe(HttpStatusCode.OK);
        (await Remaining(host)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"texto\"")]
    [InlineData("null")]
    [InlineData("123")]
    [InlineData("isto não é json")]
    [InlineData("{quebrado")]
    [InlineData(" ")]
    [Trait("AC", "009-25")]
    [Trait("Desvio", "077")]
    public async Task NonEmptyBodyThatIsNotAnObject_Is400_AndClearsNothing(string json)
    {
        var host = Host();
        await Seed(host);

        var (status, body) = await host.Post("/admin/gemini-cache-clear", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Corpo da requisição deve ser um objeto JSON"));
        (await Remaining(host)).Length.ShouldBe(3);
    }

    [Theory]
    [InlineData("""{"usuario_id":"7"}""")]
    [InlineData("""{"usuario_id":-3}""")]
    [InlineData("""{"usuario_id":1.5}""")]
    [InlineData("""{"usuario_id":true}""")]
    [InlineData("""{"usuario_id":[7]}""")]
    [InlineData("""{"usuario_id":99999999999}""")]
    [Trait("Desvio", "079")]
    public async Task TruthyUserIdThatIsNotAPositiveInteger_Is400_AndClearsNothing(string json)
    {
        var host = Host();
        await Seed(host);

        var (status, body) = await host.Post("/admin/gemini-cache-clear", json);

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'usuario_id' deve ser um número inteiro"));
        (await Remaining(host)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task NonTextPattern_Is400_AndDoesNotClearEverything()
    {
        var host = Host();
        await Seed(host);

        var (status, body) = await host.Post("/admin/gemini-cache-clear", """{"pattern":5}""");

        status.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldBe(AdminHttp.Error("Campo 'pattern' deve ser texto"));
        (await Remaining(host)).Length.ShouldBe(3);
    }

    [Fact]
    public async Task WithoutRedis_Answers200WithZeroKeys()
    {
        var host = Host(new Dictionary<string, string> { ["REDIS_URL"] = AdminHttp.RedisDown });

        var (status, body) = await host.Post("/admin/gemini-cache-clear", "{}");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe("""{"status":"sucesso","mensagem":"Cache completo limpo: 0 chaves deletadas","keys_deleted":0}""");
    }

    [Fact]
    [Trait("AC", "009-26")]
    public async Task Stats_ReturnTheInstanceMetricsInTheSpecFormat()
    {
        var host = Host();
        var metrics = host.Services.GetRequiredService<LlmCacheMetricsCollector>();
        metrics.RecordHit("intent");
        metrics.RecordHit("intent");
        metrics.RecordMiss("intent");
        metrics.RecordSave();

        var (status, body) = await host.Get("/admin/gemini-cache-stats");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldBe(
            """{"status":"sucesso","mensagem":"Estatísticas do cache obtidas com sucesso","total_requests":3,"cache_hits":2,"cache_misses":1,"cache_saves":1,"cache_errors":0,"hit_rate":"66.7%","breakdown_by_type":{"intent":{"hits":2,"misses":1,"total":3,"hit_rate":"66.7%"},"category":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"extract_trans":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"period_query":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"calendar_query":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"chart_type":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"fatura_query":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"saldo_query":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"},"other":{"hits":0,"misses":0,"total":0,"hit_rate":"0.0%"}},"estimated_savings":{"calls_saved":2,"quota_saved_pct":"66.7%"}}""");
    }

    [Fact]
    [Trait("Desvio", "004")]
    public async Task Stats_WorkWithoutRedis_AndStartAtZero()
    {
        var host = Host(new Dictionary<string, string> { ["REDIS_URL"] = AdminHttp.RedisDown });

        var (status, body) = await host.Get("/admin/gemini-cache-stats");

        status.ShouldBe(HttpStatusCode.OK);
        body.ShouldStartWith(
            """{"status":"sucesso","mensagem":"Estatísticas do cache obtidas com sucesso","total_requests":0,"cache_hits":0,"cache_misses":0,"cache_saves":0,"cache_errors":0,"hit_rate":"0.0%","breakdown_by_type":{"intent":""");
    }
}
