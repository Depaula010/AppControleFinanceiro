using System.Numerics;
using MeuSecretario.Domain.Accounts;
using MeuSecretario.Domain.CalendarAlerts;
using MeuSecretario.Domain.Settings;

namespace MeuSecretario.UnitTests.Domain.CalendarAlerts;

public class CalendarAlertRulesTests
{
    [Theory]
    [Trait("AC", "014-26")]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    public void ValidateMinutes_IntegerInRange_IsValid(int minutes)
    {
        var check = CalendarAlertRules.ValidateMinutes(RawValue.Whole(minutes));

        check.ShouldBe(new MinutesCheck(FieldState.Valid, minutes));
    }

    [Theory]
    [Trait("AC", "014-26")]
    [InlineData(0)]
    [InlineData(61)]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void ValidateMinutes_IntegerOutOfRange_IsInvalid(int minutes) =>
        CalendarAlertRules.ValidateMinutes(RawValue.Whole(minutes)).State.ShouldBe(FieldState.Invalid);

    [Fact]
    [Trait("AC", "014-26")]
    public void ValidateMinutes_HugeInteger_IsInvalid() =>
        CalendarAlertRules.ValidateMinutes(RawValue.Whole(BigInteger.Pow(10, 30))).State.ShouldBe(FieldState.Invalid);

    [Fact]
    [Trait("AC", "014-26")]
    public void ValidateMinutes_NonIntegerTypes_AreInvalid()
    {
        CalendarAlertRules.ValidateMinutes(RawValue.Fractional(5.5)).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ValidateMinutes(RawValue.Fractional(5.0)).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ValidateMinutes(RawValue.Text("5")).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ValidateMinutes(RawValue.Bool(true)).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ValidateMinutes(new ListValue(0)).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ValidateMinutes(RawValue.Other).State.ShouldBe(FieldState.Invalid);
    }

    [Fact]
    [Trait("AC", "014-20")]
    public void ValidateMinutes_AbsentAndNull_AreNotInformed()
    {
        CalendarAlertRules.ValidateMinutes(RawValue.Absent).State.ShouldBe(FieldState.Absent);
        CalendarAlertRules.ValidateMinutes(RawValue.Null).State.ShouldBe(FieldState.Absent);
    }

    [Fact]
    [Trait("AC", "014-27")]
    public void ParseActive_OnlyJsonBooleansAreValid()
    {
        CalendarAlertRules.ParseActive(RawValue.Bool(true)).ShouldBe(new ActiveCheck(FieldState.Valid, true));
        CalendarAlertRules.ParseActive(RawValue.Bool(false)).ShouldBe(new ActiveCheck(FieldState.Valid, false));
        CalendarAlertRules.ParseActive(RawValue.Absent).State.ShouldBe(FieldState.Absent);
        CalendarAlertRules.ParseActive(RawValue.Null).State.ShouldBe(FieldState.Absent);
        CalendarAlertRules.ParseActive(RawValue.Text("true")).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ParseActive(RawValue.Whole(1)).State.ShouldBe(FieldState.Invalid);
        CalendarAlertRules.ParseActive(new ListValue(1)).State.ShouldBe(FieldState.Invalid);
    }

    [Fact]
    [Trait("AC", "014-28")]
    public void IsActivationRequested_FollowsPythonTruthiness()
    {
        CalendarAlertRules.IsActivationRequested(RawValue.Bool(true)).ShouldBeTrue();
        CalendarAlertRules.IsActivationRequested(RawValue.Whole(2)).ShouldBeTrue();
        CalendarAlertRules.IsActivationRequested(RawValue.Text("abc")).ShouldBeTrue();
        CalendarAlertRules.IsActivationRequested(new ListValue(1)).ShouldBeTrue();
        CalendarAlertRules.IsActivationRequested(RawValue.Bool(false)).ShouldBeFalse();
        CalendarAlertRules.IsActivationRequested(RawValue.Whole(0)).ShouldBeFalse();
        CalendarAlertRules.IsActivationRequested(RawValue.Text(string.Empty)).ShouldBeFalse();
        CalendarAlertRules.IsActivationRequested(RawValue.Null).ShouldBeFalse();
        CalendarAlertRules.IsActivationRequested(RawValue.Absent).ShouldBeFalse();
    }

    [Theory]
    [Trait("AC", "014-22")]
    [InlineData(true, null, "Alertas de tarefas ativados")]
    [InlineData(false, null, "Alertas de tarefas desativados")]
    [InlineData(null, 10, "alerta configurado para 10 minuto(s) antes")]
    [InlineData(true, 5, "Alertas de tarefas ativados e alerta configurado para 5 minuto(s) antes")]
    [InlineData(false, 1, "Alertas de tarefas desativados e alerta configurado para 1 minuto(s) antes")]
    [InlineData(null, null, "Configuração atualizada")]
    public void SuccessMessage_JoinsThePresentParts(bool? active, int? minutes, string expected) =>
        CalendarAlertRules.SuccessMessage(active, minutes).ShouldBe(expected);
}
