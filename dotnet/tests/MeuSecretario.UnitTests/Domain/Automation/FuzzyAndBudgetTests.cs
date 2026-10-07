using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.UnitTests.Domain.Automation;

public class FuzzyScorerTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    [Trait("AC", "011-141")]
    public void EveryScorer_MatchesRapidfuzzForEveryFixture()
    {
        var cases = AutomationFixture.Cases("fuzz");
        cases.Count.ShouldBeGreaterThan(40);
        foreach (var c in cases)
        {
            var q = c.GetProperty("query").GetString()!;
            var s = c.GetProperty("choice").GetString()!;
            var label = $"'{q}' x '{s}'";
            FuzzyScorer.Ratio(q, s).ShouldBe(c.GetProperty("ratio").GetDouble(), Tolerance, "ratio " + label);
            FuzzyScorer.PartialRatio(q, s).ShouldBe(c.GetProperty("partial_ratio").GetDouble(), Tolerance, "partial_ratio " + label);
            FuzzyScorer.TokenSortRatio(q, s).ShouldBe(c.GetProperty("token_sort_ratio").GetDouble(), Tolerance, "token_sort " + label);
            FuzzyScorer.TokenSetRatio(q, s).ShouldBe(c.GetProperty("token_set_ratio").GetDouble(), Tolerance, "token_set " + label);
            FuzzyScorer.PartialTokenSortRatio(q, s).ShouldBe(c.GetProperty("partial_token_sort_ratio").GetDouble(), Tolerance, "partial_token_sort " + label);
            FuzzyScorer.PartialTokenSetRatio(q, s).ShouldBe(c.GetProperty("partial_token_set_ratio").GetDouble(), Tolerance, "partial_token_set " + label);
            FuzzyScorer.WRatio(q, s).ShouldBe(c.GetProperty("wratio").GetDouble(), Tolerance, "WRatio " + label);
        }
    }

    [Fact]
    [Trait("AC", "011-141")]
    public void ExtractOne_MatchesRapidfuzzForEveryFixture()
    {
        foreach (var c in AutomationFixture.Cases("extract_one"))
        {
            var choices = c.GetProperty("choices").EnumerateArray().Select(e => e.GetString()!).Distinct().ToList();
            var query = c.GetProperty("query").GetString()!;
            var result = FuzzyScorer.ExtractOne(query, choices, BillMatching.Threshold);
            var expected = c.GetProperty("match");
            if (expected.ValueKind == JsonValueKind.Null)
            {
                result.ShouldBeNull(query);
            }
            else
            {
                result.ShouldNotBeNull(query);
                result.Choice.ShouldBe(expected.GetString());
                result.Score.ShouldBe(c.GetProperty("score").GetDouble(), Tolerance);
            }
        }
    }

    [Fact]
    [Trait("AC", "011-141")]
    public void WRatio_IsCaseSensitive()
    {
        // O que o desvio 099 propõe corrigir: a descrição guardada com maiúscula perde pontos.
        FuzzyScorer.WRatio("luz", "luz").ShouldBe(100);
        FuzzyScorer.WRatio("luz", "Luz").ShouldBeLessThan(70);
    }

    [Fact]
    public void WRatio_EmptyIsZero()
    {
        FuzzyScorer.WRatio(string.Empty, "abc").ShouldBe(0);
        FuzzyScorer.WRatio("abc", string.Empty).ShouldBe(0);
    }
}

public class BillMatchingTests
{
    [Fact]
    [Trait("AC", "011-142")]
    public void FindBest_MatchesFindMatchingBillForEveryFixture()
    {
        var section = AutomationFixture.Section("bill_matching");
        var bills = section.GetProperty("bills").EnumerateArray()
            .Select((b, i) => new FixedBill(
                b.GetProperty("id").GetInt32(), b.GetProperty("descricao").GetString()!,
                decimal.Parse(b.GetProperty("valor").GetString()!, CultureInfo.InvariantCulture), 1, "FIXO", 2, "cat"))
            .ToList();

        foreach (var c in section.GetProperty("cases").EnumerateArray())
        {
            var input = c.GetProperty("input").GetString()!;
            var match = BillMatching.FindBest(BillMatching.CleanDescription(input), bills);
            var expectedId = c.GetProperty("result_id");
            if (expectedId.ValueKind == JsonValueKind.Null)
            {
                match.ShouldBeNull(input);
            }
            else
            {
                match.ShouldNotBeNull(input);
                match.ScheduleId.ShouldBe(expectedId.GetInt32(), input);
                match.Description.ShouldBe(c.GetProperty("result_desc").GetString(), input);
            }
        }
    }

