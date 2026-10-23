using System.Net;
using System.Text.Json.Nodes;
using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Desvio 259: o bot reenvia a mesma mensagem (mesmo <c>message_id</c>) e o webhook responde vazio sem reprocessar.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class WebhookIdempotencyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Help = """{"intent": "Menu de Ajuda"}""";

    private WhatsAppEnv _env = default!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        await _env.SeedUserAsync();
    }

    public async Task DisposeAsync() => await _env.Factory.DisposeAsync();

    private Task<(HttpStatusCode Status, string Body)> SendAsync(string? messageId) => _env.PostJsonAsync(
        messageId is null
            ? new { texto = "menu", numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" }
            : new { texto = "menu", numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net", message_id = messageId });

    private static string Reply(string body) => JsonNode.Parse(body)!["resposta"]!.GetValue<string>();

    [Fact]
    [Trait("AC", "012-017")]
    public async Task TheSameMessageId_IsProcessedOnce_AndTheRepeatAnswersEmpty()
    {
        _env.Llm.Then(Help);

        var first = await SendAsync("3EB0AAA1");
        var repeat = await SendAsync("3EB0AAA1");

        first.Status.ShouldBe(HttpStatusCode.OK);
        Reply(first.Body).ShouldContain("MENU DE FUNCIONALIDADES");
        repeat.Status.ShouldBe(HttpStatusCode.OK);
        Reply(repeat.Body).ShouldBe(string.Empty);
        _env.Llm.Requests.Count.ShouldBe(1);
    }

    [Fact]
    [Trait("AC", "012-017")]
    public async Task AnotherMessageId_OrNoId_IsProcessedNormally()
    {
        _env.Llm.Then(Help).Then(Help).Then(Help).Then(Help);

        Reply((await SendAsync("3EB0AAA1")).Body).ShouldContain("MENU DE FUNCIONALIDADES");
        Reply((await SendAsync("3EB0BBB2")).Body).ShouldContain("MENU DE FUNCIONALIDADES");
        Reply((await SendAsync(null)).Body).ShouldContain("MENU DE FUNCIONALIDADES");
        Reply((await SendAsync(null)).Body).ShouldContain("MENU DE FUNCIONALIDADES");

        _env.Llm.Requests.Count.ShouldBe(4);
    }

    [Fact]
    [Trait("AC", "012-017")]
    public async Task AFailedAttempt_ReleasesTheId_SoTheBotsRetryIsProcessed()
    {
        _env.Llm.ThenThrow(new InvalidOperationException("falha qualquer")).Then(Help);

        var failed = await SendAsync("3EB0CCC3");
        var retry = await SendAsync("3EB0CCC3");

        failed.Status.ShouldBe(HttpStatusCode.InternalServerError);
        retry.Status.ShouldBe(HttpStatusCode.OK);
        Reply(retry.Body).ShouldContain("MENU DE FUNCIONALIDADES");
    }
}
