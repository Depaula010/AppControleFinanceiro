using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

public class ConfirmationFlowTests
{
    private static string StatusText(ConfirmationStatus status) => status switch
    {
        ConfirmationStatus.Saved => "saved",
        ConfirmationStatus.AwaitingCategory => "awaiting_category",
        ConfirmationStatus.Cancelled => "cancelled",
        _ => "error",
    };

    [Fact]
    [Trait("AC", "012-018")]
    public void Process_MatchesProcessConfirmationResponseInEveryScenario()
    {
        var cases = WhatsAppFixture.Cases("confirmation_cases");
        cases.Count.ShouldBeGreaterThan(30);

        foreach (var c in cases)
        {
            var name = c.GetProperty("name").GetString();
            var pendingElement = c.GetProperty("pending");
            var pending = pendingElement.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(pendingElement.GetRawText())!.AsObject();
            var id = c.GetProperty("id").ValueKind == JsonValueKind.Null ? null : c.GetProperty("id").GetString();

            var outcome = ConfirmationFlow.Process(c.GetProperty("message").GetString()!, id, pending);

            StatusText(outcome.Status).ShouldBe(c.GetProperty("status").GetString(), name);
            outcome.Message.ShouldBe(c.GetProperty("reply").ValueKind == JsonValueKind.Null ? null : c.GetProperty("reply").GetString(), name);

            var data = c.GetProperty("data");
            if (data.ValueKind == JsonValueKind.Null)
            {
                outcome.Data.ShouldBeNull(name);
            }
            else
            {
                JsonNode.DeepEquals(outcome.Data, JsonNode.Parse(data.GetRawText())).ShouldBeTrue(name);
            }

            var ops = c.GetProperty("ops").EnumerateArray().Select(o => o[0].GetString() + ":" + o[1].GetString()).ToList();
            var expectedEffect = ops.Count switch
            {
                0 => ConfirmationEffect.None,
                1 when ops[0].StartsWith("set:", StringComparison.Ordinal) => ConfirmationEffect.SavePending,
                2 => ConfirmationEffect.DeleteBoth,
                _ => throw new InvalidOperationException("ops inesperadas em " + name),
            };
            outcome.Effect.ShouldBe(expectedEffect, name);
        }
    }

    [Fact]
    [Trait("AC", "012-111")]
    public void CategorySelection_MatchesFormatCategorySelectionMessage()
    {
        foreach (var c in WhatsAppFixture.Cases("category_selection_cases"))
        {
            var categories = c.GetProperty("categories").EnumerateArray()
                .Select(e => new CategoryOption(e.GetProperty("id").GetInt32(), e.GetProperty("nome_sub").GetString()!, e.GetProperty("nome_macro").GetString()!))
                .ToList();

            var (text, map) = CategorySelection.Format(categories);

            text.ShouldBe(c.GetProperty("text").GetString());
            var expectedMap = c.GetProperty("map").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
            map.ShouldBe(expectedMap);
        }
    }

    [Fact]
    [Trait("AC", "012-018")]
    public void Process_KeepsUnknownKeysOfThePendingRecord()
    {
        var pending = JsonNode.Parse("""{"usuario_id": 1, "extra": {"k": [1, "ç"]}, "categoria_id": 59}""")!.AsObject();

        var outcome = ConfirmationFlow.Process("trocar", "abc", pending);

        outcome.Status.ShouldBe(ConfirmationStatus.AwaitingCategory);
        outcome.Data!["extra"]!["k"]![1]!.GetValue<string>().ShouldBe("ç");
        outcome.Data["awaiting_category_selection"]!.GetValue<bool>().ShouldBeTrue();
    }
}
