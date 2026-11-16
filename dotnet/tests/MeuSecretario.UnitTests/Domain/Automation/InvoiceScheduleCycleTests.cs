using MeuSecretario.Domain.Automation;

namespace MeuSecretario.UnitTests.Domain.Automation;

/// <summary>Desvio 277: o populador lança a conta do cartão na data que pertence ao ciclo da fatura.</summary>
public class InvoiceScheduleCycleTests
{
    private static DateOnly D(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-20", 10, "2026-11-10")] // antes do fechamento (13): mesmo mês do vencimento
    [InlineData("2026-11-20", 13, "2026-11-13")] // no dia do fechamento ainda entra nesta fatura
    [InlineData("2026-11-20", 14, "2026-10-14")] // depois do fechamento: é do ciclo anterior ao mês do vencimento
    [InlineData("2026-11-20", 15, "2026-10-15")]
    [InlineData("2026-11-20", 31, "2026-10-31")]
    public void InterCard_ClosesOn13_DueOn20(string invoiceDue, int day, string expected) =>
        InvoiceScheduleRules.CycleTransactionDate(D(invoiceDue), day, 13, 20).ShouldBe(D(expected));

    [Theory]
    [InlineData("2027-01-05", 10, "2026-12-10")] // vence antes de fechar (fecha 25, vence 5): o ciclo é o mês anterior
    [InlineData("2027-01-05", 25, "2026-12-25")]
    public void CardThatDuesBeforeItCloses_UsesThePreviousMonth(string invoiceDue, int day, string expected) =>
        InvoiceScheduleRules.CycleTransactionDate(D(invoiceDue), day, 25, 5).ShouldBe(D(expected));

    [Fact]
    public void WithoutTheCardDays_KeepsTheOldBehaviour() =>
        InvoiceScheduleRules.CycleTransactionDate(D("2026-11-20"), 15, null, null).ShouldBe(D("2026-11-15"));

    [Fact]
    public void ShortMonth_ClampsTheDay() =>
        InvoiceScheduleRules.CycleTransactionDate(D("2026-03-20"), 31, 13, 20).ShouldBe(D("2026-02-28"));
}
