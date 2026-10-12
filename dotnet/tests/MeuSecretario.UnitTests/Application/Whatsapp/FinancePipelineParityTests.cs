using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>
/// Paridade das sub-entregas financeiras (012a, 012b, 012c) com o <c>WhatsAppHandler</c> real do Flask: mesma mensagem, mesmo estado no
/// Redis e mesmo roteiro do Gemini dão a mesma resposta, a mesma sequência de chamadas e o mesmo Redis no fim (AC-012-017).
/// </summary>
public class FinancePipelineParityTests
{
    /// <summary>Sub-entregas financeiras já implementadas; o harness traz cenários de todas.</summary>
    private static readonly string[] ImplementedGroups = ["012a", "012b", "012c"];

    /// <summary>
    /// Cenários do harness que gravam o comportamento ANTIGO do Flask e que um desvio aprovado já substituiu. Ficam no harness como
    /// evidência do comportamento antigo e saem da comparação estrita; os testes de <see cref="WhatsAppDeviationTests"/> provam o novo.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownDivergences = new Dictionary<string, string>
    {
        ["resposta_ok_sem_descricao_final_falha"] = "150",
        ["resposta_ok_parcelada_sem_descricao_final"] = "150",
        ["transferencia_com_contas"] = "151",
    };

    /// <summary>
    /// O Flask lê a lista de contas por SQL direto em vários pontos; só o resultado interessa, não a chamada.
    /// </summary>
    private static readonly string[] IgnoredCalls = ["finance_service.get_user_accounts"];

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryFinanceScenario_MatchesTheFlask()
    {
        var scenarios = FinanceScenarios.All(ImplementedGroups)
            .Where(s => !KnownDivergences.ContainsKey(s.GetProperty("name").GetString()!))
            .ToList();
        scenarios.Count.ShouldBeGreaterThan(40);

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
        var rig = new FinanceRig(config);

        if (config.TryGetProperty("redis", out var redis))
        {
            foreach (var entry in redis.EnumerateObject())
            {
                await rig.SeedAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()));
            }
        }

        var status = result.GetProperty("status").GetInt32();
        var body = scenario.GetProperty("body");
        var number = body.GetProperty("numero_remetente").GetString()!;
        var text = body.GetProperty("texto").GetString()!;

        string? reply = null;
        Exception? thrown = null;
        try
        {
            reply = await rig.SendAsync(text, number);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            thrown = ex;
        }

        if (status == 200)
        {
            if (thrown is not null)
            {
                failures.Add($"{name}: lançou {thrown.GetType().Name}: {thrown.Message}");
                return;
            }

            var expectedReply = result.GetProperty("body").GetProperty("resposta").GetString();
            if (reply != expectedReply)
            {
                failures.Add($"{name}: resposta\n  esperada: {expectedReply}\n  obtida:   {reply}");
            }
        }
        else if (thrown is null)
        {
            failures.Add($"{name}: o Flask respondeu {status} mas a resposta foi '{reply}'");
        }
        else if ((status == 429) != WhatsAppTexts.IsQuotaMessage(thrown.Message))
        {
            failures.Add($"{name}: status {status} do Flask, exceção '{thrown.GetType().Name}' (cota: {WhatsAppTexts.IsQuotaMessage(thrown.Message)})");
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
            return;
        }

        foreach (var entry in result.GetProperty("redis").EnumerateObject())
        {
            var actual = JsonNode.Parse(rig.Cache.RawValue(entry.Name)!);
            var expected = JsonNode.Parse(entry.Value.GetRawText());
            if (!JsonNode.DeepEquals(actual, expected))
            {
                failures.Add($"{name}: valor de {entry.Name}\n  esperado: {expected?.ToJsonString()}\n  obtido:   {actual?.ToJsonString()}");
            }
        }
    }
}
