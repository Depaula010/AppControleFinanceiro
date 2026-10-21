using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>Agendamentos de cadência contínua (desvio 255): próximo vencimento = último lançamento + intervalo.</summary>
public class ScheduleCadenceTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private static ScheduleInfo Schedule(string periodicity, DateOnly? nextDue, int day = 5, int notify = 0, string kind = "FIXO") => new(
        1, 7, 1, 10, "Gasolina", 100m, kind, periodicity, day, null, null, 0, notify, "Despesa", "Conta Corrente", "5531999990001", nextDue);

    [Theory]
    [InlineData("DIARIA", 1)]
    [InlineData("SEMANAL", 7)]
    [InlineData("QUINZENAL", 14)]
    public void IntervalDays_OfTheRollingPeriodicities(string periodicity, int days)
    {
        ScheduleCadence.IntervalDays(periodicity).ShouldBe(days);
        ScheduleCadence.IsRolling(periodicity).ShouldBeTrue();
    }

    [Theory]
    [InlineData("MENSAL")]
    [InlineData("ANUAL")]
    [InlineData("QUALQUER")]
    public void MonthlyAndAnnual_AreNotRolling(string periodicity)
    {
        ScheduleCadence.IsRolling(periodicity).ShouldBeFalse();
        ScheduleCadence.NextDue(periodicity, new DateOnly(2026, 10, 19), new DateOnly(2026, 1, 1)).ShouldBeNull();
    }

    [Fact]
    public void NextDue_IsTheLastPlusTheInterval_OrTheStartDateWhenNeverDone()
    {
        ScheduleCadence.NextDue("SEMANAL", new DateOnly(2026, 10, 19), new DateOnly(2025, 1, 1)).ShouldBe(new DateOnly(2026, 10, 26));
        ScheduleCadence.NextDue("QUINZENAL", new DateOnly(2026, 10, 20), new DateOnly(2026, 2, 20)).ShouldBe(new DateOnly(2026, 11, 3));
        ScheduleCadence.NextDue("SEMANAL", null, new DateOnly(2025, 1, 1)).ShouldBe(new DateOnly(2025, 1, 1));
        ScheduleCadence.NextDue("SEMANAL", null, null).ShouldBeNull();
    }

    [Fact]
    public void Motor_RollingSchedule_RunsOnlyWhenTheNextDueIsToday_NotOnTheDayOfTheMonth()
    {
        ScheduleRules.MotorDayMatches(Schedule("QUINZENAL", Today, day: 20), Today).ShouldBeTrue();
        ScheduleRules.MotorDayMatches(Schedule("QUINZENAL", Today.AddDays(1), day: 26), Today).ShouldBeFalse();
        ScheduleRules.MotorDayMatches(Schedule("QUINZENAL", null, day: 26), Today).ShouldBeFalse();
        ScheduleRules.MotorDayMatches(Schedule("MENSAL", null, day: 26), Today).ShouldBeTrue();
    }

    [Fact]
    public void Reminder_RollingSchedule_UsesTheNextDueMinusTheNotice()
    {
        var weekly = Schedule("SEMANAL", new DateOnly(2026, 10, 28), notify: 2, kind: "LEMBRETE_VARIAVEL");

        ScheduleRules.ReminderDueDate(weekly, Today).ShouldBe(new DateOnly(2026, 10, 28));
        ScheduleRules.ReminderDueDate(weekly, Today.AddDays(1)).ShouldBeNull();
        ScheduleRules.ReminderDueDate(weekly with { NextDue = null }, Today).ShouldBeNull();
        ScheduleRules.ReminderMessage(weekly).ShouldContain("(no dia 28/10)");
    }
}
