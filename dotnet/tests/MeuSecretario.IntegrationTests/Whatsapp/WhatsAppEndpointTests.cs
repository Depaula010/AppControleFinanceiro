using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Whatsapp;
using MeuSecretario.IntegrationTests.Support;
using MeuSecretario.TestFixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MeuSecretario.IntegrationTests.Whatsapp;

/// <summary>Cenários do harness do Flask (tests/Fixtures/whatsapp_handler_harness.py) reproduzidos contra a rota real.</summary>
internal static class HarnessScenarios
{
    private static readonly JsonElement Root = Load();

    public static string HarnessApiKey => Root.GetProperty("api_key").GetString()!;

    public static IEnumerable<JsonElement> All(params string[] groups) =>
        Root.GetProperty("scenarios").EnumerateArray().Where(s => groups.Contains(s.GetProperty("group").GetString()));

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-pipeline-scenarios.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

/// <summary><c>POST /webhook-whatsapp</c> (spec 012 §3 e §4): banco real, Gemini, bot e Redis falsos; corpo como bytes exatos.</summary>
[Collection(PostgresTestGroup.Name)]
[Trait("Category", "Docker")]
public class WhatsAppEndpointParityTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Sub-entregas já implementadas; o harness traz cenários das demais.</summary>
    private static readonly string[] ImplementedGroups = ["012-0"];

    private WhatsAppEnv _env = default!;

