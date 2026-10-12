namespace MeuSecretario.Domain.Transactions;

public readonly record struct InvoiceDates(DateOnly Closing, DateOnly Due);

/// <summary>
/// Ciclo da fatura de cartão (R4, RN-004-10). Uma só fórmula para compra, pagamento e <c>EnsureCurrent</c> (desvio 065):
/// <c>D ≤ fechamento do mês de D</c> fecha nesse mês; senão fecha no mês seguinte. O vencimento cai no mês do
/// fechamento se <c>V ≥ F</c> e no mês seguinte se <c>V &lt; F</c>. Dia inexistente no mês cai no último dia.
/// </summary>
public static class InvoiceCycle
{
    public static InvoiceDates ForPurchase(int closingDay, int dueDay, DateOnly date)
    {
        var closingThisMonth = MonthDays.Clamp(date.Year, date.Month, closingDay);
        var closing = date <= closingThisMonth
            ? closingThisMonth
            : MonthDays.Clamp(NextMonth(date).Year, NextMonth(date).Month, closingDay);

        var dueMonth = dueDay < closingDay ? NextMonth(closing) : closing;
        return new InvoiceDates(closing, MonthDays.Clamp(dueMonth.Year, dueMonth.Month, dueDay));
    }

    private static DateOnly NextMonth(DateOnly date) => new DateOnly(date.Year, date.Month, 1).AddMonths(1);
}
