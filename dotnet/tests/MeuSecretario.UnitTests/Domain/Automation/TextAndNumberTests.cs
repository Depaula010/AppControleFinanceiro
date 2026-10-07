using System.Globalization;
using System.Text.Json;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.UnitTests.Domain.Automation;

public class PyJsonValueTests
{
    [Fact]
    [Trait("AC", "011-032")]
    public void ToFloat_MatchesPythonFloatForEveryFixture()
    {
        var cases = AutomationFixture.Cases("float");
        cases.Count.ShouldBeGreaterThan(30);
        foreach (var c in cases)
        {
            var input = c.GetProperty("input");
            var result = PyJsonValue.ToFloat(input);
            if (c.GetProperty("ok").GetBoolean())
            {
                var ok = result.ShouldBeOfType<FloatConversion.Ok>(input.GetRawText());
                var expected = c.GetProperty("value").GetString()!;
                switch (expected)
                {
                    case "inf":
                        double.IsPositiveInfinity(ok.Value).ShouldBeTrue(input.GetRawText());
                        break;
                    case "-inf":
                        double.IsNegativeInfinity(ok.Value).ShouldBeTrue(input.GetRawText());
                        break;
                    case "nan":
                        double.IsNaN(ok.Value).ShouldBeTrue(input.GetRawText());
                        break;
                    default:
                        ok.Value.ShouldBe(double.Parse(expected, CultureInfo.InvariantCulture), input.GetRawText());
                        break;
                }
            }
            else
            {
                result.ShouldBeOfType<FloatConversion.Failed>(input.GetRawText()).Message.ShouldBe(c.GetProperty("error").GetString(), input.GetRawText());
            }
        }
    }

    [Theory]
    [Trait("AC", "011-032")]
    [InlineData("null", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("0.0", false)]
    [InlineData("-0.0", false)]
    [InlineData("1e-9", true)]
    [InlineData("5", true)]
    [InlineData("\"\"", false)]
    [InlineData("\"0\"", true)]
    [InlineData("[]", false)]
    [InlineData("[0]", true)]
    [InlineData("{}", false)]
    [InlineData("{\"a\":1}", true)]
    public void IsTruthy_FollowsPython(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);
        PyJsonValue.IsTruthy(document.RootElement).ShouldBe(expected);
    }

    [Fact]
    public void IsTruthy_AbsentIsFalse() => PyJsonValue.IsTruthy(null).ShouldBeFalse();

    [Theory]
    [InlineData("[1]", "list")]
    [InlineData("{}", "dict")]
    [InlineData("\"a\"", "str")]
    [InlineData("true", "bool")]
    [InlineData("1", "int")]
    [InlineData("1.5", "float")]
    [InlineData("1e3", "float")]
    [InlineData("null", "NoneType")]
    public void TypeName_IsThePythonName(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        PyJsonValue.TypeName(document.RootElement).ShouldBe(expected);
    }

    [Theory]
    [InlineData("abc", "'abc'")]
    [InlineData("it's", "\"it's\"")]
    [InlineData("a'b\"c", "'a\\'b\"c'")]
    [InlineData("tab\t", "'tab\\t'")]
    [InlineData("\u00a0", "'\\xa0'")]
    [InlineData("\u200b", "'\\u200b'")]
    [InlineData("ç", "'ç'")]
    [InlineData("\u0001", "'\\x01'")]
    public void Repr_FollowsPython(string text, string expected) => PyJsonValue.Repr(text).ShouldBe(expected);

