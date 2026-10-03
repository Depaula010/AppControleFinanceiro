using System.Net;
using System.Text;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Bot;
using MeuSecretario.Infrastructure.Llm;
using MeuSecretario.UnitTests.Infrastructure.Bot;
using MeuSecretario.UnitTests.TestSupport;

namespace MeuSecretario.UnitTests.Infrastructure.Llm;

public class GeminiTransportTests
{
    private const string Ok = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"resposta\"}]}}]}";

    private static (GeminiHttpTransport Transport, FakeHttpHandler Handler, ListLogger<GeminiHttpTransport> Log, FakeIntegrationSettings Settings) Build()
    {
        var handler = new FakeHttpHandler();
        var settings = new FakeIntegrationSettings { GeminiModel = "gemini-2.5-flash-lite" };
        var log = new ListLogger<GeminiHttpTransport>();
        return (new GeminiHttpTransport(new StubHttpClientFactory(handler), settings, log), handler, log, settings);
    }

    [Fact]
    [Trait("AC", "010-40")]
    public async Task Generate_SendsTheExactRequest()
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, Ok);
        const string prompt = "Olá, ação 😀\nsegunda \"linha\"";

        var text = await transport.GenerateAsync(prompt, "chave-A", CancellationToken.None);

        text.ShouldBe("resposta");
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash-lite:generateContent");
        request.Headers["x-goog-api-key"].ShouldBe("chave-A");
        request.Headers["Content-Type"].ShouldBe("application/json");
        FakeHttpHandler.AssertJsonBody(request, JsonSerializer.Serialize(new { contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } } }));
        using var body = JsonDocument.Parse(request.BodyText!);
        body.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["contents"]);
        body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().ShouldBe(prompt);
    }

    [Fact]
    [Trait("AC", "010-41")]
    public async Task Generate_ModelOverride_ChangesOnlyTheModelSegment()
    {
        var (transport, handler, _, settings) = Build();
        settings.GeminiModel = "gemini-teste-9";
        handler.Enqueue(HttpStatusCode.OK, Ok);

        await transport.GenerateAsync("x", "k", CancellationToken.None);

        handler.Requests.Single().Uri.ToString().ShouldBe("https://generativelanguage.googleapis.com/v1beta/models/gemini-teste-9:generateContent");
    }

    [Fact]
    [Trait("AC", "010-41")]
    public async Task Generate_EmptyModel_UsesTheDefault()
    {
        var (transport, handler, _, settings) = Build();
        settings.GeminiModel = string.Empty;
        handler.Enqueue(HttpStatusCode.OK, Ok);

        await transport.GenerateAsync("x", "k", CancellationToken.None);

        handler.Requests.Single().Uri.AbsolutePath.ShouldEndWith("models/gemini-2.5-flash-lite:generateContent");
    }

    [Fact]
    [Trait("AC", "010-42")]
    public async Task Generate_ConcurrentCallsWithDifferentKeys_EachSendsItsOwnKey()
    {
        var (transport, handler, _, _) = Build();
        var rng = new Random(7);
        handler.Enqueue(async (request, ct) =>
        {
            await Task.Delay(rng.Next(5, 40), ct);
            var key = request.Headers["x-goog-api-key"];
            var json = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = key } } } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        });

        var keys = new[] { "chave-usuario-1", "chave-usuario-2", "chave-sistema" };
        var tasks = Enumerable.Range(0, 30).Select(i => keys[i % 3]).Select(async key =>
            (Key: key, Echo: await transport.GenerateAsync("p", key, CancellationToken.None))).ToList();
        var results = await Task.WhenAll(tasks);

        results.ShouldAllBe(r => r.Key == r.Echo);
        handler.Requests.Count.ShouldBe(30);
        handler.Requests.Select(r => r.Headers["x-goog-api-key"]).Distinct().Count().ShouldBe(3);
    }

    [Fact]
    [Trait("AC", "010-45")]
    public async Task Generate_ConcatenatesTextParts()
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"a\"},{\"text\":\"b\"}]}}]}");

        (await transport.GenerateAsync("x", "k", CancellationToken.None)).ShouldBe("ab");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"\"}]}}]}")]
    [Trait("AC", "010-45")]
    public async Task Generate_NoCandidateOrNoText_IsBlocked(string body)
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, body);

        var ex = await Should.ThrowAsync<LlmBlockedException>(() => transport.GenerateAsync("x", "k", CancellationToken.None));

        ex.Message.ShouldBe("Falha na API: Resposta do Gemini bloqueada. sem detalhes");
    }

    [Fact]
    [Trait("AC", "010-45")]
    public async Task Generate_BlockReason_IsInTheMessage()
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}");

        var ex = await Should.ThrowAsync<LlmBlockedException>(() => transport.GenerateAsync("x", "k", CancellationToken.None));

        ex.Message.ShouldBe("Falha na API: Resposta do Gemini bloqueada. SAFETY");
    }

    [Theory]
    [InlineData(429, "")]
    [InlineData(429, "{\"error\":{\"code\":429,\"status\":\"RESOURCE_EXHAUSTED\",\"message\":\"x\"}}")]
    [InlineData(400, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}")]
    [InlineData(500, "{\"error\":{\"message\":\"You exceeded your current Quota\"}}")]
    [Trait("AC", "010-46")]
    public async Task Generate_Quota_MapsToTheLiteralMessage_WithASingleRequest(int status, string body)
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue((HttpStatusCode)status, body);

        var ex = await Should.ThrowAsync<LlmQuotaExceededException>(() => transport.GenerateAsync("x", "k", CancellationToken.None));

        ex.Message.ShouldBe("429 Quota exceeded - Gemini API rate limit reached");
        handler.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(403, "")]
    [InlineData(400, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}")]
    [InlineData(401, "{\"error\":{\"message\":\"Permission denied on resource\"}}")]
    [Trait("AC", "010-47")]
    public async Task Generate_PermissionDenied_MapsToTheLiteralMessage(int status, string body)
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue((HttpStatusCode)status, body);

        var ex = await Should.ThrowAsync<LlmPermissionDeniedException>(() => transport.GenerateAsync("x", "k", CancellationToken.None));

        ex.Message.ShouldBe("403 Permission denied - Invalid API key or permissions");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(500)]
    [InlineData(503)]
    [Trait("AC", "010-48")]
    public async Task Generate_OtherFailures_AreGenericAndLeakNothing(int status)
    {
        var (transport, handler, log, _) = Build();
        handler.Enqueue((HttpStatusCode)status, "{\"error\":{\"message\":\"API key not valid: segredo-no-corpo\"}}");

        var ex = await Should.ThrowAsync<LlmException>(() => transport.GenerateAsync("prompt-secreto", "chave-secreta", CancellationToken.None));

        ex.GetType().ShouldBe(typeof(LlmException));
        ex.Message.ShouldBe($"Falha na API do Gemini (HTTP {status}).");
        (ex.Message + log.All).ShouldNotContain("segredo");
        (ex.Message + log.All).ShouldNotContain("chave-secreta");
        (ex.Message + log.All).ShouldNotContain("prompt-secreto");
    }

    [Fact]
    [Trait("AC", "010-48")]
    public async Task Generate_NetworkFailure_BecomesLlmException_WithoutTheUrlOrKeyInLogs()
    {
        var (transport, handler, log, _) = Build();
        handler.EnqueueException(new HttpRequestException("connect falhou em https://x?key=chave-secreta"));

        var ex = await Should.ThrowAsync<LlmException>(() => transport.GenerateAsync("p", "chave-secreta", CancellationToken.None));

        ex.Message.ShouldNotContain("chave-secreta");
        log.All.ShouldNotContain("chave-secreta");
        log.All.ShouldContain(nameof(HttpRequestException));
    }

    [Fact]
    public async Task Generate_CallerCancellation_Propagates()
    {
        var (transport, handler, _, _) = Build();
        handler.EnqueueDelayed(TimeSpan.FromSeconds(30), HttpStatusCode.OK, Ok);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(() => transport.GenerateAsync("p", "k", cts.Token));
    }

    [Fact]
    public async Task Generate_InvalidSuccessBody_IsGenericLlmException()
    {
        var (transport, handler, _, _) = Build();
        handler.Enqueue(HttpStatusCode.OK, "<html>nope</html>");

        var ex = await Should.ThrowAsync<LlmException>(() => transport.GenerateAsync("p", "k", CancellationToken.None));

        ex.GetType().ShouldBe(typeof(LlmException));
    }
}
