using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.UnitTests.Application.Whatsapp.Config;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

/// <summary>
/// Paridade do pipeline com o <c>WhatsAppHandler</c> real do Flask nos cenários das sub-entregas 012e (confirmar/cancelar evento e
/// "calcular rota") e 012f (<c>Configurar Notificações</c>): <c>tests/Fixtures/whatsapp_scenarios_efh.py</c>.
/// </summary>
public class CalendarConfigParityTests
{
    private static readonly JsonElement Root = Load();

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-pipeline-scenarios-efh.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static bool Has(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

    private sealed class Pipeline
    {
        public Pipeline(JsonElement scenario)
        {
            var config = scenario.GetProperty("config");
            Rig = new CalendarRig("2026-10-08T14:20:00-03:00");
            Calls = [];

            // Google Calendar: o roteiro de create_event decide se a criação funciona, falha ou o usuário não tem Google.
            if (Has(config, "create_event") && !config.GetProperty("create_event")[0].GetBoolean())
            {
                var message = config.GetProperty("create_event")[1].GetString()!;
                if (message.Contains("Usuário não conectou", StringComparison.Ordinal))
                {
                    Rig.Client.NotConnected = true;
                }
                else
                {
                    Rig.Client.FailInsert = true;
                }
            }

            Addresses = new MemoryAddresses();
            foreach (var a in Has(config, "addresses") ? config.GetProperty("addresses").EnumerateArray() : [])
            {
                Addresses.Rows.Add((a.GetProperty("label").GetString()!, a.GetProperty("endereco").GetString()!, Decimal(a, "lat"), Decimal(a, "lon")));
            }

            Routes = Substitute.For<IRouteClient>();
            if (Has(config, "geocode") && config.GetProperty("geocode")[0].ValueKind == JsonValueKind.Number)
            {
                var g = config.GetProperty("geocode");
                Routes.GeocodeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(
                    new GeocodeResult(g[0].GetDouble(), g[1].GetDouble(), g[2].ValueKind == JsonValueKind.String ? g[2].GetString()! : string.Empty));
            }

            if (Has(config, "travel"))
            {
                var t = config.GetProperty("travel");
                Routes.CalculateTravelTimeAsync(Arg.Any<Coordinates>(), Arg.Any<Coordinates>(), null, Arg.Any<CancellationToken>())
                    .Returns(new TravelResult(t.GetProperty("duration_minutes").GetInt32(), t.GetProperty("distance_km").GetDouble(), null));
            }

            Store = new MemoryConfigStore { FailNotificationUpdate = scenario.GetProperty("fail_update").GetBoolean() };
            var initial = scenario.GetProperty("notif_initial");
            if (initial.ValueKind == JsonValueKind.String && initial.GetString() == "default")
            {
                Store.Notification = Defaults();
            }
            else if (initial.ValueKind == JsonValueKind.Object)
            {
                Store.Notification = Defaults() with
                {
                    MorningActive = initial.TryGetProperty("resumo_matinal_ativo", out var ma) ? ma.GetBoolean() : true,
                    MorningTime = initial.TryGetProperty("resumo_matinal_hora", out var mh) ? Time(mh.GetString()!) : new TimeOnly(7, 0),
                    CheckinActive = initial.TryGetProperty("checkin_noturno_ativo", out var ca) ? ca.GetBoolean() : true,
                    CheckinTime = initial.TryGetProperty("checkin_noturno_hora", out var chh) ? Time(chh.GetString()!) : new TimeOnly(20, 0),
                };
            }

            if (Has(config, "intent"))
            {
                Rig.Llm.Then(JsonSerializer.Serialize(new { intent = config.GetProperty("intent").GetString() }));
            }

            if (Has(config, "returns") && config.GetProperty("returns").TryGetProperty("gemini_service.extract_notification_config", out var notification))
            {
                Rig.Llm.Then(notification.GetRawText());
            }

            var transactions = new PendingTransactionStateStub();
            var steps = new List<IWhatsAppStep>
            {
                new EventReplyStep(Rig.Events, Rig.Client),
                new RouteRequestStep(Rig.Events, new UserAddresses(Addresses), Routes, new MeuSecretario.Infrastructure.Whatsapp.RouteOriginChoiceStore(Rig.Cache)),
                new BareCancelStep(transactions, Rig.Events),
            };
            var intents = new List<IWhatsAppIntent> { new HelpIntent(), new NotificationsIntent(Rig.Llm, Store) };
            UseCase = new HandleWhatsAppMessage(
                new FakeUsers(true, Calls), steps, new IntentClassifier(Rig.Llm), new IntentRouter(intents, new NullDiagnostics()));
        }

        public CalendarRig Rig { get; }

        public List<string> Calls { get; }

        public MemoryAddresses Addresses { get; }

        public IRouteClient Routes { get; }

        public MemoryConfigStore Store { get; }

        public HandleWhatsAppMessage UseCase { get; }

        private static WhatsAppNotificationConfig Defaults() => new(true, new TimeOnly(7, 0), true, new TimeOnly(20, 0), true);
    }

    private static TimeOnly Time(string text) => TimeOnly.Parse(text, CultureInfo.InvariantCulture);

    private static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private sealed class PendingTransactionStateStub : IPendingTransactionState
    {
        public Task<string?> GetLastPendingIdAsync(string number, CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task<JsonObject?> GetAsync(string number, string id, CancellationToken cancellationToken) => Task.FromResult<JsonObject?>(null);

        public Task SaveAsync(string number, string id, JsonObject data, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteBothAsync(string number, string id, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteLastPendingAsync(string number, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Desvio 008: os motivos técnicos saem das mensagens de falha.</summary>
    private static string WithoutReasons(string expected) =>
        expected.StartsWith("❌ Erro ao criar evento: ", StringComparison.Ordinal) && !expected.Contains("Usuário não conectou", StringComparison.Ordinal)
            ? "❌ Erro ao criar evento."
            : expected.StartsWith("❌ Erro ao atualizar configuração: ", StringComparison.Ordinal)
                ? "❌ Erro ao atualizar configuração."
                : expected;

    /// <summary>
    /// Cenários que embutem o comportamento antigo do Flask e ficam no JSON como evidência; o comportamento novo (desvios 152, 190 e 191,
    /// feature 012, 2026-10-10) é provado em <c>NotificationDeviationTests</c>.
    /// </summary>
    private static readonly HashSet<string> SupersededByDeviations =
    [
        "resumo_desativar_vira_ativar", "resumo_desligar_vira_ativar", "checkin_desligar_vira_ativar", "checkin_off_com_ativar_ganha_ativar",
        "gemini_agenda_ativar_grava_e_quebra", "gemini_agenda_desativar_grava_e_quebra", "gemini_agenda_configurar_grava_e_quebra",
        "gemini_contas_ativar_quebra_sem_gravar", "gemini_contas_configurar_quebra_sem_gravar",
    ];

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryScenario_MatchesTheFlask()
    {
        var scenarios = Root.GetProperty("scenarios").EnumerateArray().ToList();
        scenarios.Count.ShouldBeGreaterThan(60);

        foreach (var scenario in scenarios)
        {
            var name = scenario.GetProperty("name").GetString()!;
            if (SupersededByDeviations.Contains(name))
            {
                continue;
            }

            var result = scenario.GetProperty("result");
            var pipeline = new Pipeline(scenario);

            foreach (var entry in Has(scenario.GetProperty("config"), "redis") ? scenario.GetProperty("config").GetProperty("redis").EnumerateObject() : [])
            {
                await pipeline.Rig.Cache.SetJsonAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()), TimeSpan.FromSeconds(300));
            }

            var texto = scenario.GetProperty("texto").GetString()!;
            var number = scenario.GetProperty("numero").GetString()!;

            if (result.GetProperty("status").GetInt32() == 500)
            {
                await Should.ThrowAsync<Exception>(
                    () => pipeline.UseCase.ExecuteAsync(new WebhookInput(texto, number, null), CancellationToken.None), name);
            }
            else
            {
                var reply = await pipeline.UseCase.ExecuteAsync(new WebhookInput(texto, number, null), CancellationToken.None);
                reply.ShouldBe(WithoutReasons(result.GetProperty("body").GetProperty("resposta").GetString()!), name);
            }

            var cache = pipeline.Rig.Cache;
            // Desvio 156: a chave route_origin_pending (escolha da origem da rota) só existe no .NET e é conferida em EventStepsTests.
            (await cache.KeysByPatternAsync("*")).Where(k => !k.StartsWith("route_origin_pending:", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal).ShouldBe(
                    result.GetProperty("redis").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal), name);
            foreach (var entry in result.GetProperty("redis").EnumerateObject())
            {
                JsonNode.DeepEquals(JsonNode.Parse(cache.RawValue(entry.Name)!), JsonNode.Parse(entry.Value.GetRawText())).ShouldBeTrue($"{name}: {entry.Name}");
            }

            var expectedNotif = result.GetProperty("notif");
            if (expectedNotif.ValueKind == JsonValueKind.Object)
            {
                var actual = pipeline.Store.Notification;
                actual.ShouldNotBeNull(name);
                actual.MorningActive.ShouldBe(expectedNotif.GetProperty("resumo_matinal_ativo").GetBoolean(), name);
                actual.MorningTime.ShouldBe(Time(expectedNotif.GetProperty("resumo_matinal_hora").GetString()!), name);
                actual.CheckinActive.ShouldBe(expectedNotif.GetProperty("checkin_noturno_ativo").GetBoolean(), name);
                actual.CheckinTime.ShouldBe(Time(expectedNotif.GetProperty("checkin_noturno_hora").GetString()!), name);
            }
            else
            {
                pipeline.Store.Notification.ShouldBeNull(name);
            }
        }
    }
}
