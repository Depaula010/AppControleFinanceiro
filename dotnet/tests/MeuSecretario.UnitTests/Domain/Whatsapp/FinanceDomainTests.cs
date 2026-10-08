using System.Text.Json;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Referência gerada pelo Python (generate_whatsapp_fixtures.py, seções 012a/b/c): whatsapp-financial-fixtures.json.</summary>
internal static class FinanceFixture
{
    private static readonly JsonElement Root = Load();

    public static JsonElement Section(string name) => Root.GetProperty(name);

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "whatsapp-financial-fixtures.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }
}

public class FinanceDomainTests
{
    [Fact]
    [Trait("AC", "012-115")]
    public void FeedbackMessage_MatchesThePythonInEveryVariant()
    {
        var cases = FinanceFixture.Section("feedback_cases").EnumerateArray().ToList();
        cases.Count.ShouldBeGreaterThan(100);
        foreach (var c in cases)
        {
            var tx = c.GetProperty("tx");
            var transaction = new FeedbackTransaction(
                1, 1, 1, 1,
                tx.GetProperty("descricao").GetString(),
                tx.GetProperty("valor").GetDouble(),
                tx.GetProperty("nome_conta").GetString()!,
                null);

            PotFeedback? pot = c.GetProperty("pot") is { ValueKind: JsonValueKind.Object } p
                ? new PotFeedback(p.GetProperty("nome_pote").GetString()!, p.GetProperty("saldo_restante").GetDouble(),
                    p.GetProperty("percentual_usado").GetDouble(), p.GetProperty("periodicidade").GetString()!)
                : null;

            var credit = c.GetProperty("kind").GetString() == "credito";
            FeedbackFooter? footer = null;
            if (c.GetProperty("footer") is { ValueKind: JsonValueKind.Object } f)
            {
                footer = credit
                    ? new FeedbackFooter.Invoice(
                        f.GetProperty("nome_conta").GetString()!, f.GetProperty("valor_total").GetDouble(),
                        f.GetProperty("dia_fechamento").ValueKind == JsonValueKind.Null ? null : f.GetProperty("dia_fechamento").GetInt32())
                    : new FeedbackFooter.Balance(f.GetProperty("nome_conta").GetString()!, f.GetProperty("saldo_disponivel").GetDouble());
            }

            FeedbackMessage.Format(transaction, pot, footer, credit, c.GetProperty("show").GetBoolean())
                .ShouldBe(c.GetProperty("text").GetString());
        }
    }

    [Fact]
    [Trait("AC", "012-115")]
    public void TrafficLight_MatchesThePython()
    {
        foreach (var c in FinanceFixture.Section("emoji_cases").EnumerateArray())
        {
            FeedbackMessage.Light(c.GetProperty("pct").GetDouble()).ShouldBe(c.GetProperty("emoji").GetString());
        }
    }

    [Fact]
    [Trait("AC", "012-115")]
    public void PotAlert_MatchesThePython()
    {
        foreach (var c in FinanceFixture.Section("alert_cases").EnumerateArray())
        {
            var row = c.GetProperty("row");
            var expected = c.GetProperty("show").GetBoolean();
            var actual = row.ValueKind == JsonValueKind.Null
                ? FeedbackMessage.ShouldShowPot(false, false, 0, c.GetProperty("pct").GetDouble())
                : FeedbackMessage.ShouldShowPot(true, row[0].GetBoolean(), row[1].GetInt32(), c.GetProperty("pct").GetDouble());
            actual.ShouldBe(expected);
        }
    }

    [Fact]
    [Trait("AC", "012-107")]
    public void AccountMention_ExtractsTheSameNameAsThePython()
    {
        foreach (var c in FinanceFixture.Section("mention_names").EnumerateArray())
        {
            var expected = c.GetProperty("name").ValueKind == JsonValueKind.Null ? null : c.GetProperty("name").GetString();
            AccountMention.ExtractName(c.GetProperty("message").GetString()!).ShouldBe(expected, c.GetProperty("message").GetString());
        }
    }

    [Theory]
    [InlineData("Nubank", "paguei com o nubank", "Nubank")]
    [InlineData("Cartão Inter", "gastei no cartao inter hoje", "Cartão Inter")]
    public void AccountMention_MatchesTheFuzzyName(string account, string message, string expected)
    {
        var accounts = new[] { new AccountRef(1, account, "Conta Corrente"), new AccountRef(2, "Carteira", "Dinheiro") };
        AccountMention.Match(message, accounts)!.Name.ShouldBe(expected);
    }

    [Fact]
    public void AccountMention_NoKeywordOrNoAccounts_IsNull()
    {
        AccountMention.Match("oi", [new AccountRef(1, "Nubank", "x")]).ShouldBeNull();
        AccountMention.Match("no nubank", []).ShouldBeNull();
    }

    [Fact]
    public void Validate_Messages()
    {
        static ExtractedTransaction P(string json) => ExtractedTransaction.Parse(JsonDocument.Parse(json).RootElement, "Despesa");
        TransactionIntentRules.Validate("Despesa", P("""{"valor_decimal": null}""")).ShouldBe(TransactionIntentRules.MissingValue("Despesa"));
        TransactionIntentRules.Validate("Renda", P("""{"valor_decimal": -1}""")).ShouldBe(TransactionIntentRules.MissingValue("Renda"));
        TransactionIntentRules.Validate("Despesa", P("""{"valor_decimal": 0.001}""")).ShouldBe(TransactionIntentRules.TooLow);
        TransactionIntentRules.Validate("Despesa", P("""{"valor_decimal": 5, "parcelamento": 49}""")).ShouldBe(TransactionIntentRules.InvalidInstallments);
        TransactionIntentRules.Validate("Renda", P("""{"valor_decimal": 5, "parcelamento": 49}""")).ShouldBeNull();
        TransactionIntentRules.Validate("Despesa", P("""{"valor_decimal": 5, "parcelamento": 12}""")).ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => TransactionIntentRules.Validate("Despesa", P("""{"valor_decimal": "x"}""")));
    }
}
