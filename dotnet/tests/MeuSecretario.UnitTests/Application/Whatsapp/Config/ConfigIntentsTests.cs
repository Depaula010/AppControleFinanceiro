using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Settings;
using MeuSecretario.UnitTests.Application.Whatsapp.Calendar;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Config;

/// <summary>Banco em memória das configurações do WhatsApp (mesmo comportamento que o <c>FakeDb</c> do gerador de fixtures).</summary>
internal sealed class MemoryConfigStore : IWhatsAppConfigStore
{
    public WhatsAppNotificationConfig? Notification { get; set; }

    public WhatsAppReportConfig? Report { get; set; }

    public (string City, string? State)? Location { get; set; }

    public (string? ApiKey, string? Name)? User { get; set; }

    public bool FailLocation { get; set; }

    public bool FailNotificationUpdate { get; set; }

    public List<string> Writes { get; } = [];

    public Task<WhatsAppNotificationConfig> GetOrCreateNotificationAsync(int userId, CancellationToken cancellationToken)
    {
        Notification ??= new WhatsAppNotificationConfig(true, new TimeOnly(7, 0), true, new TimeOnly(20, 0), true);
        return Task.FromResult(Notification);
    }

    public Task<WhatsAppNotificationConfig> UpdateMorningAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken)
    {
        if (FailNotificationUpdate)
        {
            throw new InvalidOperationException("banco fora com segredo");
        }

        Writes.Add($"morning {active} {time}");
        Notification = Notification! with { MorningActive = active ?? Notification.MorningActive, MorningTime = time ?? Notification.MorningTime };
        return Task.FromResult(Notification);
    }

    public Task<WhatsAppNotificationConfig> UpdateCheckinAsync(int userId, bool? active, TimeOnly? time, CancellationToken cancellationToken)
    {
        if (FailNotificationUpdate)
        {
            throw new InvalidOperationException("banco fora com segredo");
        }

        Writes.Add($"checkin {active} {time}");
        Notification = Notification! with { CheckinActive = active ?? Notification.CheckinActive, CheckinTime = time ?? Notification.CheckinTime };
        return Task.FromResult(Notification);
    }

    public Task<WhatsAppNotificationConfig> UpdateFinancialAlertsAsync(int userId, bool active, CancellationToken cancellationToken)
    {
        if (FailNotificationUpdate)
        {
            throw new InvalidOperationException("banco fora com segredo");
        }

        Writes.Add($"alerts {active}");
        Notification = Notification! with { FinancialAlertsActive = active };
        return Task.FromResult(Notification);
    }

    public Task<WhatsAppReportConfig> GetOrCreateReportAsync(int userId, CancellationToken cancellationToken)
    {
        Report ??= new WhatsAppReportConfig(true, "INICIO_MES", new TimeOnly(8, 0));
        return Task.FromResult(Report);
    }

    public Task<WhatsAppReportConfig> UpdateReportAsync(int userId, bool? active, string? moment, TimeOnly? time, CancellationToken cancellationToken)
    {
        Report = Report! with { Active = active ?? Report.Active, Moment = moment ?? Report.Moment, Time = time ?? Report.Time };
        return Task.FromResult(Report);
    }

    public Task UpdateLocationAsync(int userId, string city, string? state, CancellationToken cancellationToken)
    {
        if (FailLocation)
        {
            throw new InvalidOperationException("boom segredo");
        }

        Location = (city, state);
        return Task.CompletedTask;
    }

    public Task<(string? ApiKey, string? Name)?> GetApiKeyAsync(int userId, CancellationToken cancellationToken) => Task.FromResult(User);
}

internal sealed class MemoryAddresses : IFavoriteAddressRepository
{
    public List<(string Label, string Address, decimal? Lat, decimal? Lon)> Rows { get; } = [];

    public Task<IReadOnlyList<AddressRow>> ListAsync(int userId, CancellationToken cancellationToken)
    {
        // Ordem do banco (label): casa, outro, trabalho.
        IReadOnlyList<AddressRow> list =
        [
            .. Rows.OrderBy(r => r.Label, StringComparer.Ordinal).Select((r, i) => new AddressRow(i + 1, r.Label, r.Address, r.Lat, r.Lon)),
        ];
        return Task.FromResult(list);
    }

