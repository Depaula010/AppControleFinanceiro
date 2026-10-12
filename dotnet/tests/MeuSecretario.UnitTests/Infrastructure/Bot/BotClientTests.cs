using System.Net;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.UnitTests.Infrastructure.Bot;

public class BotClientTests
{
    private const string Number = "5531999990000";
    private const string Message = "ola";
    private static readonly string Key = CacheKeys.BotSessionId("sessao-principal");

    /// <summary>Bot que lista a sessão <paramref name="id"/> e responde <paramref name="postStatus"/> a qualquer POST.</summary>
    private static BotRig Bot(HttpStatusCode postStatus = HttpStatusCode.OK, string id = "id-1", string postBody = "{}") =>
        new BotRig().Serve(r => r.Method == HttpMethod.Get
            ? BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("sessao-principal", id)))
            : BotRig.Json(postStatus, postBody));

    [Fact]
    [Trait("AC", "010-23")]
    public async Task SendText_PostsTheExactPayloadToTheV1Endpoint()
    {
        var rig = Bot();

        var ok = await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);

        ok.ShouldBeTrue();
        var post = rig.Posts.Single();
        post.Uri.ToString().ShouldBe("http://bot.test:3000/api/v1/sessions/id-1/send-message");
        post.Headers["X-API-Key"].ShouldBe("chave-do-bot");
        post.Headers["Content-Type"].ShouldBe("application/json");
        post.BodyText.ShouldBe("{\"numero\":\"5531999990000\",\"mensagem\":\"ola\"}");
        rig.Factory.ClientNames.Where(n => n == "bot-text").ShouldNotBeEmpty();
    }

    [Fact]
    [Trait("AC", "010-23")]
    public async Task SendText_KeepsAccentsAndEmojiAsTheCallerPassedThem()
    {
        var rig = Bot();

        await rig.Client.SendTextAsync(Number, "Saldo: R$ 10,00 ação 😀 \"x\"\nlinha", CancellationToken.None);

        using var body = JsonDocument.Parse(rig.Posts.Single().BodyText!);
        body.RootElement.GetProperty("mensagem").GetString().ShouldBe("Saldo: R$ 10,00 ação 😀 \"x\"\nlinha");
    }

    [Fact]
    [Trait("AC", "010-24")]
    public async Task SendImage_SendsBase64WithPaddingAndCaption_OnTheImageClient()
    {
        var rig = Bot();
        byte[] image = [1, 2, 3, 4, 5];

        var ok = await rig.Client.SendImageAsync(Number, image, "Gráfico", CancellationToken.None);

        ok.ShouldBeTrue();
        var post = rig.Posts.Single();
        post.Uri.ToString().ShouldBe("http://bot.test:3000/api/v1/sessions/id-1/send-image");
        post.Headers["X-API-Key"].ShouldBe("chave-do-bot");
        using var body = JsonDocument.Parse(post.BodyText!);
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["numero", "imagem", "legenda"]);
        body.RootElement.GetProperty("numero").GetString().ShouldBe(Number);
        body.RootElement.GetProperty("imagem").GetString().ShouldBe("AQIDBAU=");
        Convert.FromBase64String(body.RootElement.GetProperty("imagem").GetString()!).ShouldBe(image);
        body.RootElement.GetProperty("legenda").GetString().ShouldBe("Gráfico");
        rig.Factory.ClientNames.ShouldContain("bot-image");
    }

    [Fact]
    [Trait("AC", "010-24")]
    public async Task SendImage_WithoutCaption_SendsNullLegenda()
    {
        var rig = Bot();

        await rig.Client.SendImageAsync(Number, new byte[] { 9 }, null, CancellationToken.None);

        using var body = JsonDocument.Parse(rig.Posts.Single().BodyText!);
        body.RootElement.GetProperty("legenda").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [Trait("AC", "010-25")]
    [Trait("AC", "010-32")]
    [InlineData(201)]
    [InlineData(204)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Send_AnyStatusButOk_IsFalse_AndNoSensitiveDataIsLogged(int status)
    {
        var rig = Bot((HttpStatusCode)status, postBody: "{\"erro\":\"corpo-secreto-do-bot\"}");

        (await rig.Client.SendTextAsync(Number, "mensagem-privada", CancellationToken.None)).ShouldBeFalse();

        rig.Posts.Count().ShouldBe(1);
        AssertNoSensitiveData(rig);
        rig.Logs.ShouldContain(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    [Trait("AC", "010-26")]
    public async Task Send_404_DeletesTheCache_ResolvesAgain_AndRetriesOnce()
    {
        var sessions = 0;
        var rig = new BotRig().Serve(r =>
        {
            if (r.Method == HttpMethod.Get)
            {
                sessions++;
                return BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("sessao-principal", sessions == 1 ? "velho" : "novo")));
            }

            return new HttpResponseMessage(r.Uri.ToString().Contains("/sessions/velho/", StringComparison.Ordinal) ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        });
        await rig.Cache.SetJsonAsync(Key, "velho", TimeSpan.FromSeconds(60));
        sessions = 1;

        var ok = await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);

        ok.ShouldBeTrue();
        rig.Gets.Count().ShouldBe(1);
        rig.Posts.Select(p => p.Uri.AbsolutePath).ShouldBe(
            ["/api/v1/sessions/velho/send-message", "/api/v1/sessions/novo/send-message"]);
        rig.Cache.RawValue(Key).ShouldBe("\"novo\"");
    }

    [Fact]
    [Trait("AC", "010-26")]
    public async Task Send_404Twice_IsFalseWithExactlyTwoPosts()
    {
        var rig = Bot(HttpStatusCode.NotFound);

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeFalse();

        rig.Posts.Count().ShouldBe(2);
        rig.Gets.Count().ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-22")]
    public async Task Send_404WithSessionIdOverride_DoesNotRetry_AndTouchesNoCache()
    {
        var rig = Bot(HttpStatusCode.NotFound);
        rig.Settings.BotSessionId = "id-fixo";

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeFalse();

        rig.Handler.Requests.Count.ShouldBe(1);
        rig.Posts.Single().Uri.AbsolutePath.ShouldBe("/api/v1/sessions/id-fixo/send-message");
        rig.Cache.RawValue(Key).ShouldBeNull();
    }

    [Theory]
    [Trait("AC", "010-27")]
    [InlineData("401")]
    [InlineData("nomatch")]
    [InlineData("network")]
    public async Task Send_SessionNotResolved_FallsBackToTheLegacyEndpoint(string failure)
    {
        var rig = new BotRig();
        rig.Handler.Enqueue(r => r.Method == HttpMethod.Get
            ? failure switch
            {
                "401" => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                "nomatch" => BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("outra", "x"))),
                _ => throw new HttpRequestException("rede"),
            }
            : new HttpResponseMessage(HttpStatusCode.OK));

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeTrue();

        var post = rig.Posts.Single();
        post.Uri.ToString().ShouldBe("http://bot.test:3000/enviar-mensagem");
        post.Headers["x-api-key"].ShouldBe("segredo-da-api");
        post.Headers.ContainsKey("X-API-Key").ShouldBeTrue(); // cabeçalhos não diferenciam caixa
        post.BodyText.ShouldBe("{\"numero\":\"5531999990000\",\"mensagem\":\"ola\"}");
    }

    [Fact]
    [Trait("AC", "010-27")]
    public async Task SendImage_Legacy_UsesEnviarImagem()
    {
        var rig = new BotRig();
        rig.Handler.Enqueue(r => new HttpResponseMessage(r.Method == HttpMethod.Get ? HttpStatusCode.Unauthorized : HttpStatusCode.OK));

        (await rig.Client.SendImageAsync(Number, new byte[] { 1 }, "c", CancellationToken.None)).ShouldBeTrue();

        var post = rig.Posts.Single();
        post.Uri.AbsolutePath.ShouldBe("/enviar-imagem");
        post.Headers["x-api-key"].ShouldBe("segredo-da-api");
    }

    [Fact]
    [Trait("AC", "010-26")]
    public async Task Send_404OnLegacyEndpoint_ResolvesAgainAndRetriesOnce()
    {
        var rig = new BotRig();
        var gets = 0;
        rig.Handler.Enqueue(r =>
        {
            if (r.Method == HttpMethod.Get)
            {
                return ++gets == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    : BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("sessao-principal", "id-2")));
            }

            return new HttpResponseMessage(r.Uri.AbsolutePath == "/enviar-mensagem" ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        });

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeTrue();

        rig.Posts.Select(p => p.Uri.AbsolutePath).ShouldBe(["/enviar-mensagem", "/api/v1/sessions/id-2/send-message"]);
    }

    [Fact]
    [Trait("AC", "010-28")]
    public async Task Send_NetworkFailure_IsFalseWithASinglePost_AndNoRetry()
    {
        var rig = new BotRig();
        rig.Handler.Enqueue(r => r.Method == HttpMethod.Get
            ? BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("sessao-principal", "id-1")))
            : throw new HttpRequestException("Connection refused para mensagem-privada"));

        (await rig.Client.SendTextAsync(Number, "mensagem-privada", CancellationToken.None)).ShouldBeFalse();

        rig.Posts.Count().ShouldBe(1);
        AssertNoSensitiveData(rig);
        rig.Logs.ShouldContain("HttpRequestException");
    }

    [Fact]
    [Trait("AC", "010-28")]
    public async Task Send_Timeout_IsFalseWithoutThrowing()
    {
        var rig = new BotRig();
        rig.Handler.Enqueue(r => r.Method == HttpMethod.Get
            ? BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("sessao-principal", "id-1")))
            : throw new TaskCanceledException("timeout"));

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeFalse();

        rig.Posts.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Send_CallerCancellation_Propagates()
    {
        var rig = Bot();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => rig.Client.SendTextAsync(Number, Message, cts.Token));
    }

    [Fact]
    [Trait("AC", "010-29")]
    public async Task Send_SessionWithoutMatch_IsNotCached_SoTheNextSendQueriesAgain()
    {
        var rig = new BotRig().Serve(r => r.Method == HttpMethod.Get
            ? BotRig.Json(HttpStatusCode.OK, BotRig.SessionsBody(("outra", "x")))
            : new HttpResponseMessage(HttpStatusCode.OK));

        await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);
        await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);

        rig.Gets.Count().ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-30")]
    public async Task Send_EmptyUrl_IsFalseWithoutRequests_AndWarns()
    {
        var rig = Bot();
        rig.Settings.BotWhatsappUrl = " ";

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeFalse();
        (await rig.Client.SendImageAsync(Number, new byte[] { 1 }, null, CancellationToken.None)).ShouldBeFalse();

        rig.Handler.Requests.ShouldBeEmpty();
        rig.ClientLog.Messages.Count.ShouldBe(2);
        rig.ClientLog.All.ShouldContain("BOT_WHATSAPP_URL");
        rig.ClientLog.All.ShouldNotContain(Number);
    }

    [Fact]
    [Trait("AC", "010-31")]
    public async Task Send_UrlWithTrailingSlash_BuildsPathsWithoutDoubleSlash()
    {
        var rig = Bot();
        rig.Settings.BotWhatsappUrl = "http://bot.test:3000/";

        await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);

        rig.Gets.Single().Uri.ToString().ShouldBe("http://bot.test:3000/api/v1/sessions");
        rig.Posts.Single().Uri.ToString().ShouldBe("http://bot.test:3000/api/v1/sessions/id-1/send-message");
    }

    [Fact]
    [Trait("AC", "010-21")]
    public async Task Send_UsesTheCachedSession_WithoutQueryingTheBot()
    {
        var rig = Bot();
        await rig.Cache.SetJsonAsync(Key, "id-cache", TimeSpan.FromSeconds(60));

        await rig.Client.SendTextAsync(Number, Message, CancellationToken.None);

        rig.Gets.ShouldBeEmpty();
        rig.Posts.Single().Uri.AbsolutePath.ShouldBe("/api/v1/sessions/id-cache/send-message");
    }

    [Fact]
    [Trait("AC", "010-33")]
    public async Task Send_RedisDown_StillSends_ResolvingTheSessionEveryTime()
    {
        var rig = Bot();
        rig.Cache.Unavailable = true;

        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeTrue();
        (await rig.Client.SendTextAsync(Number, Message, CancellationToken.None)).ShouldBeTrue();

        rig.Gets.Count().ShouldBe(2);
        rig.Posts.Count().ShouldBe(2);
    }

    [Fact]
    [Trait("AC", "010-32")]
    public async Task Logs_NeverContainTheFullNumberTheMessageOrTheKeys_OnlyTheLastFourDigits()
    {
        var rig = Bot(HttpStatusCode.BadRequest, postBody: "corpo-secreto-do-bot");

        await rig.Client.SendTextAsync(Number, "mensagem-privada", CancellationToken.None);
        await rig.Client.SendImageAsync(Number, new byte[] { 1 }, "legenda-privada", CancellationToken.None);

        AssertNoSensitiveData(rig);
        rig.ClientLog.All.ShouldContain("…0000");
        rig.ClientLog.All.ShouldContain("imagem");
    }

    [Fact]
    public async Task SendText_LongMessage_IsSplitInLineBreaks_AndSentInSequence()
    {
        var first = new string('a', 3000);
        var second = new string('b', 3000);
        var rig = Bot();

        (await rig.Client.SendTextAsync(Number, first + "\n" + second, CancellationToken.None)).ShouldBeTrue();

        var messages = rig.Posts.Select(p => JsonDocument.Parse(p.BodyText!).RootElement.GetProperty("mensagem").GetString()).ToArray();
        messages.ShouldBe([first, second]);
    }

    [Fact]
    public async Task SendText_LongMessage_StopsAtTheFirstFailingPart()
    {
        var rig = Bot(HttpStatusCode.InternalServerError);

        (await rig.Client.SendTextAsync(Number, new string('a', 3000) + "\n" + new string('b', 3000), CancellationToken.None)).ShouldBeFalse();

        rig.Posts.Count().ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "010-24")]
    public void AddBotClient_RegistersTheTwoNamedClientsWithTheirTimeouts_AndNoKeyInDefaultHeaders()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICache>(new InMemoryCache());
        services.AddSingleton<IIntegrationSettings>(new FakeIntegrationSettings());
        services.AddBotClient();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        using var text = factory.CreateClient("bot-text");
        using var image = factory.CreateClient("bot-image");

        text.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        image.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        text.DefaultRequestHeaders.ShouldBeEmpty();
        image.DefaultRequestHeaders.ShouldBeEmpty();
        provider.GetRequiredService<IBotClient>().ShouldBeOfType<BotClient>();
        provider.GetRequiredService<IBotClient>().ShouldBeSameAs(provider.GetRequiredService<IBotClient>());
    }

    private static void AssertNoSensitiveData(BotRig rig)
    {
        var logs = rig.Logs;
        logs.ShouldNotContain("mensagem-privada");
        logs.ShouldNotContain("legenda-privada");
        logs.ShouldNotContain("corpo-secreto-do-bot");
        logs.ShouldNotContain("chave-do-bot");
        logs.ShouldNotContain("segredo-da-api");
        logs.ShouldNotContain(Number);
    }
}
