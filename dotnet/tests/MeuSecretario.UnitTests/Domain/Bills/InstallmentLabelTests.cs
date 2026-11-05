using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Bills;
using MeuSecretario.Domain.Transactions;
using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Bills;

/// <summary>Desvios 269 e 271: rótulo <c>(x/N)</c> das contas parceladas e leitura do sufixo nas descrições.</summary>
public class InstallmentLabelTests
{
    [Fact]
    [Trait("Desvio", "269")]
    public void Paid_ShowsTheInstallmentsAlreadyPaid() =>
        InstallmentLabel.Paid("Robo aspirador", 4, 18).ShouldBe("Robo aspirador (4/18)");

    [Fact]
    [Trait("Desvio", "269")]
    public void Next_ShowsTheInstallmentDueNow() =>
        InstallmentLabel.Next("Robo aspirador", 4, 18).ShouldBe("Robo aspirador (5/18)");

    [Fact]
    [Trait("Desvio", "269")]
    public void NoTotal_LeavesTheDescription()
    {
        InstallmentLabel.Paid("Luz", 0, null).ShouldBe("Luz");
        InstallmentLabel.Next("Luz", null, null).ShouldBe("Luz");
        InstallmentLabel.Next("Luz", 0, 0).ShouldBe("Luz");
    }

    [Fact]
    [Trait("Desvio", "269")]
    public void NullExecuted_CountsAsZero()
    {
        InstallmentLabel.Paid("Tênis", null, 3).ShouldBe("Tênis (0/3)");
        InstallmentLabel.Next("Tênis", null, 3).ShouldBe("Tênis (1/3)");
    }

    [Fact]
    [Trait("Desvio", "269")]
    public void ADescriptionWithSuffix_IsNotLabelledTwice() =>
        InstallmentLabel.Paid("Tênis (2/3)", 2, 3).ShouldBe("Tênis (2/3)");

    [Theory]
    [Trait("Desvio", "271")]
    [InlineData("ZP ROGGI 62899 (1/6)", "ZP ROGGI 62899", 1, 6)]
    [InlineData("KABUM (5/5)", "KABUM", 5, 5)]
    [InlineData("  MP ALIEXPRESS (2/3)  ", "MP ALIEXPRESS", 2, 3)]
    [InlineData("LOJA X (2/5) (Cartão Inter)", "LOJA X", 2, 5)]
    [InlineData("Curso (12/99)", "Curso", 12, 99)]
    public void Parse_ReadsTheSuffix(string description, string baseDescription, int number, int total) =>
        InstallmentLabel.Parse(description).ShouldBe(new InstallmentSuffix(baseDescription, number, total));

    [Theory]
    [Trait("Desvio", "271")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mercado")]
    [InlineData("Pós graduação (Parcela)")]
    [InlineData("(1/6)")]
    [InlineData("Loja (0/3)")]
    [InlineData("Loja (4/3)")]
    [InlineData("Loja (1/1)")]
    [InlineData("Loja (1/100)")]
    [InlineData("Loja (1/6) extra")]
    [InlineData("Loja (1/6) (2/6)x")]
    public void Parse_RejectsWhatIsNotAnInstallment(string? description) =>
        InstallmentLabel.Parse(description).ShouldBeNull();

    [Fact]
    [Trait("Desvio", "271")]
    public void Decide_WithoutSchedule_CreatesUnlessItIsTheLast()
    {
        InstallmentTracking.Decide(new InstallmentSuffix("X", 1, 6), null).ShouldBe(new InstallmentTrackingAction.Create(1));
        InstallmentTracking.Decide(new InstallmentSuffix("X", 6, 6), null).ShouldBeOfType<InstallmentTrackingAction.None>();
    }

    [Fact]
    [Trait("Desvio", "271")]
    public void Decide_WithSchedule_AdvancesOnlyForward()
    {
        var schedule = new TrackedInstallmentSchedule(70, 2);
        InstallmentTracking.Decide(new InstallmentSuffix("X", 3, 6), schedule).ShouldBe(new InstallmentTrackingAction.Advance(70, 3, false));
        InstallmentTracking.Decide(new InstallmentSuffix("X", 6, 6), schedule).ShouldBe(new InstallmentTrackingAction.Advance(70, 6, true));
        InstallmentTracking.Decide(new InstallmentSuffix("X", 2, 6), schedule).ShouldBeOfType<InstallmentTrackingAction.None>();
        InstallmentTracking.Decide(new InstallmentSuffix("X", 1, 6), schedule).ShouldBeOfType<InstallmentTrackingAction.None>();
    }

    [Fact]
    [Trait("Desvio", "271")]
    public void PlanFor_StartsOneMonthLater_OnTheSameDay()
    {
        var plan = InstallmentTracking.PlanFor(new DateOnly(2026, 1, 31), 99.9m, new InstallmentSuffix("X", 2, 5));

        plan.StartDate.ShouldBe(new DateOnly(2026, 2, 28));
        plan.DayOfMonth.ShouldBe(31);
        plan.InstallmentsExecuted.ShouldBe(2);
        plan.Total.ShouldBe(5);
        plan.Amount.ShouldBe(99.9m);
    }

    [Fact]
    [Trait("Desvio", "269")]
    public void Catalog_ShowsPaidInstallments_AndPending_ShowsTheNextOne()
    {
        var catalog = FixedBillsMessages.Catalog(
            [new CatalogBill("Robo aspirador", 221.98, 10, null, "MENSAL", "Eletrônicos", "Conta Corrente", "Despesa Discricionária", 18, 4)]);
        catalog.ShouldContain("  • Robo aspirador (4/18)\n    R$ 221,98 · dia 10\n");

        var bill = new FixedBill(70, "Robo aspirador", 221.98m, 10, "PARCELADO", 3, "Eletrônicos", 18, 4);
        bill.Label.ShouldBe("Robo aspirador (5/18)");
        new FixedBill(10, "Luz", 120m, 10, "FIXO", 2, "Energia").Label.ShouldBe("Luz");
    }
}