    public Task UpsertAsync(int userId, string label, string address, AddressCoordinates? coordinates, CancellationToken cancellationToken)
    {
        Rows.RemoveAll(r => r.Label == label);
        Rows.Add((label, address, coordinates?.Latitude, coordinates?.Longitude));
        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(int userId, string label, CancellationToken cancellationToken) => Task.FromResult(Rows.RemoveAll(r => r.Label == label) > 0);
}

internal sealed class CountingDiagnostics : IWhatsAppDiagnostics
{
    public int Failures { get; private set; }

    public void IntentFailed(string intent, string errorType) => Failures++;
}

public class ConfigIntentsTests
{
    private const string Ops = "❌ Ops! Algo deu errado. Por favor, tente novamente.";

    private static async Task<string> Run(CalendarRig rig, IWhatsAppIntent intent) =>
        await new IntentRouter([intent], new CountingDiagnostics()).DispatchAsync(intent.Name, rig.Context(), CancellationToken.None);

    private static CalendarRig NewRig() => new("2026-10-08T14:20:00-03:00");

    private static JsonElement Setup(JsonElement c, string name) => c.GetProperty("setup").TryGetProperty(name, out var value) ? value : default;

    private static TimeOnly Time(string text) => TimeOnly.Parse(text, CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- localização

    [Fact]
    [Trait("AC", "012-603")]
    public async Task Location_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("location_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = NewRig();
            var store = new MemoryConfigStore { FailLocation = Setup(c, "fail_location").ValueKind == JsonValueKind.True };
            rig.Llm.Then(c.GetProperty("gemini").GetRawText());

            var reply = await Run(rig, new LocationIntent(rig.Llm, store));

            var expected = c.GetProperty("message").GetString()!;
            if (expected.StartsWith("❌ Erro ao salvar localização: ", StringComparison.Ordinal))
            {
                expected = "❌ Erro ao salvar localização."; // desvio 008: sem o motivo
            }

            reply.ShouldBe(expected, name);
            reply.ShouldNotContain("segredo");

            var state = c.GetProperty("state").GetProperty("location");
            if (state.ValueKind == JsonValueKind.Null)
            {
                store.Location.ShouldBeNull(name);
            }
            else
            {
                store.Location!.Value.City.ShouldBe(state.GetProperty("cidade").GetString(), name);
                store.Location.Value.State.ShouldBe(state.GetProperty("estado").ValueKind == JsonValueKind.Null ? null : state.GetProperty("estado").GetString(), name);
            }
        }
    }

    // ---------------------------------------------------------------- relatório mensal

    [Fact]
    [Trait("AC", "012-603")]
    public async Task MonthlyReport_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("report_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = NewRig();
            var store = new MemoryConfigStore();
            var initial = Setup(c, "report");
            if (initial.ValueKind == JsonValueKind.Array)
            {
                store.Report = new WhatsAppReportConfig(initial[0].GetBoolean(), initial[1].GetString()!, Time(initial[2].GetString()!));
            }

            rig.Llm.Then(c.GetProperty("gemini").GetRawText());

            var reply = await Run(rig, new MonthlyReportIntent(rig.Llm, store));

            reply.ShouldBe(c.GetProperty("message").GetString(), name);
            var state = c.GetProperty("state").GetProperty("report");
            if (state.ValueKind == JsonValueKind.Null)
            {
                store.Report.ShouldBeNull(name);
            }
            else
            {
                store.Report.ShouldNotBeNull(name);
                store.Report.Active.ShouldBe(state.GetProperty("ativo").GetBoolean(), name);
                store.Report.Moment.ShouldBe(state.GetProperty("momento").GetString(), name);
                store.Report.Time.ShouldBe(Time(state.GetProperty("hora").GetString()!), name);
            }
        }
    }

    // ---------------------------------------------------------------- API key

