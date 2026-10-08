using System.Text.Json;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

public class PaymentDomainTests
{
    private static PaymentExtraction Parse(string json) => PaymentExtraction.Parse(JsonDocument.Parse(json).RootElement);

    [Fact]
    [Trait("AC", "012-205")]
    public void Extraction_DefaultsAndTruthiness()
    {
        var empty = Parse("{}");
        empty.Items.ShouldBeEmpty();
        empty.TotalValue.ShouldBeNull();
        empty.TriggerWord.ShouldBe("paguei");
        empty.HasTotal.ShouldBeFalse();

        var full = Parse("""{"itens": ["a", "b"], "valor_total": 12.5, "trigger_word": "quitei"}""");
        full.Items.ShouldBe(["a", "b"]);
        full.HasTotal.ShouldBeTrue();
        full.TriggerWord.ShouldBe("quitei");

        Parse("""{"itens": null, "valor_total": 0, "trigger_word": null}""").TriggerWord.ShouldBe("None");
        Parse("""{"valor_total": 0}""").HasTotal.ShouldBeFalse();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"itens": "x"}""")]
    [InlineData("""{"itens": [1]}""")]
    [InlineData("""{"valor_total": "10"}""")]
    public void Extraction_ShapesThatBreakTheFlask_Throw(string json) =>
        Should.Throw<InvalidOperationException>(() => Parse(json));

    [Fact]
    [Trait("AC", "012-204")]
    public void Reply_AsksForValues_WhenNothingWasDone()
    {
        PaymentReply.Build([], [], ["luz"], "paguei").ShouldBe("🤔 Para processar 'luz', preciso do valor.\n\nExemplo: *paguei luz 150*");
        PaymentReply.Build([], [], ["luz", "gás"], "paguei")
            .ShouldBe("🤔 Para processar 'luz', 'gás', preciso dos valores.\n\nTente informar um item por vez com o valor.");
    }

    [Fact]
    [Trait("AC", "012-204")]
    public void Reply_Combines_SettledExpensesAndMissing()
    {
        var text = PaymentReply.Build(
            [new SettledBill("Internet", 100, 100, "FIXO", "Telecom")],
            [new CreatedExpense("pizza", 40, "Padaria", "Nubank")],
            ["seguro"],
            "paguei");

        text.ShouldContain("✅ *CONTA QUITADA* ✅");
        text.ShouldContain("\n━━━━━━━━━━━━━━━━━━━━\n\n💸 *DESPESA REGISTRADA* 💸");
        text.ShouldEndWith("\n\n⚠️ *Itens não processados* (faltou valor):\n• seguro\n");
    }

    [Fact]
    public void Reply_NothingAtAll_IsEmpty() => PaymentReply.Build([], [], [], "paguei").ShouldBe(string.Empty);
}
