using System.Net;
using System.Text;
using System.Text.Json;
using MeuSecretario.Api.Whatsapp;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Ops;
using MeuSecretario.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Desvio 266: o webhook avisa o dono quando o Gemini recusa as chamadas (cota, saldo, chave, modelo).</summary>
[Trait("Category", "Docker")]
public sealed class WebhookGeminiAlertTests(PostgresFixture postgres) : IAsyncLifetime, IClassFixture<PostgresFixture>
{
    private WhatsAppEnv _env = default!;
    private readonly RecordingAlerts _alerts = new();
    private WebApplicationFactory<Program> _factory = default!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        await _env.SeedUserAsync();
        _factory = _env.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOpsAlerts>();
            services.AddSingleton<IOpsAlerts>(_alerts);
        }));
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _env.Factory.DisposeAsync();
    }

    private async Task<HttpStatusCode> SendAsync(string text)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { texto = text, numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" }), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("x-api-key", WhatsAppEnv.ApiKey);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task GeminiQuota_AnswersTheUserAnd_AlertsTheOwner_WithoutTheMessageText()
    {
        _env.Llm.ThenThrow(new LlmQuotaExceededException("429 Quota exceeded - Gemini API rate limit reached"));

        var status = await SendAsync("gastei 21 reais na padaria");

        status.ShouldBe((HttpStatusCode)429);
        var alert = _alerts.Raised.ShouldHaveSingleItem();
        alert.Source.ShouldBe(WebhookFailureAlert.QuotaSource);
        alert.Message.ShouldNotContain("padaria");
        alert.Message.ShouldNotContain(WhatsAppEnv.Number);
    }

    [Fact]
    public async Task GeminiModelGone_AlertsWithTheModelHint()
    {
        _env.Llm.ThenThrow(new LlmException("Falha na API do Gemini (HTTP 404)."));

        var status = await SendAsync("gastei 21 reais na padaria");

        status.ShouldBe(HttpStatusCode.InternalServerError);
        var alert = _alerts.Raised.ShouldHaveSingleItem();
        alert.Source.ShouldBe(WebhookFailureAlert.ModelSource);
        alert.Message.ShouldContain("gemini.modelo");
    }

    [Fact]
    public async Task Menu_DoesNotTouchTheGemini_AndRaisesNothing()
    {
        // Sem roteirizar o Gemini: se o menu o chamasse, o teste quebraria.
        var status = await SendAsync("menu");

        status.ShouldBe(HttpStatusCode.OK);
        _alerts.Raised.ShouldBeEmpty();
    }

    private sealed class RecordingAlerts : IOpsAlerts
    {
        public List<(string Source, string Message)> Raised { get; } = [];

        public void Raise(string source, string message) => Raised.Add((source, message));
    }
}
