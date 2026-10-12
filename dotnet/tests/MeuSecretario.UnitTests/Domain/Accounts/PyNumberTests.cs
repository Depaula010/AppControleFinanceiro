using System.Numerics;
using MeuSecretario.Domain.Accounts;

namespace MeuSecretario.UnitTests.Domain.Accounts;

public class PyNumberTests
{
    public static TheoryData<RawValue, decimal> ValidMoney => new()
    {
        { RawValue.Text("12.5"), 12.5m },
        { RawValue.Text(" 7 "), 7m },
        { RawValue.Text("+.5"), 0.5m },
        { RawValue.Text("5."), 5m },
        { RawValue.Text("1e2"), 100m },
        { RawValue.Whole(12), 12m },
        { RawValue.Fractional(-3.5), -3.5m },
        { RawValue.Fractional(70.55), 70.55m },
        { RawValue.Fractional(10.005), 10.005m },
        { RawValue.Fractional(9999999999999.99), 9999999999999.99m },
        { RawValue.Bool(true), 1m },
        { RawValue.Bool(false), 0m },
    };

    public static TheoryData<RawValue> InvalidMoney =>
    [
        RawValue.Text("abc"),
        RawValue.Text("1,5"),
        RawValue.Text(""),
        RawValue.Text("inf"),
        RawValue.Text("Infinity"),
        RawValue.Text("nan"),
        RawValue.Text("1e30"),
        RawValue.Text("10000000000000"),       // 10^13: não cabe em numeric(15,2)
        RawValue.Text("9999999999999.999"),    // arredonda para 10^13
        RawValue.Fractional(1e30),
        RawValue.Fractional(double.PositiveInfinity),
        RawValue.Fractional(double.NaN),
        RawValue.Whole(BigInteger.Pow(10, 13)),
        RawValue.Whole(BigInteger.Pow(10, 40)),
        RawValue.Other,
        RawValue.Null,
        RawValue.Absent,
    ];

    [Theory]
    [MemberData(nameof(ValidMoney))]
    [Trait("AC", "002-17")]
    public void TryMoney_AcceptsWhatPythonFloatAcceptsWithinNumeric15x2(RawValue raw, decimal expected)
    {
        PyNumber.TryMoney(raw, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(InvalidMoney))]
    [Trait("AC", "002-28")]
    public void TryMoney_RejectsUnconvertibleNonFiniteAndOutOfRange(RawValue raw)
    {
        PyNumber.TryMoney(raw, out _).ShouldBeFalse();
    }

    public static TheoryData<RawValue, int> ValidInts => new()
    {
        { RawValue.Whole(10), 10 },
        { RawValue.Fractional(10.9), 10 },
        { RawValue.Fractional(-0.5), 0 },
        { RawValue.Text("10"), 10 },
        { RawValue.Text(" 10 "), 10 },
        { RawValue.Text("+7"), 7 },
        { RawValue.Bool(true), 1 },
        { RawValue.Bool(false), 0 },
    };

    public static TheoryData<RawValue> InvalidInts =>
    [
        RawValue.Text("10.5"),
        RawValue.Text("abc"),
        RawValue.Text(""),
        RawValue.Whole(BigInteger.Pow(2, 40)),
        RawValue.Fractional(double.NaN),
        RawValue.Fractional(1e30),
        RawValue.Other,
        RawValue.Null,
        RawValue.Absent,
    ];

    [Theory]
    [MemberData(nameof(ValidInts))]
    [Trait("AC", "002-18")]
    public void TryInt_AcceptsWhatPythonIntAccepts(RawValue raw, int expected)
    {
        PyNumber.TryInt(raw, out var value).ShouldBeTrue();
        value.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(InvalidInts))]
    [Trait("AC", "002-18")]
    public void TryInt_RejectsTheRest(RawValue raw)
    {
        PyNumber.TryInt(raw, out _).ShouldBeFalse();
    }
}

public class RawBodyTests
{
    [Fact]
    public void Get_ReturnsAbsentForMissingKeys_AndHasIsFalseForThem()
    {
        var body = new RawBody(new Dictionary<string, RawValue> { ["a"] = RawValue.Null });

        body.Get("b").ShouldBe(RawValue.Absent);
        body.Has("b").ShouldBeFalse();
        body.Has("a").ShouldBeTrue();
        body.Get("a").ShouldBe(RawValue.Null);
        body.IsEmpty.ShouldBeFalse();
        new RawBody(new Dictionary<string, RawValue>()).IsEmpty.ShouldBeTrue();
    }
}