    [Theory]
    [InlineData("1_000", 1000)]
    [InlineData("  +7 ", 7)]
    [InlineData(".5", 0.5)]
    [InlineData("5.", 5)]
    [InlineData("1e3", 1000)]
    [InlineData("١٢", 12)]
    public void TryParseFloat_AcceptsWhatPythonAccepts(string text, double expected)
    {
        PyJsonValue.TryParseFloat(text, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1__0")]
    [InlineData("_1")]
    [InlineData("1_")]
    [InlineData("1,5")]
    [InlineData("0x10")]
    [InlineData("e5")]
    [InlineData("1e")]
    [InlineData("--1")]
    public void TryParseFloat_RejectsWhatPythonRejects(string text) => PyJsonValue.TryParseFloat(text, out _).ShouldBeFalse();
}

public class InputSanitizerTests
{
    [Fact]
    [Trait("AC", "011-140")]
    public void Sanitize_MatchesPythonForEveryFixture()
    {
        var cases = AutomationFixture.Cases("sanitize");
        cases.Count.ShouldBeGreaterThan(15);
        foreach (var c in cases)
        {
            var text = c.GetProperty("text").GetString()!;
            InputSanitizer.Sanitize(text, c.GetProperty("max").GetInt32(), c.GetProperty("allow_special").GetBoolean())
                .ShouldBe(c.GetProperty("result").GetString(), $"'{text}'");
        }
    }

    [Fact]
    public void Sanitize_FalsyComesBackAsIs()
    {
        InputSanitizer.Sanitize(null, 10, false).ShouldBeNull();
        InputSanitizer.Sanitize(string.Empty, 10, false).ShouldBe(string.Empty);
    }
}

public class PaymentTypeTests
{
    [Theory]
    [Trait("AC", "011-033")]
    [InlineData("Crédito", "credito")]
    [InlineData(" PIX ", "pix")]
    [InlineData("débito", "debito")]
    [InlineData("Dinheiro", "dinheiro")]
    [InlineData("ｃｒｅｄｉｔｏ", "credito")]
    [InlineData("cartão", "cartao")]
    [InlineData("😀", "")]
    public void Normalize_StripsCaseAndAccents(string input, string expected) => PaymentType.Normalize(input).ShouldBe(expected);

    [Theory]
    [Trait("AC", "011-033")]
    [InlineData("credito", true)]
    [InlineData("debito", true)]
    [InlineData("pix", true)]
    [InlineData("dinheiro", true)]
    [InlineData("cartao", false)]
    [InlineData("boleto", false)]
    [InlineData("", false)]
    public void IsValid_OnlyTheFourTypes(string value, bool expected) => PaymentType.IsValid(value).ShouldBe(expected);

    [Fact]
    public void ValidList_IsTheFlaskText() => PaymentType.ValidList.ShouldBe("credito, debito, pix, dinheiro");
}

public class MoneyTextTests
{
    [Fact]
    [Trait("AC", "011-145")]
    public void Brl_MatchesFormatarMoedaForEveryFixture()
    {
        foreach (var c in AutomationFixture.Cases("money"))
        {
            var value = c.GetProperty("value");
            double? number = value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
            MoneyText.Brl(number).ShouldBe(c.GetProperty("text").GetString(), value.GetRawText());
        }
    }

    [Fact]
    [Trait("AC", "011-143")]
    public void Grouped2_MatchesPythonFormatForEveryFixture()
    {
        foreach (var c in AutomationFixture.Cases("grouped"))
        {
            MoneyText.Grouped2(c.GetProperty("value").GetDouble()).ShouldBe(c.GetProperty("text").GetString(), c.GetProperty("value").GetRawText());
        }
    }

    [Theory]
    [InlineData(-0.001, "-0.00")]
    [InlineData(-0.0, "-0.00")]
    [InlineData(1e30, "1,000,000,000,000,000,019,884,624,838,656.00")]
    public void Grouped2_EdgeCases(double value, string expected) => MoneyText.Grouped2(value).ShouldBe(expected);

    [Theory]
    [InlineData(96.0, "96.0")]
    [InlineData(98.5, "98.5")]
    [InlineData(100.0, "100.0")]
    [InlineData(0.0, "0.0")]
    [InlineData(1234567.0, "1234567.0")]
    public void PyFloatRepr_AlwaysHasADecimalPart(double value, string expected) => PyFloat.Repr(value).ShouldBe(expected);

    [Theory]
    [InlineData(2.675, 2, 2.67)]
    [InlineData(0.125, 2, 0.12)]
    [InlineData(66.25, 1, 66.2)]
    [InlineData(96.05, 1, 96.0)]
    public void PyFloatRound_IsHalfToEvenOnTheBinaryValue(double value, int digits, double expected) => PyFloat.Round(value, digits).ShouldBe(expected);

    [Theory]
    [InlineData(15.9, true)]
    [InlineData(1e13, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void MoneyDecimal_FitsNumeric15x2(double value, bool fits) => MoneyDecimal.TryFromDouble(value, out _).ShouldBe(fits);

    [Fact]
    public void MoneyDecimal_UsesTheShortestRepresentation()
    {
        MoneyDecimal.TryFromDouble(10.005, out var value).ShouldBeTrue();
        value.ShouldBe(10.005m);
    }
}

public class PythonJsonTests
{
    [Fact]
    [Trait("AC", "011-148")]
    public void DumpCategories_MatchesJsonDumps()
    {
        foreach (var c in AutomationFixture.Cases("categories_json"))
        {
            var categories = c.GetProperty("categories").EnumerateArray()
                .Select(e => new CategoryOption(e.GetProperty("id").GetInt32(), e.GetProperty("nome_sub").GetString()!, e.GetProperty("nome_macro").GetString()!))
                .ToList();
            PythonJson.DumpCategories(categories).ShouldBe(c.GetProperty("json").GetString());
        }
    }

    [Theory]
    [InlineData("a\u007fb", "\"a\\u007fb\"")]
    [InlineData("\b\f\n\r\t", "\"\\b\\f\\n\\r\\t\"")]
    [InlineData("\u0001", "\"\\u0001\"")]
    public void Quote_EscapesLikeEnsureAscii(string text, string expected) => PythonJson.Quote(text).ShouldBe(expected);
}
