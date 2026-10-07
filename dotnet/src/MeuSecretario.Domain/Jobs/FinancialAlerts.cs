namespace MeuSecretario.Domain.Jobs;

/// <summary>Conta agendada que vence hoje ou amanhã (<c>get_upcoming_bills_and_invoices</c>, spec 013 RN-013-30).</summary>
/// <param name="Type"><c>Receita</c> se o grupo é <c>Renda</c>, senão <c>Despesa</c>.</param>
public sealed record AlertBill(int Id, string Description, double Value, string Category, string Account, string Type)
{
    public const string Income = "Receita";

    public const string Expense = "Despesa";

    public static string TypeFor(string? groupName) => groupName == "Renda" ? Income : Expense;
}

/// <summary>Fatura de cartão que vence hoje ou amanhã (RN-013-31).</summary>
public sealed record AlertInvoice(string Card, double Value, DateOnly DueDate);

/// <summary>As quatro listas do Python (<c>contas_hoje</c>, <c>contas_amanha</c>, <c>faturas_hoje</c>, <c>faturas_amanha</c>).</summary>
public sealed record FinancialAlerts(
    IReadOnlyList<AlertBill> BillsToday,
    IReadOnlyList<AlertBill> BillsTomorrow,
    IReadOnlyList<AlertInvoice> InvoicesToday,
    IReadOnlyList<AlertInvoice> InvoicesTomorrow)
{
    public static FinancialAlerts Empty { get; } = new([], [], [], []);

    public bool Any => BillsToday.Count > 0 || BillsTomorrow.Count > 0 || InvoicesToday.Count > 0 || InvoicesTomorrow.Count > 0;
}

/// <summary><c>FinancialAlertFormatter.format</c> (<c>financial_alert_formatter.py</c>, RN-013-32): texto literal.</summary>
public static class FinancialAlertFormatter
{
    /// <summary><c>null</c> quando não há nada a avisar.</summary>
    public static string? Format(FinancialAlerts alerts, bool includeGreeting)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        if (!alerts.Any)
        {
            return null;
        }

        var parts = new List<string>();
        if (includeGreeting)
        {
            parts.Add("🌅 *Bom dia!*\n");
        }

        parts.Add("💰 *ALERTAS FINANCEIROS*");

        if (alerts.BillsToday.Count > 0 || alerts.InvoicesToday.Count > 0)
        {
            var expenses = alerts.BillsToday.Where(b => b.Type == AlertBill.Expense).ToList();
            var incomes = alerts.BillsToday.Where(b => b.Type == AlertBill.Income).ToList();

            if (expenses.Count > 0 || alerts.InvoicesToday.Count > 0)
            {
                parts.Add("\n⚠️ *VENCE HOJE (Despesas):*");
                AddBills(parts, expenses);
                AddInvoices(parts, alerts.InvoicesToday);
            }

            if (incomes.Count > 0)
            {
                parts.Add("\n💵 *VENCE HOJE (Receitas):*");
                AddBills(parts, incomes);
            }
        }

        if (alerts.BillsTomorrow.Count > 0 || alerts.InvoicesTomorrow.Count > 0)
        {
            var expenses = alerts.BillsTomorrow.Where(b => b.Type == AlertBill.Expense).ToList();
            var incomes = alerts.BillsTomorrow.Where(b => b.Type == AlertBill.Income).ToList();

            if (expenses.Count > 0 || alerts.InvoicesTomorrow.Count > 0)
            {
                parts.Add("\n🔔 *VENCE AMANHÃ (Despesas):*");
                AddBills(parts, expenses);
                AddInvoices(parts, alerts.InvoicesTomorrow);
            }

            if (incomes.Count > 0)
            {
                parts.Add("\n💰 *VENCE AMANHÃ (Receitas):*");
                AddBills(parts, incomes);
            }
        }

        return string.Join("\n", parts);
    }

    private static void AddBills(List<string> parts, IEnumerable<AlertBill> bills)
    {
        foreach (var bill in bills)
        {
            parts.Add($"• {bill.Description} - R$ {PyNumber.FixedComma(bill.Value)}");
        }
    }

    private static void AddInvoices(List<string> parts, IEnumerable<AlertInvoice> invoices)
    {
        foreach (var invoice in invoices)
        {
            parts.Add($"• Fatura {invoice.Card} - R$ {PyNumber.FixedComma(invoice.Value)}");
        }
    }
}
