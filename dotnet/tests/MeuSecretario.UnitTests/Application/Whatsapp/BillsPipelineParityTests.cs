using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>
/// Paridade da 012d (contas fixas, vencimentos, contas atrasadas e resposta ao check-in) com o <c>WhatsAppHandler</c> real do Flask:
/// mesma mensagem, mesmo banco simulado e mesmo Redis dão a mesma resposta, a mesma sequência de consultas e o mesmo Redis no fim (AC-012-017).
/// </summary>
public class BillsPipelineParityTests
{
    /// <summary>Resolver a fatura do cartão é detalhe do motor de agendamentos (013); o rótulo do Flask não tem equivalente aqui.</summary>
    private static readonly string[] IgnoredCalls = ["finance_service.get_or_create_fatura"];

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryBillsScenario_MatchesTheFlask()
    {
        var scenarios = BillsScenarios.All.ToList();
        scenarios.Count.ShouldBeGreaterThan(60);

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
        var rig = new BillsRig(config);

        if (config.TryGetProperty("redis", out var redis))
        {
            foreach (var entry in redis.EnumerateObject())
            {
                await rig.SeedAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()));
            }
        }

        var body = scenario.GetProperty("body");
        var reply = await rig.SendAsync(body.GetProperty("texto").GetString()!, body.GetProperty("numero_remetente").GetString()!);

        result.GetProperty("status").GetInt32().ShouldBe(200, name);
        var expectedReply = result.GetProperty("body").GetProperty("resposta").GetString();

        // Desvio 008: o Flask escreve str(e) depois dos dois-pontos; aqui a mensagem termina no ponto.
        if (scenario.GetProperty("deviation").GetString() == "008" && expectedReply!.StartsWith("❌ Erro ao salvar transações: ", StringComparison.Ordinal))
        {
            expectedReply = CheckinReplyTexts.SaveFailed;
        }

        if (reply != expectedReply)
        {
            failures.Add($"{name}: resposta\n  esperada: {expectedReply}\n  obtida:   {reply}");
        }

        var expectedCalls = result.GetProperty("calls").EnumerateArray()
            .Select(c => c.GetString()!)
            .Where(c => !c.StartsWith("route_intent:", StringComparison.Ordinal) && !c.StartsWith("registry:", StringComparison.Ordinal))
            .Where(c => !IgnoredCalls.Contains(c))
            .ToList();
        if (!rig.Calls.SequenceEqual(expectedCalls))
        {
            failures.Add($"{name}: chamadas\n  esperadas: {string.Join(", ", expectedCalls)}\n  obtidas:   {string.Join(", ", rig.Calls)}");
        }

        var keys = (await rig.Cache.KeysByPatternAsync("*")).Order(StringComparer.Ordinal).ToList();
        var expectedKeys = result.GetProperty("redis").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
        if (!keys.SequenceEqual(expectedKeys))
        {
            failures.Add($"{name}: chaves do Redis\n  esperadas: {string.Join(", ", expectedKeys)}\n  obtidas:   {string.Join(", ", keys)}");
        }
    }
}
