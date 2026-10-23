using MeuSecretario.Domain.Whatsapp;

namespace MeuSecretario.UnitTests.Domain.Whatsapp;

/// <summary>Textos dos relatórios sob comando (desvio 256).</summary>
public class ReportMessagesTests
{
    [Fact]
    public void Invoice_ShowsTotal_Categories_TopPurchases_AndFutureInstallments()
    {
        var report = new CardInvoiceReport(
            "Cartão Inter", new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 13), "Paga", 1000m, -50m, 40,
            [new CategoryAmount("Supermercado / Mercearia", 600m, 25), new CategoryAmount("Outros (Lazer e Entretenimento)", 400m, 15)],
            [new ReportInvoiceLine("ZP ROGGI (1/6)", new DateOnly(2026, 8, 28), 511m)],
            2, 1022m);

        ReportMessages.Invoice(report).ShouldBe(
            "💳 *FATURA CARTÃO INTER - VENC. 20/09/2026* 💳\n"
            + "✅ Paga • fecha em 13/09\n\n"
            + "💰 *Total de compras:* R$ 1.000,00\n"
            + "↩️ Estornos: -R$ 50,00\n"
            + "🧾 40 lançamento(s)\n\n"
            + "📊 *POR CATEGORIA*\n"
            + "1. Supermercado / Mercearia - R$ 600,00 (60%)\n"
            + "2. Outros (Lazer e Entretenimento) - R$ 400,00 (40%)\n\n"
            + "🔝 *MAIORES COMPRAS*\n"
            + "• 28/08 ZP ROGGI (1/6) - R$ 511,00\n\n"
            + "🧮 *Parcelas a vencer:* 2 compra(s) parcelada(s); faltam R$ 1.022,00 nas próximas faturas");
    }

    [Fact]
    public void Invoice_WithoutRefundsOrInstallments_OmitsThoseLines_AndFoldsTheTailOfTheRanking()
    {
        var categories = Enumerable.Range(1, 10).Select(i => new CategoryAmount($"Cat {i}", 100m - i, 1)).ToList();
        var report = new CardInvoiceReport(
            "Cartão Inter", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 13), "Aberta", 955m, 0m, 10, categories, [], 0, 0m);

        var text = ReportMessages.Invoice(report);

        text.ShouldContain("🟢 Aberta");
        text.ShouldNotContain("Estornos");
        text.ShouldNotContain("Parcelas a vencer");
        text.ShouldNotContain("MAIORES COMPRAS");
        text.ShouldContain("8. Cat 8");
        text.ShouldNotContain("9. Cat 9");
        text.ShouldContain("… outras 2 categoria(s) - R$ 181,00 (19%)");
    }

    [Fact]
    public void MonthByCategory_ShowsTheVariationAgainstTheSameStretchOfTheLastMonth()
    {
        var report = new MonthByCategoryReport(
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 22), true, 1000m, 800m,
            [
                new CategoryDelta("Restaurantes, Bares e Delivery", 600m, 400m, 20),
                new CategoryDelta("Supermercado / Mercearia", 300m, 400m, 10),
                new CategoryDelta("Combustível / Recarga", 100m, 0m, 2),
            ]);

        ReportMessages.MonthByCategory(report).ShouldBe(
            "📊 *GASTOS POR CATEGORIA - OUTUBRO/2026* 📊\n"
            + "_até 22/10, contra o mesmo trecho do mês anterior_\n\n"
            + "💸 *Total:* R$ 1.000,00 (▲ 25% vs R$ 800,00)\n\n"
            + "1. Restaurantes, Bares e Delivery - R$ 600,00 (60%) ▲ 50%\n"
            + "2. Supermercado / Mercearia - R$ 300,00 (30%) ▼ 25%\n"
            + "3. Combustível / Recarga - R$ 100,00 (10%) 🆕");
    }

    [Fact]
    public void MonthByCategory_ClosedMonth_HasNoPartialNote_AndNoComparisonWithoutPreviousSpending()
    {
        var report = new MonthByCategoryReport(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), false, 100m, 0m, [new CategoryDelta("Streaming", 100m, 0m, 1)]);

        var text = ReportMessages.MonthByCategory(report);

        text.ShouldNotContain("até ");
        text.ShouldContain("💸 *Total:* R$ 100,00\n");
        text.ShouldNotContain(" vs ");
    }

    [Fact]
    public void MonthLabel_UsesTheAccentedMonthName() =>
        ReportMessages.MonthLabel(new DateOnly(2026, 3, 1)).ShouldBe("março/2026");
}
