using MeuSecretario.IntegrationTests.Support;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Desvio 261: cada resposta do webhook devolve um <c>X-Correlation-Id</c> (o mesmo que prefixa as linhas de log daquela mensagem).</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public sealed class WebhookCorrelationHeaderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private WhatsAppEnv _env = default!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        await _env.SeedUserAsync();
    }

    public async Task DisposeAsync() => await _env.Factory.DisposeAsync();

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryResponse_CarriesACorrelationId_ThatChangesPerMessage()
    {
        _env.Llm.Then("""{"intent": "Menu de Ajuda"}""").Then("""{"intent": "Menu de Ajuda"}""");
        using var client = _env.Factory.CreateClient();

        async Task<string> IdAsync()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative))
            {
                Content = new StringContent(
                    $$"""{"texto": "menu", "numero_remetente": "{{WhatsAppEnv.Number}}@s.whatsapp.net"}""", System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.TryAddWithoutValidation("x-api-key", WhatsAppEnv.ApiKey);
            using var response = await client.SendAsync(request);
            return response.Headers.GetValues("X-Correlation-Id").Single();
        }

        var first = await IdAsync();
        var second = await IdAsync();

        first.ShouldMatch("^[0-9a-f]{8}$");
        second.ShouldNotBe(first);
    }
}