    [Theory]
    [Trait("AC", "011-142")]
    [InlineData("  Conta de Luz ", "luz")]
    [InlineData("a luz", "luz")]
    [InlineData("o aluguel", "aluguel")]
    [InlineData("conta de conta de luz", "de luz")]
    [InlineData("de a luz", "a luz")]
    [InlineData("a o luz", "luz")]
    [InlineData("conta  ", "conta")]
    [InlineData("abacate", "abacate")]
    public void CleanDescription_RemovesLeadingArticlesInSequence(string input, string expected) =>
        BillMatching.CleanDescription(input).ShouldBe(expected);

    [Fact]
    [Trait("AC", "011-142")]
    public void FindBest_NoBillsMeansNoMatch() => BillMatching.FindBest("luz", []).ShouldBeNull();

    [Fact]
    [Trait("AC", "011-142")]
    public void FindBest_RepeatedDescriptionsCollapseOnTheLastRow()
    {
        var bills = new[]
        {
            new FixedBill(1, "luz", 10m, 1, "FIXO", 2, "a"),
            new FixedBill(7, "luz", 20m, 1, "FIXO", 2, "b"),
        };
        BillMatching.FindBest("luz", bills)!.ScheduleId.ShouldBe(7);
    }
}

public class BudgetValidationTests
{
    [Fact]
    [Trait("AC", "011-143")]
    public void Periods_MatchCalcularPeriodoPoteForEveryFixture()
    {
        var cases = AutomationFixture.Cases("periods");
        cases.Count.ShouldBeGreaterThan(40);
        foreach (var c in cases)
        {
            var (start, end) = BudgetPeriods.For(c.GetProperty("periodicity").GetString()!, DateOnly.Parse(c.GetProperty("date").GetString()!, CultureInfo.InvariantCulture));
            start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("start").GetString(), c.ToString());
            end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture).ShouldBe(c.GetProperty("end").GetString(), c.ToString());
        }
    }

    [Fact]
    [Trait("AC", "011-143")]
    public void Evaluate_MatchesValidateBudgetForEveryScenario()
    {
        foreach (var c in AutomationFixture.Cases("budget"))
        {
            var name = c.GetProperty("name").GetString()!;
            var pots = c.GetProperty("pots").EnumerateArray().ToList();
            var spends = c.GetProperty("spends").EnumerateArray().ToList();
            var input = pots.Select((p, i) =>
            {
                var spendText = spends[i].ValueKind == JsonValueKind.Null ? "0" : spends[i].GetString()!;
                var spent = Math.Abs((double)decimal.Parse(spendText, CultureInfo.InvariantCulture));
                var pot = new PotRow(
                    p.GetProperty("id").GetInt32(), p.GetProperty("name").GetString()!,
                    double.Parse(p.GetProperty("limit").GetString()!, CultureInfo.InvariantCulture), p.GetProperty("periodicity").GetString()!);
                return (pot, spent);
            }).ToList();

            var result = BudgetValidation.Evaluate(input, c.GetProperty("amount").GetDouble());

            result.RequiresConfirmation.ShouldBe(c.GetProperty("requires_confirmation").GetBoolean(), name);
            result.Message.ShouldBe(c.GetProperty("message").GetString(), name);
            var expected = c.GetProperty("validations").EnumerateArray().ToList();
            result.Checks.Count.ShouldBe(expected.Count, name);
            for (var i = 0; i < expected.Count; i++)
            {
                result.Checks[i].SpentAfter.ShouldBe(expected[i].GetProperty("after").GetDouble(), name);
                result.Checks[i].Percent.ShouldBe(expected[i].GetProperty("percent").GetDouble(), name);
                result.Checks[i].Exceeds.ShouldBe(expected[i].GetProperty("exceeds").GetBoolean(), name);
            }
        }
    }

    [Fact]
    public void Evaluate_WithoutPotsAllowsAndStaysQuiet()
    {
        var result = BudgetValidation.Evaluate([], 50);
        result.RequiresConfirmation.ShouldBeFalse();
        result.Message.ShouldBeEmpty();
    }
}