    public async Task InitializeAsync()
    {
        await postgres.ResetAsync();
        _env = new WhatsAppEnv(postgres);
        await _env.SeedUserAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ResetAsync(JsonElement config, string number)
    {
        _env.Llm.Reset();
        _env.Cache.Clear();
        _env.Bot.Sent.Clear();
        _env.Checkin.Calls.Clear();
        _env.Health.Connected = !config.TryGetProperty("db_ok", out var db) || db.GetBoolean();

        var known = !config.TryGetProperty("user", out var user) || user.GetBoolean();
        await using var connection = await postgres.DataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE usuarios SET numero_whatsapp = @number", new { number = known ? number : "5500000000000" });
    }

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryHarnessScenario_MatchesTheFlask()
    {
        var scenarios = HarnessScenarios.All(ImplementedGroups).ToList();
        scenarios.Count.ShouldBeGreaterThan(25);

        foreach (var scenario in scenarios)
        {
            var name = scenario.GetProperty("name").GetString()!;
            var config = scenario.GetProperty("config");
            var calls = scenario.GetProperty("result").GetProperty("calls").EnumerateArray().Select(c => c.GetString()!).ToList();
            var lookup = calls.FirstOrDefault(c => c.StartsWith("user_service.check_user_exists:", StringComparison.Ordinal));
            await ResetAsync(config, lookup is null ? WhatsAppEnv.Number : lookup["user_service.check_user_exists:".Length..]);

            if (config.TryGetProperty("redis", out var redis))
            {
                foreach (var entry in redis.EnumerateObject())
                {
                    await _env.Cache.SetJsonAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()), TimeSpan.FromSeconds(300));
                }
            }

            if (config.TryGetProperty("intent", out var intent))
            {
                if (intent.ValueKind == JsonValueKind.Object)
                {
                    _env.Llm.ThenThrow(new InvalidOperationException(intent.GetProperty("__raise__").GetString()));
                }
                else
                {
                    _env.Llm.Then(intent.ValueKind == JsonValueKind.Null ? """{"intent": null}""" : JsonSerializer.Serialize(new { intent = intent.GetString() }));
                }
            }

            if (config.TryGetProperty("checkin_response", out var checkin))
            {
                _env.Checkin.Reply = checkin[1].GetString()!;
            }

            var (status, body) = await SendAsync(scenario, config);

            var result = scenario.GetProperty("result");
            var deviation = scenario.GetProperty("deviation");
            var expectedStatus = deviation.ValueKind == JsonValueKind.String && deviation.GetString() == "024" ? 400 : result.GetProperty("status").GetInt32();
            var expectedBody = deviation.ValueKind == JsonValueKind.String && deviation.GetString() == "024"
                ? JsonNode.Parse("""{"status": "erro", "mensagem": "Dados faltando"}""")
                : JsonNode.Parse(result.GetProperty("body").GetRawText());

            ((int)status).ShouldBe(expectedStatus, name);
            JsonNode.DeepEquals(JsonNode.Parse(body), expectedBody).ShouldBeTrue($"{name}: {body}");

            var expectedKeys = result.GetProperty("redis").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
            _env.Cache.Values.Keys.Order(StringComparer.Ordinal).ToList().ShouldBe(expectedKeys, name);
            foreach (var entry in result.GetProperty("redis").EnumerateObject())
            {
                JsonNode.DeepEquals(JsonNode.Parse(_env.Cache.Values[entry.Name]), JsonNode.Parse(entry.Value.GetRawText())).ShouldBeTrue($"{name}: {entry.Name}");
            }

            _env.Checkin.Calls.Count.ShouldBe(result.GetProperty("calls").EnumerateArray().Count(c => c.GetString() == "NightlyCheckinService.process_response"), name);
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(JsonElement scenario, JsonElement config)
    {
        var headers = new Dictionary<string, string>();
        var jsonError = config.TryGetProperty("json_error", out _);
        var bodyText = jsonError ? "texto=oi" : scenario.GetProperty("body").GetRawText();
        var bytes = Encoding.UTF8.GetBytes(bodyText);

        if (!config.TryGetProperty("send_api_key", out var send) || send.GetBoolean())
        {
            var key = config.TryGetProperty("api_key", out var apiKey) ? apiKey.GetString()! : HarnessScenarios.HarnessApiKey;
            headers["x-api-key"] = key.Replace(HarnessScenarios.HarnessApiKey, WhatsAppEnv.ApiKey, StringComparison.Ordinal);
        }

        if (config.TryGetProperty("signature", out var signature))
        {
            headers["X-Webhook-Signature"] = signature.GetString() == "valid"
                ? Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WhatsAppEnv.ApiKey), bytes))
                : signature.GetString()!;
        }

        return await _env.PostAsync(bytes, headers, jsonError ? "text/plain" : "application/json");
    }

    [Fact]
    [Trait("AC", "012-003")]
    public async Task Signature_IsComputedOverTheRawBytes_SoWhitespaceAndAccentsCount()
    {
        const string raw = "{ \"texto\" :  \"ajuda é você\",\n\"numero_remetente\": \"5531999990001@s.whatsapp.net\" }";
        _env.Llm.Then("""{"intent": "Menu de Ajuda"}""");
        var bytes = Encoding.UTF8.GetBytes(raw);

        var valid = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WhatsAppEnv.ApiKey), bytes));
        var reformatted = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WhatsAppEnv.ApiKey), Encoding.UTF8.GetBytes(raw.Replace(" ", string.Empty, StringComparison.Ordinal))));

        var (okStatus, _) = await _env.PostAsync(bytes, new Dictionary<string, string> { ["x-api-key"] = WhatsAppEnv.ApiKey, ["X-Webhook-Signature"] = valid });
        var (badStatus, badBody) = await _env.PostAsync(bytes, new Dictionary<string, string> { ["x-api-key"] = WhatsAppEnv.ApiKey, ["X-Webhook-Signature"] = reformatted });

        okStatus.ShouldBe(HttpStatusCode.OK);
        badStatus.ShouldBe(HttpStatusCode.Unauthorized);
        badBody.ShouldBe("""{"status":"erro","resposta":"Assinatura inválida"}""");
    }

    [Fact]
    [Trait("AC", "012-005")]
    public async Task BodyThatIsNotAnObject_Answers400WithTheEnvelopeMessage()
    {
        foreach (var raw in new[] { "[1,2]", "\"texto\"", "5", "", "null", "{" })
        {
            var (status, body) = await _env.PostAsync(Encoding.UTF8.GetBytes(raw), new Dictionary<string, string> { ["x-api-key"] = WhatsAppEnv.ApiKey });

            status.ShouldBe(HttpStatusCode.BadRequest, raw);
            body.ShouldBe("""{"status":"erro","mensagem":"Dados faltando"}""", raw);
        }
    }

    [Fact]
    [Trait("AC", "012-002")]
    public async Task WithoutTheGeminiKey_Answers503BeforeAnyAuthentication()
    {
        var factory = postgres.Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIntegrationSettings>();
            services.AddSingleton<IIntegrationSettings>(new NoGeminiSettings());
        }));
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhook-whatsapp", UriKind.Relative))
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).ShouldBe("""{"status":"erro","mensagem":"Serviço não configurado"}""");
    }

    [Fact]
    [Trait("AC", "012-001")]
    public async Task OtherMethodsAndTrailingSlash_AreNotTheRoute()
    {
        using var client = _env.Factory.CreateClient();

        (await client.GetAsync(new Uri("/webhook-whatsapp", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await client.PostAsync(new Uri("/webhook-whatsapp/", UriKind.Relative), new StringContent("{}"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    [Trait("AC", "012-008")]
    public async Task Logs_NeverCarryTheMessageTheNumberTheKeyOrTheErrorText()
    {
        _env.Llm.ThenThrow(new InvalidOperationException("429 segredo-do-modelo chave=" + WhatsAppEnv.ApiKey));

        var (status, body) = await _env.PostJsonAsync(new { texto = "gastei 77 na padaria do Zé", numero_remetente = WhatsAppEnv.Number + "@s.whatsapp.net" });

        status.ShouldBe((HttpStatusCode)429);
        body.ShouldNotContain("segredo-do-modelo");
        var logs = _env.Logs.All;
        logs.ShouldNotContain("padaria");
        logs.ShouldNotContain(WhatsAppEnv.Number);
        logs.ShouldNotContain(WhatsAppEnv.ApiKey);
        logs.ShouldNotContain("segredo-do-modelo");
        logs.ShouldContain("InvalidOperationException");
    }

    [Fact]
    [Trait("AC", "012-009")]
    public async Task AnUnknownIntentNeverReachesThePipelineAsAnError()
    {
        _env.Llm.Then("""{"intent": "Algo Novo"}""");

        var (status, body) = await _env.PostJsonAsync(new { texto = "oi", numero_remetente = WhatsAppEnv.Number });

        status.ShouldBe(HttpStatusCode.OK);
        JsonDocument.Parse(body).RootElement.GetProperty("resposta").GetString().ShouldBe(WhatsAppTexts.NotUnderstood);
        _env.Llm.Requests.Single().UserId.ShouldNotBeNull();
    }
}

/// <summary>Configuração de integrações sem a chave do Gemini (o <c>gemini_model</c> do Flask não existiria).</summary>
internal sealed class NoGeminiSettings : IIntegrationSettings
{
    public string GeminiApiKey => string.Empty;

    public string GeminiModel => "gemini-2.5-flash-lite";

    public string ApiSecretKey => AuthFixtures.JwtSecret;

    public string BotWhatsappUrl => string.Empty;

    public string BotApiKey => string.Empty;

    public string BotSessionName => string.Empty;

    public string BotSessionId => string.Empty;

    public string GoogleClientId => string.Empty;

    public string GoogleClientSecret => string.Empty;

    public string GoogleRedirectUri => string.Empty;

    public DateTimeOffset? GoogleConnectLegacyUntil => null;

    public string WeatherApiKey => string.Empty;

    public string OpenRouteApiKey => string.Empty;

    public string OpenRouteBaseUrl => string.Empty;

    public int TravelTimeDailyLimit => 10;
}
