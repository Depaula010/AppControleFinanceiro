using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Whatsapp;

namespace MeuSecretario.UnitTests.Application.Whatsapp;

/// <summary>
/// Paridade do pipeline com o <c>WhatsAppHandler</c> real do Flask (harness Python, spec 012 AC-012-017): a mesma mensagem, o mesmo
/// estado inicial no Redis e o mesmo roteiro do Gemini devem dar a mesma resposta, as mesmas chamadas e o mesmo Redis no fim.
/// </summary>
public class WhatsAppPipelineParityTests
{
    /// <summary>Sub-entregas já implementadas; o harness traz cenários das demais.</summary>
    private static readonly string[] ImplementedGroups = ["012-0"];

    [Fact]
    [Trait("AC", "012-017")]
    public async Task EveryPipelineScenario_MatchesTheFlask()
    {
        var scenarios = PipelineScenarios.All("pipeline", ImplementedGroups).ToList();
        scenarios.Count.ShouldBeGreaterThan(10);

        foreach (var scenario in scenarios)
        {
            var name = scenario.GetProperty("name").GetString()!;
            var config = scenario.GetProperty("config");
            var result = scenario.GetProperty("result");

            var userExists = !config.TryGetProperty("user", out var user) || user.GetBoolean();
            var checkinReply = config.TryGetProperty("checkin_response", out var checkin) ? checkin[1].GetString()! : "RESPOSTA-DO-CHECKIN";
            var rig = new WhatsAppRig(userExists, checkinReply: checkinReply);

            if (config.TryGetProperty("redis", out var redis))
            {
                foreach (var entry in redis.EnumerateObject())
                {
                    await rig.SeedAsync(entry.Name, JsonNode.Parse(entry.Value.GetRawText()));
                }
            }

            if (config.TryGetProperty("intent", out var intent))
            {
                rig.Llm.Then(intent.ValueKind == JsonValueKind.Null ? """{"intent": null}""" : JsonSerializer.Serialize(new { intent = intent.GetString() }));
            }

            var body = scenario.GetProperty("body");
            var reply = await rig.SendAsync(body.GetProperty("texto").GetString()!, body.GetProperty("numero_remetente").GetString()!);

            reply.ShouldBe(result.GetProperty("body").GetProperty("resposta").GetString(), name);

            var expectedCalls = result.GetProperty("calls").EnumerateArray()
                .Select(c => c.GetString()!)
                .Where(c => !c.StartsWith("route_intent:", StringComparison.Ordinal) && !c.StartsWith("registry:", StringComparison.Ordinal))
                .ToList();
            rig.Calls.ShouldBe(expectedCalls, name);

            var keys = (await rig.Cache.KeysByPatternAsync("*")).Order(StringComparer.Ordinal).ToList();
            var expectedKeys = result.GetProperty("redis").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList();
            keys.ShouldBe(expectedKeys, name);
            foreach (var entry in result.GetProperty("redis").EnumerateObject())
            {
                JsonNode.DeepEquals(JsonNode.Parse(rig.Cache.RawValue(entry.Name)!), JsonNode.Parse(entry.Value.GetRawText())).ShouldBeTrue($"{name}: {entry.Name}");
            }
        }
    }
}
