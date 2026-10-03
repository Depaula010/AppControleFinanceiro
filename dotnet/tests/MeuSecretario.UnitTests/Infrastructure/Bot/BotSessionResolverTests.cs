using System.Net;
using MeuSecretario.Application.Caching;

namespace MeuSecretario.UnitTests.Infrastructure.Bot;

public class BotSessionResolverTests
{
    private static readonly string Key = CacheKeys.BotSessionId("sessao-principal");

    private static BotRig WithSessions(params (string Name, string Id)[] sessions) =>
        new BotRig().Serve(_ => BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(sessions)));

    [Fact]
    [Trait("AC", "010-20")]
    public async Task Resolve_EmptyCache_QueriesTheBotWithApiKeyHeader_AndCachesTheIdForTenMinutes()
    {
        var rig = WithSessions(("outra", "id-outra"), ("sessao-principal", "id-1"));

        var id = await rig.Resolver.ResolveAsync(false, CancellationToken.None);

        id.ShouldBe("id-1");
        var get = rig.Gets.Single();
        get.Uri.ToString().ShouldBe("http://bot.test:3000/api/v1/sessions");
        get.Headers["X-API-Key"].ShouldBe("chave-do-bot");
        rig.Cache.RawValue(Key).ShouldBe("\"id-1\"");
        rig.Cache.TtlOf(Key)!.Value.TotalSeconds.ShouldBeInRange(599, 600);
        rig.Factory.ClientNames.ShouldAllBe(n => n == "bot-text");
    }

    [Fact]
    [Trait("AC", "010-21")]
    public async Task Resolve_IdInCache_DoesNotQueryTheBot()
    {
        var rig = WithSessions(("sessao-principal", "id-novo"));
        await rig.Cache.SetJsonAsync(Key, "id-cache", TimeSpan.FromSeconds(60));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBe("id-cache");

        rig.Handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Resolve_ForceRefresh_IgnoresCacheAndOverwritesIt()
    {
        var rig = WithSessions(("sessao-principal", "id-novo"));
        await rig.Cache.SetJsonAsync(Key, "id-velho", TimeSpan.FromSeconds(60));

        (await rig.Resolver.ResolveAsync(true, CancellationToken.None)).ShouldBe("id-novo");

        rig.Gets.Count().ShouldBe(1);
        rig.Cache.RawValue(Key).ShouldBe("\"id-novo\"");
    }

    [Fact]
    [Trait("AC", "010-22")]
    public async Task Resolve_SessionIdOverride_SkipsCacheAndBot()
    {
        var rig = WithSessions(("sessao-principal", "id-bot"));
        rig.Settings.BotSessionId = "id-fixo";

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBe("id-fixo");
        (await rig.Resolver.ResolveAsync(true, CancellationToken.None)).ShouldBe("id-fixo");

        rig.Handler.Requests.ShouldBeEmpty();
        rig.Cache.RawValue(Key).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-20")]
    public async Task Resolve_RequiresExactSessionName_AndTakesTheFirstMatch()
    {
        var rig = WithSessions(("sessao-principal-2", "id-errado"), ("Sessao-Principal", "id-caixa"), ("sessao-principal", "id-certo"), ("sessao-principal", "id-duplicado"));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBe("id-certo");
    }

    [Fact]
    [Trait("AC", "010-29")]
    public async Task Resolve_NoMatch_ReturnsNullAndDoesNotCache()
    {
        var rig = WithSessions(("outra", "id-outra"));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBeNull();
        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBeNull();

        rig.Gets.Count().ShouldBe(2);
        rig.Cache.RawValue(Key).ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "010-33")]
    [InlineData(401)]
    [InlineData(500)]
    public async Task Resolve_HttpError_ReturnsNull(int status)
    {
        var rig = new BotRig().Serve(_ => BotRig.Json((HttpStatusCode)status, "{\"error\":\"segredo-do-corpo\"}"));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBeNull();

        rig.Cache.RawValue(Key).ShouldBeNull();
        rig.Logs.ShouldNotContain("segredo-do-corpo");
        rig.Logs.ShouldNotContain("chave-do-bot");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":[{\"session_name\":\"sessao-principal\"}]}")]
    public async Task Resolve_UnexpectedBody_ReturnsNull(string body)
    {
        var rig = new BotRig().Serve(_ => BotRig.Json(HttpStatusCode.OK, body));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "010-33")]
    public async Task Resolve_NetworkException_ReturnsNull()
    {
        var rig = new BotRig();
        rig.Handler.EnqueueException(new HttpRequestException("falha com chave-do-bot"));

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBeNull();

        rig.Logs.ShouldNotContain("chave-do-bot");
        rig.Logs.ShouldContain("HttpRequestException");
    }

    [Fact]
    [Trait("AC", "010-33")]
    public async Task Resolve_CacheUnavailable_StillResolvesOnEveryCall()
    {
        var rig = WithSessions(("sessao-principal", "id-1"));
        rig.Cache.Unavailable = true;

        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBe("id-1");
        (await rig.Resolver.ResolveAsync(false, CancellationToken.None)).ShouldBe("id-1");

        rig.Gets.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Resolve_EmptyApiKey_StillSendsTheHeader()
    {
        var rig = WithSessions(("sessao-principal", "id-1"));
        rig.Settings.BotApiKey = string.Empty;

        await rig.Resolver.ResolveAsync(false, CancellationToken.None);

        rig.Gets.Single().Headers["X-API-Key"].ShouldBe(string.Empty);
    }
}
