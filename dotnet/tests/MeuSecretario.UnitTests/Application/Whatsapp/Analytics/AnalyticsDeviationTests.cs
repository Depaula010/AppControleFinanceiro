using System.Text.Json;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>
/// Desvios aprovados da 012 (2026-10-10) nas análises e no gráfico: 230 (período validado), 232 (Comparação Mensal em valor absoluto e
/// pt-BR) e 233 (dias da semana em português). Os cenários do Flask seguem no harness como evidência do comportamento antigo.
/// </summary>
public class AnalyticsDeviationTests
{
    private static async Task<(string Reply, AnalyticsRig Rig)> RunAsync(string configJson, string message = "grafico")
    {
        using var document = JsonDocument.Parse(configJson);
        var config = document.RootElement.Clone();
        var rig = new AnalyticsRig(config);
        var reply = await rig.Router.DispatchAsync(
            config.GetProperty("intent").GetString()!, new IntentContext(new WhatsAppUser(7, "Ana"), "5531999990001", message, null), CancellationToken.None);
        return (reply, rig);
    }

    private static string Chart(string chartInfo, string rows = """[["Moradia", 1500.0], ["Lazer", 200.0]]""") =>
        $$"""
        {"intent": "Gráfico de Gastos", "llm": {"extract_chart_type": {{JsonSerializer.Serialize(chartInfo)}}},
         "pie_rows": {{rows}}, "bar_rows": [["2026-09-01", 100.0, 200.0]], "saldo_inicial": 10.0, "line_rows": [["2026-10-01", 5.0]]}
        """;

    // ---------------------------------------------------------------- 230

