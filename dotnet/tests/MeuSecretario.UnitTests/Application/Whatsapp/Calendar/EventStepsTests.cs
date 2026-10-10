using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.UnitTests.Domain.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

public partial class EventStepsTests
{
    private const string Key = "pending_event:5531999990001:ab12cd34";

    private static PipelineContext Ctx(string text) =>
        new(new WhatsAppUser(7, "Ana"), CalendarRig.Number, text, text.Trim().ToLowerInvariant(), null);

    private static async Task Seed(CalendarRig rig, JsonNode data) => await rig.Cache.SetJsonAsync(Key, data, TimeSpan.FromSeconds(300));

    private static EventReplyStep Step(CalendarRig rig) => new(rig.Events, rig.Client);

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Confirm_MatchesTheFlask_ForEveryScenario()
    {
        foreach (var c in CalendarRig.Cases("confirm_event_cases").Where(c => c.GetProperty("name").GetString() != "expirado"))
        {
            var name = c.GetProperty("name").GetString()!;
            var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
            await Seed(rig, JsonNode.Parse(c.GetProperty("pending").GetProperty(Key).GetRawText())!);
            // o estado inicial do cenário é o mesmo evento; falhas vêm do nome do cenário
            if (name == "nao_conectado")
            {
                rig.Client.NotConnected = true;
            }

            if (name == "falha_da_api")
            {
                rig.Client.FailInsert = true;
            }

            var expectedPython = c.GetProperty("message").GetString()!;
            var reply = await Step(rig).HandleAsync(Ctx("sim"), CancellationToken.None);

            if (c.GetProperty("ok").GetBoolean())
            {
                reply.ShouldBe($"✅ *Evento Criado!*\n\n📝 Reunião\n{expectedPython}", name);
                (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty(name);
            }
            else
            {
                var expected = expectedPython.StartsWith("Erro ao criar evento: Usuário não conectou", StringComparison.Ordinal)
                    ? "❌ " + expectedPython
                    : "❌ Erro ao criar evento.";
                reply.ShouldBe(expected, name);
                (await rig.Cache.KeysByPatternAsync("*")).ShouldBe([Key], name);
            }
        }
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Confirm_PassesTheEventToTheCalendar()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse("""{"usuario_id":7,"titulo":"Reunião","data_evento":"2026-10-09","hora_inicio":"7:5","hora_fim":"9:0","descricao":"Pauta","localizacao":""}""")!);

        await Step(rig).HandleAsync(Ctx("OK"), CancellationToken.None);

        var sent = rig.Client.Inserted.Single();
        sent.Summary.ShouldBe("Reunião");
        sent.Date.ShouldBe(new DateOnly(2026, 10, 9));
        sent.StartTime.ShouldBe(new TimeOnly(7, 5));
        sent.EndTime.ShouldBe(new TimeOnly(9, 0));
        sent.Description.ShouldBe("Pauta");
        sent.Location.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Confirm_AllDayEvent_HasNoTimes()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse("""{"usuario_id":7,"titulo":"Aniversário","data_evento":"2026-12-15","hora_inicio":null,"hora_fim":"10:00","descricao":null,"localizacao":null}""")!);

        await Step(rig).HandleAsync(Ctx("confirmar"), CancellationToken.None);

        var sent = rig.Client.Inserted.Single();
        sent.StartTime.ShouldBeNull();
        sent.EndTime.ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task Confirm_WhenThePendingEventVanishedBetweenReads_PrefixesTheEmojiTwice()
    {
        var events = Substitute.For<IPendingEventStore>();
        events.GetLatestAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new LatestPendingEvent("ab12cd34", new PendingEvent(7, "Reunião", "2026-10-09", null, null, null, null)));
        events.GetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((PendingEvent?)null);

        var reply = await new EventReplyStep(events, Substitute.For<ICalendarClient>()).HandleAsync(Ctx("s"), CancellationToken.None);

