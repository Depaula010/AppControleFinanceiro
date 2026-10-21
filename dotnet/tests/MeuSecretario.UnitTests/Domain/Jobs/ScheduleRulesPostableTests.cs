using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.UnitTests.Domain.Jobs;

/// <summary>"Dá para lançar" (desvio 253): a definição única que o executor usa e o SQL do check-in espelha.</summary>
public class ScheduleRulesPostableTests
{
    [Theory]
    [InlineData("FIXO", true, 0, null, false, true)]
    [InlineData("FIXO", false, 0, null, false, false)]
    [InlineData("LEMBRETE_VARIAVEL", true, 0, null, false, false)]
    [InlineData("LEMBRETE_VARIAVEL", true, 0, null, true, true)]
    [InlineData("LEMBRETE_VARIAVEL", false, 0, null, true, false)]
    [InlineData("PARCELADO", true, 2, 3, false, true)]
    [InlineData("PARCELADO", true, 3, 3, false, false)]
    [InlineData("PARCELADO", true, 5, null, false, true)]
    [InlineData("PARCELADO", false, 0, 3, false, false)]
    public void IsPostable_FollowsKindActiveAndInstallments(
        string kind, bool active, int executed, int? total, bool allowVariableReminder, bool expected) =>
        ScheduleRules.IsPostable(kind, active, executed, total, allowVariableReminder).ShouldBe(expected);
}