    [Theory]
    [Trait("AC", "012-230")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": 7.5}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": "30"}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": [30]}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": null}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": true}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": -3}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": 0}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": 3651}""")]
    [InlineData("""{"tipo_grafico": "pizza", "periodo_dias": 99999999999}""")]
    public async Task Pie_InvalidPeriod_UsesTheDefault30Days(string chartInfo)
    {
        var (reply, rig) = await RunAsync(Chart(chartInfo));

        reply.ShouldBe("✅ 📊 Gastos por Categoria - Últimos 30 dias");
        rig.Reader.Starts.ShouldBe(["pie|2026-09-07"]);
        rig.Renderer.Pie.ShouldNotBeNull();
    }

    [Theory]
    [Trait("AC", "012-230")]
    [InlineData("7", "2026-09-30")]
    [InlineData("1", "2026-10-06")]
    [InlineData("3650", "2016-10-09")]
    [InlineData("7.0", "2026-09-30")]
    public async Task Pie_ValidPeriod_IsHonouredAsAnInteger(string days, string start)
    {
        var (reply, rig) = await RunAsync(Chart($$"""{"tipo_grafico": "pizza", "periodo_dias": {{days}} }"""));

        var shown = days == "7.0" ? "7" : days;
        reply.ShouldBe($"✅ 📊 Gastos por Categoria - Últimos {shown} dias");
        rig.Reader.Starts.ShouldBe([$"pie|{start}"]);
    }

    [Theory]
    [Trait("AC", "012-230")]
    [InlineData("barras", "Evolução Mensal", "bar")]
    [InlineData("linha", "📈 Evolução do Saldo", "line")]
    public async Task BarsAndLine_InvalidMonths_UseTheDefault6Months(string type, string label, string kind)
    {
        foreach (var months in new[] { "2.5", "\"6\"", "null", "true", "-1", "0", "121", "99999999999" })
        {
            var (reply, rig) = await RunAsync(Chart($$"""{"tipo_grafico": "{{type}}", "num_meses": {{months}} }"""));

            reply.ShouldContain("Últimos 6 meses", Case.Sensitive, months);
            reply.ShouldContain(label.Replace("📈 ", string.Empty), Case.Sensitive, months);
            rig.Reader.Starts.ShouldBe([$"{kind}|2026-04-10"], months);
        }
    }

    [Theory]
    [Trait("AC", "012-230")]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(120)]
    public async Task BarsAndLine_ValidMonths_AreHonoured(int months)
    {
        var (reply, _) = await RunAsync(Chart($$"""{"tipo_grafico": "barras", "num_meses": {{months}} }"""));

        reply.ShouldBe($"✅ 📊 Evolução Mensal - Últimos {months} meses");
    }

    // ---------------------------------------------------------------- 232

    private static string Comparison(string rows) => $$"""{"intent": "Comparação Mensal", "mc": {{rows}}}""";

    [Fact]
    [Trait("AC", "012-232")]
    public async Task MonthlyComparison_UsesAbsoluteValuesAndPtBrNumbers()
    {
        // As mesmas linhas do cenário comparacao_completa do Flask (despesas negativas): o texto antigo fica no harness como evidência.
        var (reply, _) = await RunAsync(Comparison(
            """
            [["2026-10", "Moradia", -1500.0], ["2026-09", "Moradia", -1400.0], ["2026-10", "Alimentação", -700.0], ["2026-09", "Alimentação", -900.0],
             ["2026-09", "Lazer", -300.0], ["2026-10", "Transporte", -250.0], ["2026-09", "Transporte", -200.0], ["2026-10", "Saúde", -60.0],
             ["2026-09", "Saúde", -55.0], ["2026-10", "Educação", -45.5], ["2026-09", "Educação", -40.0], ["2026-10", "Pets", -12.5],
             ["2026-09", "Pets", -11.0]]
            """));

        reply.ShouldBe(
            "📊 *Comparação: Outubro/2026 vs Setembro/2026*\n\n" +
            "💰 *Outubro/2026:* R$ 2.568,00\n" +
            "💰 *Setembro/2026:* R$ 2.906,00\n" +
            "📉 *Variação:* -11,6%\n\n" +
            "*Maiores mudanças por categoria:*\n" +
            "🟢 *Lazer:* R$ 0,00 (-100,0%)\n" +
            "🔴 *Transporte:* R$ 250,00 (+25,0%)\n" +
            "🟢 *Alimentação:* R$ 700,00 (-22,2%)\n" +
            "🔴 *Educação:* R$ 45,50 (+13,8%)\n" +
            "🔴 *Pets:* R$ 12,50 (+13,6%)\n");
    }

    [Fact]
    [Trait("AC", "012-232")]
    public async Task MonthlyComparison_SpendingMore_IsAPositiveVariationInRed()
    {
        var (reply, _) = await RunAsync(Comparison("""[["2026-10", "Lazer", -1200.5], ["2026-09", "Lazer", -1000.0]]"""));

        reply.ShouldContain("📈 *Variação:* +20,1%");
        reply.ShouldContain("🔴 *Lazer:* R$ 1.200,50 (+20,1%)");
        reply.ShouldNotContain("R$ -");
        reply.ShouldNotContain("1,200");
    }

    [Fact]
    [Trait("AC", "012-232")]
    public void MonthlyComparison_TiedVariations_AreOrderedByCategoryName()
    {
        // O Flask iterava um set (ordem aleatória por processo); agora o desempate é o nome da categoria.
        var rows = new MacroMonthTotal[]
        {
            new("2026-10", "Zeta", 200), new("2026-10", "Beta", 200), new("2026-10", "Alfa", 100),
            new("2026-09", "Zeta", 100), new("2026-09", "Beta", 100), new("2026-09", "Alfa", 50),
        };

        var text = MonthlyComparison.Format("2026-10", "2026-09", rows);

        var lines = text.Split('\n').Where(l => l.Contains("(+100,0%)", StringComparison.Ordinal)).ToList();
        lines.Count.ShouldBe(3);
        lines[0].ShouldContain("*Alfa:*");
        lines[1].ShouldContain("*Beta:*");
        lines[2].ShouldContain("*Zeta:*");
        text.ShouldContain("🔴 *Beta:* R$ 200,00 (+100,0%)");
    }

    // ---------------------------------------------------------------- 233

    [Theory]
    [Trait("AC", "012-233")]
    [InlineData("Sunday", "Domingo")]
    [InlineData("Monday", "Segunda-feira")]
    [InlineData("Tuesday", "Terça-feira")]
    [InlineData("Wednesday", "Quarta-feira")]
    [InlineData("Thursday", "Quinta-feira")]
    [InlineData("Friday", "Sexta-feira")]
    [InlineData("Saturday", "Sábado")]
    [InlineData("Wednesday ", "Quarta-feira")]
    [InlineData("Funday", "Funday")]
    public void WeekdayNames_TranslateToPortuguese(string english, string expected) => WeekdayNames.ToPortuguese(english).ShouldBe(expected);

    [Fact]
    [Trait("AC", "012-233")]
    public void AnalysisPrompt_ListsTheWeekdaysInPortuguese()
    {
        var data = new SpendingAnalysis(
            "2026-10",
            [new MonthTotal("2026-10", -100)],
            [],
            [new WeekdaySpend("Monday", -150.5, 4), new WeekdaySpend("Sunday", -80, 2)],
            [],
            [new TopExpense("Aluguel", -250, new DateOnly(2026, 10, 5))],
            []);

        var prompt = AnalyticsReport.Prompt(data);

        prompt.ShouldContain("- Segunda-feira: R$ ");
        prompt.ShouldContain("- Domingo: R$ ");
        prompt.ShouldNotContain("Monday");
        prompt.ShouldNotContain("Sunday");
    }
}
