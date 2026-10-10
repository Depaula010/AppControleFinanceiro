using System.Text.RegularExpressions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>
/// Desvio 158: prompts de análise e previsão com valores absolutos e <c>formatar_moeda</c>, previsão sem histórico e desempate por nome.
/// Os cenários antigos do harness continuam em <see cref="AnalyticsParityTests.Deviation158"/> como evidência do Flask.
/// </summary>
public class Deviation158Tests
{
    private static JsonElementScenario Scenario(string name) =>
        new(AnalyticsScenarios.All().Single(s => s.GetProperty("name").GetString() == name));

    private readonly record struct JsonElementScenario(System.Text.Json.JsonElement Element)
    {
        public async Task<(string Reply, string Prompt)> RunAsync()
        {
            var config = Element.GetProperty("config");
            var rig = new AnalyticsRig(config);
            var body = Element.GetProperty("body");
            var number = body.GetProperty("numero_remetente").GetString()!.Split('@')[0];
            var reply = await rig.Router.DispatchAsync(
                config.GetProperty("intent").GetString()!,
                new IntentContext(new WhatsAppUser(7, "Ana"), number, body.GetProperty("texto").GetString()!, null),
                CancellationToken.None);
            return (reply, rig.Llm.AnalysisPrompts.Single());
        }
    }

    [Theory]
    [Trait("AC", "012-701")]
    [InlineData("analise_completa")]
    [InlineData("analise_variacao_negativa")]
    [InlineData("analise_so_mes_atual")]
    [InlineData("previsao_completa")]
    [InlineData("previsao_historico_curto")]
    [InlineData("previsao_media_domina")]
    public async Task Prompts_CarryAbsoluteValuesInBrazilianFormat(string name)
    {
        var (_, prompt) = await Scenario(name).RunAsync();

        prompt.ShouldNotContain("R$ -");
        prompt.ShouldNotContain("R$ R$");
        Regex.IsMatch(prompt, @"R\$ \d{1,3}(,\d{3})+\.\d{2}").ShouldBeFalse("formato inglês {:,.2f} no prompt");
        Regex.IsMatch(prompt, @"R\$ \d{1,3}(\.\d{3})*,\d{2}").ShouldBeTrue();
    }

    [Theory]
    [Trait("AC", "012-701")]
    [InlineData(-1234.5, "1.234,50")]
    [InlineData(1234.5, "1.234,50")]
    [InlineData(-0.0, "0,00")]
    [InlineData(-9.99, "9,99")]
    public void PromptMoney_IsTheAbsoluteValueInPtBr(double value, string expected) =>
        AnalyticsReport.PromptMoney(value).ShouldBe(expected);

    [Fact]
    [Trait("AC", "012-701")]
    public void Variation_IsComputedBetweenAbsoluteSpendings()
    {
        var data = new SpendingAnalysis(
            "2026-10",
            [new MonthTotal("2026-10", -150), new MonthTotal("2026-09", -100)],
            [], [], [], [], []);

        var prompt = AnalyticsReport.Prompt(data);

        prompt.ShouldContain("- Total gasto este mês: R$ 150,00");
        prompt.ShouldContain("- Mês anterior: R$ 100,00");
        prompt.ShouldContain("- Variação: +50.0%");
    }

    [Fact]
    [Trait("AC", "012-701")]
    public async Task Forecast_WithoutHistory_RespondsInsteadOfFailing()
    {
        var (reply, prompt) = await Scenario(AnalyticsParityTests.Deviation158NoHistory).RunAsync();

        reply.ShouldStartWith("📈 *Previsão de Gastos*");
        prompt.ShouldContain("- Média mensal: R$ 0,00");
    }

    [Fact]
    [Trait("AC", "012-702")]
    public void MonthlyComparison_TiesAreBrokenByCategoryName()
    {
        var rows = new MacroMonthTotal[]
        {
            new("2026-10", "Beta", -200), new("2026-10", "Alfa", -100), new("2026-09", "Alfa", -50), new("2026-09", "Beta", -100),
        };

        var text = MonthlyComparison.Format("2026-10", "2026-09", rows);

        text.IndexOf("*Alfa:*", StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("*Beta:*", StringComparison.Ordinal));
    }
}