    [Fact]
    [Trait("AC", "012-603")]
    public async Task ApiKey_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("apikey_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var user = Setup(c, "user");
            var store = new MemoryConfigStore
            {
                User = user.ValueKind == JsonValueKind.Null
                    ? null
                    : (user.GetProperty("api_key").GetString(), user.GetProperty("nome").ValueKind == JsonValueKind.Null ? null : user.GetProperty("nome").GetString()),
            };

            (await Run(NewRig(), new ApiKeyIntent(store, Substitute.For<IEncryptionService>()))).ShouldBe(c.GetProperty("message").GetString(), name);
        }
    }

    // ---------------------------------------------------------------- endereços

    private static MemoryAddresses Addresses(JsonElement c)
    {
        var repo = new MemoryAddresses();
        var initial = Setup(c, "addresses");
        if (initial.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in initial.EnumerateArray())
            {
                repo.Rows.Add((a.GetProperty("label").GetString()!, a.GetProperty("endereco").GetString()!, Decimal(a, "lat"), Decimal(a, "lon")));
            }
        }

        return repo;
    }

    private static decimal? Decimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static IRouteClient Geocoder(JsonElement c)
    {
        var routes = Substitute.For<IRouteClient>();
        var geocode = Setup(c, "geocode");
        routes.GeocodeAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(
            geocode.ValueKind == JsonValueKind.Array && geocode[0].ValueKind == JsonValueKind.Number
                ? new GeocodeResult(geocode[0].GetDouble(), geocode[1].GetDouble(), geocode[2].ValueKind == JsonValueKind.String ? geocode[2].GetString()! : string.Empty)
                : null);
        return routes;
    }

    private static void ShouldMatchAddresses(MemoryAddresses repo, JsonElement c, string name)
    {
        var expected = c.GetProperty("state").GetProperty("addresses").EnumerateArray().OrderBy(a => a.GetProperty("label").GetString(), StringComparer.Ordinal).ToList();
        repo.Rows.OrderBy(r => r.Label, StringComparer.Ordinal).Select(r => (r.Label, r.Address))
            .ShouldBe(expected.Select(a => (a.GetProperty("label").GetString()!, a.GetProperty("endereco").GetString()!)), name);
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task AddressConfig_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("address_config_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = NewRig();
            var repo = Addresses(c);
            rig.Llm.Then(c.GetProperty("gemini").GetRawText());

            var reply = await Run(rig, new AddressConfigIntent(rig.Llm, repo, Geocoder(c)));

            var expected = c.GetProperty("message").GetString()!;
            if (expected.StartsWith("❌ Erro ao salvar endereço: ", StringComparison.Ordinal))
            {
                expected = "❌ Erro ao salvar endereço."; // desvio 008
            }

            reply.ShouldBe(expected, name);
            ShouldMatchAddresses(repo, c, name);
        }
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task AddressConfig_SavesTheGeocodedCoordinates()
    {
        var rig = NewRig();
        var repo = new MemoryAddresses();
        var routes = Substitute.For<IRouteClient>();
        routes.GeocodeAsync("Av Paulista 1000", null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, "Avenida Paulista, 1000"));
        rig.Llm.Then("""{"label": "casa", "endereco_completo": "Av Paulista 1000"}""");

        await Run(rig, new AddressConfigIntent(rig.Llm, repo, routes));

        repo.Rows.Single().ShouldBe(("casa", "Avenida Paulista, 1000", -23.56m, -46.65m));
        rig.Llm.Requests.Single().UserId.ShouldBe(7);
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task AddressList_MatchesTheFlask_InCasaTrabalhoOutroOrder()
    {
        foreach (var c in CalendarRig.Cases("address_list_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = NewRig();
            var repo = Addresses(c);
            var intent = new AddressListIntent(new UserAddresses(repo));

            (await Run(rig, intent)).ShouldBe(c.GetProperty("message").GetString(), name);
        }
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task AddressList_WhenTheReadFails_AnswersAsIfThereWereNone()
    {
        var repo = Substitute.For<IFavoriteAddressRepository>();
        repo.ListAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<AddressRow>>(_ => throw new InvalidOperationException("fora"));

        (await Run(NewRig(), new AddressListIntent(new UserAddresses(repo)))).ShouldStartWith("📍 *Seus Endereços Favoritos*\n\nVocê ainda não tem endereços");
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task AddressDelete_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("address_delete_cases"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = NewRig();
            var repo = Addresses(c);
            rig.Llm.Then(c.GetProperty("gemini").GetRawText());

            var reply = await Run(rig, new AddressDeleteIntent(rig.Llm, repo));

            var expected = c.GetProperty("message").GetString()!;
            if (expected.StartsWith("❌ Erro ao deletar endereço: ", StringComparison.Ordinal))
            {
                expected = "❌ Erro ao deletar endereço."; // desvio 008
            }

            reply.ShouldBe(expected, name);
            ShouldMatchAddresses(repo, c, name);
        }
    }

    [Fact]
    [Trait("AC", "012-603")]
    public async Task Intents_WhenGeminiBreaks_AnswerTheGenericText()
    {
        var rig = NewRig();
        rig.Llm.ThenThrow(new LlmQuotaExceededException("429 Quota exceeded"));

        (await Run(rig, new LocationIntent(rig.Llm, new MemoryConfigStore()))).ShouldBe(Ops);
    }
}
