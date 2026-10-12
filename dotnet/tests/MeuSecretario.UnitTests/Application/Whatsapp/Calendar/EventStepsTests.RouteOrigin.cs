using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Settings;
using MeuSecretario.Application.Whatsapp;
using NSubstitute;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Calendar;

// Desvio 156: resposta casa/trabalho/outro à pergunta de origem da rota.
public partial class EventStepsTests
{
    private static async Task<(CalendarRig Rig, IRouteClient Routes)> ManyAddresses()
    {
        var rig = new CalendarRig("2026-10-08T14:20:00-03:00");
        await Seed(rig, JsonNode.Parse(EventJson("Av Paulista 1000"))!);
        rig.Addresses.ListAsync(7, Arg.Any<CancellationToken>()).Returns(
        [
            new AddressRow(1, "casa", "Rua A, 1", -19.9m, -43.9m),
            new AddressRow(3, "trabalho", "Rua B, 2", -19.8m, -43.8m),
        ]);
        var routes = Substitute.For<IRouteClient>();
        routes.GeocodeAsync("Av Paulista 1000", null, Arg.Any<CancellationToken>()).Returns(new GeocodeResult(-23.56, -46.65, "Avenida Paulista, 1000, São Paulo"));
        routes.CalculateTravelTimeAsync(new Coordinates(-19.8, -43.8), new Coordinates(-23.56, -46.65), null, Arg.Any<CancellationToken>())
            .Returns(new TravelResult(30, 8.5, null));
        routes.CalculateTravelTimeAsync(new Coordinates(-19.9, -43.9), new Coordinates(-23.56, -46.65), null, Arg.Any<CancellationToken>())
            .Returns(new TravelResult(35, 9.5, null));
        return (rig, routes);
    }

    [Theory]
    [InlineData("trabalho")]
    [InlineData("  Trabalho ")]
    [Trait("AC", "012-503")]
    public async Task Desvio156_OriginAnswerAfterTheQuestionCalculatesTheRouteWithThatOrigin(string answer)
    {
        var (rig, routes) = await ManyAddresses();
        var step = Route(rig, routes);
        (await step.HandleAsync(Ctx("calcular rota"), CancellationToken.None))!.ShouldStartWith("🚗 *Qual endereço usar como origem?*");

        var reply = await step.HandleAsync(Ctx(answer), CancellationToken.None);

        reply.ShouldBe(
            "✅ *Rota Calculada!*\n\n📍 Origem: 💼 Trabalho\n📍 Destino: Avenida Paulista, 1000, São Paulo\n\n" +
            "⏱️ *Tempo estimado:* 30 minutos\n📏 *Distância:* 8.5 km\n\n💡 Responda *'sim'* para confirmar o evento\nou *'não'* para cancelar");
        // A escolha é consumida: a mesma palavra de novo volta a seguir o pipeline.
        (await step.HandleAsync(Ctx("trabalho"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Desvio156_WithoutAPendingQuestion_CasaFollowsThePipeline()
    {
        var (rig, routes) = await ManyAddresses();

        (await Route(rig, routes).HandleAsync(Ctx("casa"), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    [Trait("AC", "012-503")]
    public async Task Desvio156_LabelWithoutAddress_AsksAgainAndKeepsTheChoice()
    {
        var (rig, routes) = await ManyAddresses();
        var step = Route(rig, routes);
        await step.HandleAsync(Ctx("calcular rota"), CancellationToken.None);

        var reply = await step.HandleAsync(Ctx("outro"), CancellationToken.None);

        reply.ShouldNotBeNull().ShouldStartWith("❌ Você não tem um endereço *outro* salvo.");
        (await step.HandleAsync(Ctx("casa"), CancellationToken.None))!.ShouldContain("📍 Origem: 🏠 Casa");
    }
}
