using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Whatsapp;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>
/// Comportamento NOVO dos desvios de paridade aprovados para a 012 (grupo financeiro). Os cenários do harness que gravam o
/// comportamento antigo do Flask continuam em <see cref="FinancePipelineParityTests.KnownDivergences"/>.
/// </summary>
public partial class WhatsAppDeviationTests
{
    private const string Number = "5531999990001@s.whatsapp.net";

    private static async Task<FinanceRig> RigAsync(string scenarioName)
    {
        var scenario = FinanceScenarios.ByName(scenarioName);
        var config = scenario.GetProperty("config");
        var rig = new FinanceRig(config);
        if (config.TryGetProperty("redis", out var redis))
        {
            foreach (var entry in redis.EnumerateObject())
            {
                await rig.SeedAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()));
            }
        }

        return rig;
    }

    [Fact]
    public void KnownDivergences_StayInTheHarnessAsEvidenceOfTheOldBehaviour()
    {
        foreach (var (name, deviation) in FinancePipelineParityTests.KnownDivergences)
        {
            FinanceScenarios.ByName(name).GetProperty("deviation").GetString().ShouldBe(deviation, name);
        }
    }

    [Theory]
    [Trait("AC", "012-113")]
    [InlineData("Padaria do Zé", "padaria", "Padaria do Zé")]
    [InlineData(null, "padaria", "padaria")]
    [InlineData("", "padaria", "padaria")]
    [InlineData(null, null, null)]
    [InlineData("", "", null)]
    public void Desvio150_FinalDescriptionIsUsedOnlyWhenFilled(string? final, string? raw, string? expected) =>
        ConfirmationDescription.Pick(final, raw).ShouldBe(expected);

    [Theory]
    [Trait("AC", "012-113")]
    [InlineData("resposta_ok_sem_descricao_final_falha", "padaria")]
    [InlineData("resposta_ok_sem_chave_descricao_final", "padaria")]
    [InlineData("resposta_ok_grava_despesa", "Padaria do Zé")]
    [InlineData("resposta_ok_parcelada_sem_descricao_final", "tenis")]
    [InlineData("resposta_ok_parcelada", "Tênis")]
    public async Task Desvio150_OkSavesWithTheRawDescriptionWhenThereIsNoSchedule(string scenarioName, string expectedDescription)
    {
        var rig = await RigAsync(scenarioName);

        var reply = await rig.SendAsync("OK", Number);

        reply.ShouldBe("FEEDBACK");
        rig.World.PostedDescriptions.ShouldBe([expectedDescription]);
        (await rig.Cache.KeysByPatternAsync("*")).ShouldBeEmpty();
    }
}
