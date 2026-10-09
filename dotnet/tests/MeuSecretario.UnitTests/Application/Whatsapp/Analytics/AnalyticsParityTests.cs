using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Domain.Whatsapp.Analytics;

namespace MeuSecretario.UnitTests.Application.Whatsapp.Analytics;

/// <summary>
/// Paridade da 012g (análises e gráfico) com o <c>WhatsAppHandler</c> real do Flask: as intenções reais de <c>analytics_intents.py</c> sobre
/// <c>analytics_service</c>, <c>forecast_service</c> e <c>chart_service</c> reais (banco falso, Gemini roteirizado, matplotlib simulado).
/// Mesma mensagem e mesmas linhas do banco dão a mesma resposta, o mesmo prompt do Gemini, o mesmo início de período e os mesmos dados do gráfico.
/// </summary>
public class AnalyticsParityTests
{
    private static readonly string[] IgnoredCallPrefixes = ["user_service.", "gemini_service.get_message_intent", "route_intent:"];

    [Fact]
    [Trait("AC", "012-701")]
    [Trait("AC", "012-702")]
    [Trait("AC", "012-703")]
    public async Task EveryScenario_MatchesTheFlask()
    {
        var scenarios = AnalyticsScenarios.All().ToList();
        scenarios.Count.ShouldBeGreaterThan(70);

        var failures = new List<string>();
        foreach (var scenario in scenarios)
        {
            var name = scenario.GetProperty("name").GetString()!;
            try
            {
                await RunAsync(scenario, name, failures);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    private static async Task RunAsync(JsonElement scenario, string name, List<string> failures)
    {
        var config = scenario.GetProperty("config");
        var result = scenario.GetProperty("result");
        var rig = new AnalyticsRig(config);

        var body = scenario.GetProperty("body");
        var rawNumber = body.GetProperty("numero_remetente").GetString()!;
        var number = rawNumber.Split('@')[0];
        var text = body.GetProperty("texto").GetString()!;
        var intent = config.GetProperty("intent").GetString()!;

        var reply = await rig.Router.DispatchAsync(intent, new IntentContext(new WhatsAppUser(7, "Ana"), number, text, null), CancellationToken.None);

        var expectedReply = result.GetProperty("body").GetProperty("resposta").GetString();
        if (reply != expectedReply)
        {
            failures.Add($"{name}: resposta\n  esperada: {expectedReply}\n  obtida:   {reply}");
        }

        var extra = result.GetProperty("extra");

        var expectedPrompts = extra.GetProperty("prompts").EnumerateArray().Select(p => p.GetProperty("prompt").GetString()!).ToList();
        if (!rig.Llm.AnalysisPrompts.SequenceEqual(expectedPrompts))
        {
            failures.Add($"{name}: prompt\n  esperado: {string.Join("\n--\n", expectedPrompts)}\n  obtido:   {string.Join("\n--\n", rig.Llm.AnalysisPrompts)}");
        }

        foreach (var request in rig.Llm.AnalysisRequests)
        {
            if (request.UserId != 7)
            {
                failures.Add($"{name}: o Gemini deve receber o usuário (desvio 063)");
            }
        }

        var expectedCalls = result.GetProperty("calls").EnumerateArray()
            .Select(c => c.GetString()!)
            .Where(c => !IgnoredCallPrefixes.Any(prefix => c.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();
        if (!rig.Calls.SequenceEqual(expectedCalls))
        {
            failures.Add($"{name}: chamadas\n  esperadas: {string.Join(", ", expectedCalls)}\n  obtidas:   {string.Join(", ", rig.Calls)}");
        }

        var expectedSent = extra.GetProperty("sent").EnumerateArray()
            .Select(s => $"{s.GetProperty("numero").GetString()}|{s.GetProperty("bytes").GetBoolean()}|{s.GetProperty("caption").GetString()}").ToList();
        if (!rig.Bot.Sent.SequenceEqual(expectedSent))
        {
            failures.Add($"{name}: envios\n  esperados: {string.Join(", ", expectedSent)}\n  obtidos:   {string.Join(", ", rig.Bot.Sent)}");
        }

        var expectedStarts = extra.GetProperty("sql").EnumerateArray()
            .Where(s => s[0].GetString() is "pie" or "bar" or "line")
            .Select(s => $"{s[0].GetString()}|{s[1].GetProperty("data_inicio").GetString()}").ToList();
        if (!rig.Reader.Starts.SequenceEqual(expectedStarts))
        {
            failures.Add($"{name}: início do período\n  esperado: {string.Join(", ", expectedStarts)}\n  obtido:   {string.Join(", ", rig.Reader.Starts)}");
        }

        CompareChart(name, extra.GetProperty("plt"), rig.Renderer, failures);
    }

    private static void CompareChart(string name, JsonElement plt, RecordingRenderer renderer, List<string> failures)
    {
        if (plt.GetProperty("pie").ValueKind == JsonValueKind.Object)
        {
            var pie = plt.GetProperty("pie");
            var labels = pie.GetProperty("labels").EnumerateArray().Select(l => l.GetString()!).ToList();
            var values = pie.GetProperty("values").EnumerateArray().Select(v => v.GetDouble()).ToList();
            if (renderer.Pie is null || !renderer.Pie.Select(p => p.Label).SequenceEqual(labels) || !renderer.Pie.Select(p => p.Value).SequenceEqual(values))
            {
                failures.Add($"{name}: dados da pizza");
            }
        }
        else if (renderer.Pie is not null)
        {
            failures.Add($"{name}: a pizza não deveria ser desenhada");
        }

        var bars = plt.GetProperty("bar");
        if (bars.GetArrayLength() == 2)
        {
            var expenses = bars[0].GetProperty("values").EnumerateArray().Select(v => v.GetDouble()).ToList();
            var income = bars[1].GetProperty("values").EnumerateArray().Select(v => v.GetDouble()).ToList();
            var labels = plt.GetProperty("xticklabels").EnumerateArray().Select(l => l.GetString()!).ToList();
            if (renderer.Bars is null
                || !renderer.Bars.Select(b => b.Label).SequenceEqual(labels)
                || !renderer.Bars.Select(b => b.Expenses).SequenceEqual(expenses)
                || !renderer.Bars.Select(b => b.Income).SequenceEqual(income))
            {
                failures.Add($"{name}: dados das barras");
            }
        }
        else if (renderer.Bars is not null)
        {
            failures.Add($"{name}: as barras não deveriam ser desenhadas");
        }

        if (plt.GetProperty("plot").ValueKind == JsonValueKind.Object)
        {
            var plot = plt.GetProperty("plot");
            var dates = plot.GetProperty("x").EnumerateArray().Select(x => x.GetString()!).ToList();
            var balances = plot.GetProperty("y").EnumerateArray().Select(y => y.GetDouble()).ToList();
            if (renderer.Line is null
                || !renderer.Line.Select(p => p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).SequenceEqual(dates)
                || !renderer.Line.Select(p => p.Balance).SequenceEqual(balances))
            {
                failures.Add($"{name}: dados da linha");
            }
        }
        else if (renderer.Line is not null)
        {
            failures.Add($"{name}: a linha não deveria ser desenhada");
        }
    }
}