        reply.ShouldBe("❌ ❌ Nenhum evento pendente encontrado ou já expirou (5 minutos).");
    }

    [Theory]
    [InlineData("não")]
    [InlineData("cancelar")]
    [InlineData("n")]
    [InlineData("desistir")]
    [Trait("AC", "012-502")]
    public async Task Cancel_DiscardsThePendingEvent(string word)
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse("""{"usuario_id":7,"titulo":"Reunião","data_evento":"2026-10-09","hora_inicio":null,"hora_fim":null,"descricao":null,"localizacao":null}""")!);

        var reply = await Step(rig).HandleAsync(Ctx(word), CancellationToken.None);

        reply.ShouldBe("✅ Evento cancelado com sucesso!");
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
        rig.Client.Inserted.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("sim")]
    [InlineData("não")]
    [InlineData("gastei 50")]
    [InlineData("sim, calcular rota")]
    [Trait("AC", "012-502")]
    public async Task WithoutAPendingEvent_OrForOtherText_ThePipelineContinues(string text)
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");

        (await Step(rig).HandleAsync(Ctx(text), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-502")]
    public async Task RouteWords_AreLeftToTheRouteStep_EvenWithAPendingEvent()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse("""{"usuario_id":7,"titulo":"x","data_evento":"2026-10-09","hora_inicio":null,"hora_fim":null,"descricao":null,"localizacao":null}""")!);

        (await Step(rig).HandleAsync(Ctx("sim calcular rota"), CancellationToken.None)).ShouldBeNull();
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBe([Key]);
    }

    // ---------------------------------------------------------------- calcular rota

    private static RouteRequestStep Route(CalendarRig rig, IRouteClient routes) => new(rig.Events, rig.UserAddresses, routes, new MeuSecretario.Infrastructure.Whatsapp.RouteOriginChoiceStore(rig.Cache));

    private static string EventJson(string? location) =>
        $$"""{"usuario_id":7,"titulo":"Reunião","data_evento":"2026-10-09","hora_inicio":"14:00","hora_fim":null,"descricao":null,"localizacao":{{(location is null ? "null" : "\"" + location + "\"")}}}""";

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WithoutAPendingEvent()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");

        (await Route(rig, Substitute.For<IRouteClient>()).HandleAsync(Ctx("calcular rota"), CancellationToken.None))
            .ShouldBe("❌ Não encontrei nenhum evento pendente de confirmação.");
        (await Route(rig, Substitute.For<IRouteClient>()).HandleAsync(Ctx("outra coisa"), CancellationToken.None)).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [Trait("AC", "012-503")]
    public async Task Route_WithoutLocation(string? location)
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse(EventJson(location))!);

        (await Route(rig, Substitute.For<IRouteClient>()).HandleAsync(Ctx("calcular rota"), CancellationToken.None))
            .ShouldBe("❌ Este evento não tem localização definida, não posso calcular tempo de deslocamento.");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WithoutAddresses()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse(EventJson("Av Paulista 1000"))!);

        (await Route(rig, Substitute.For<IRouteClient>()).HandleAsync(Ctx("calcular rota"), CancellationToken.None)).ShouldBe(
            "❌ Você não tem endereços cadastrados.\n\nConfigure um endereço primeiro:\nExemplo: _'Configurar endereço casa: Av Paulista 1000, São Paulo-SP'_");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WithManyAddresses_AsksTheOrigin_InCasaTrabalhoOutroOrder()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse(EventJson("Av Paulista 1000"))!);
        rig.Addresses.ListAsync(7, Arg.Any<CancellationToken>()).Returns(
        [
            new AddressRow(1, "casa", "Rua A, 1", -19.9m, -43.9m),
            new AddressRow(2, "outro", "Rua C, 3", null, null),
            new AddressRow(3, "trabalho", "Rua B, 2", -19.8m, -43.8m),
        ]);

        var reply = await Route(rig, Substitute.For<IRouteClient>()).HandleAsync(Ctx("Calcular a rota"), CancellationToken.None);

        reply.ShouldBe(
            "🚗 *Qual endereço usar como origem?*\n\n🏠 *Casa*: Rua A, 1\n💼 *Trabalho*: Rua B, 2\n➕ *Outro*: Rua C, 3\n\n" +
            "💬 Responda com: _'casa'_, _'trabalho'_ ou _'outro'_");
    }

    private static async Task<(CalendarRig Rig, IRouteClient Routes)> OneAddress()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00", addressCount: 1);
        await Seed(rig, JsonNode.Parse(EventJson("Av Paulista 1000"))!);
        return (rig, Substitute.For<IRouteClient>());
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WithOneAddress_CalculatesTheTrip_WithTheSystemKey()
    {
        var (rig, routes) = await OneAddress();
        routes.GeocodeAsync("Av Paulista 1000", null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, "Avenida Paulista, 1000, São Paulo"));
        routes.CalculateTravelTimeAsync(new Coordinates(-19.9, -43.9), new Coordinates(-23.56, -46.65), null, Arg.Any<CancellationToken>())
            .Returns(new TravelResult(45, 12.5, null));

        var reply = await Route(rig, routes).HandleAsync(Ctx("calcular rota"), CancellationToken.None);

        reply.ShouldBe(
            "✅ *Rota Calculada!*\n\n📍 Origem: 🏠 Casa\n📍 Destino: Avenida Paulista, 1000, São Paulo\n\n" +
            "⏱️ *Tempo estimado:* 45 minutos\n📏 *Distância:* 12.5 km\n\n💡 Responda *'sim'* para confirmar o evento\nou *'não'* para cancelar");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_DistanceWithoutDecimals_KeepsThePythonFloatText_AndEmptyLabelFallsBackToTheEventLocation()
    {
        var (rig, routes) = await OneAddress();
        routes.GeocodeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, ""));
        routes.CalculateTravelTimeAsync(Arg.Any<Coordinates>(), Arg.Any<Coordinates>(), null, Arg.Any<CancellationToken>()).Returns(new TravelResult(5, 10, null));

        var reply = (await Route(rig, routes).HandleAsync(Ctx("calcular rota"), CancellationToken.None))!;

        reply.ShouldContain("📍 Destino: Av Paulista 1000\n");
        reply.ShouldContain("📏 *Distância:* 10.0 km");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WhenTheDestinationIsNotFound()
    {
        var (rig, routes) = await OneAddress();
        routes.GeocodeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns((GeocodeResult?)null);

        (await Route(rig, routes).HandleAsync(Ctx("calcular rota"), CancellationToken.None)).ShouldBe(
            "❌ Não consegui localizar o destino:\n'Av Paulista 1000'\n\nVerifique se o endereço está completo e tente novamente.");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WhenTheCalculationFails()
    {
        var (rig, routes) = await OneAddress();
        routes.GeocodeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, "x"));
        routes.CalculateTravelTimeAsync(Arg.Any<Coordinates>(), Arg.Any<Coordinates>(), null, Arg.Any<CancellationToken>()).Returns((TravelResult?)null);

        (await Route(rig, routes).HandleAsync(Ctx("calcular rota"), CancellationToken.None)).ShouldBe(
            "❌ Não consegui calcular a rota.\n\nTente novamente mais tarde ou confirme o evento sem cálculo de tempo.");
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Route_WhenTheAddressHasNoCoordinates_AnswersThatItCouldNotCalculate()
    {
        var (rig, routes) = await OneAddress();
        rig.Addresses.ListAsync(7, Arg.Any<CancellationToken>()).Returns([new AddressRow(1, "casa", "Rua A", null, null)]);
        routes.GeocodeAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, "x"));

        (await Route(rig, routes).HandleAsync(Ctx("calcular rota"), CancellationToken.None))!.ShouldStartWith("❌ Não consegui calcular a rota.");
        await routes.DidNotReceive().CalculateTravelTimeAsync(Arg.Any<Coordinates>(), Arg.Any<Coordinates>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }
}
