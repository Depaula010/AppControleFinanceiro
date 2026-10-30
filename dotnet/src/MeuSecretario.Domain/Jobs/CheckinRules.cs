using System.Globalization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// Linha das queries do check-in (<c>get_contas_pendentes_checkin_noturno</c> e <c>get_contas_atrasadas_checkin_noturno</c>):
/// as mesmas colunas do dicionário do Python.
/// </summary>
/// <param name="DueDate"><c>data_vencimento_real</c>.</param>
public sealed record CheckinBill(
    int Id,
    string Description,
    decimal? ExpectedAmount,
    int Day,
    int AccountId,
    int SubcategoryId,
    int UserId,
    string AccountName,
    string AccountType,
    string Category,
    string MacroName,
    string GroupName,
    string Kind,
    int? ExecutedInstallments,
    int? TotalInstallments,
    DateOnly? DueDate);

/// <summary>Fatura vencida do check-in (<c>get_faturas_vencidas</c>: o nome do cartão vem na coluna <c>cartao</c>).</summary>
/// <remarks><paramref name="AccountId"/> é a conta do cartão (desvio 264: a fatura vira item pagável do check-in).</remarks>
public sealed record OverdueInvoice(int Id, string Card, DateOnly DueDate, string Status, decimal Total, int AccountId = 0);

/// <summary>Fatura que vence hoje (<c>get_faturas_vencendo_hoje</c>: aqui a coluna se chama <c>nome_conta</c>).</summary>
public sealed record DueTodayInvoice(int Id, string AccountName, DateOnly DueDate, string Status, decimal Total, int AccountId = 0);

/// <summary>Conta fixa de cartão de crédito cobrada hoje na fatura (desvio 267): só informativa, nunca confirmável.</summary>
public sealed record CardBillNotice(string Description, decimal Value, string Account);

/// <summary><c>collect_financial_snapshot</c> (RN-013-42).</summary>
/// <param name="CardBillsToday">
/// Contas fixas do cartão cobradas hoje, MESMO as que o motor já lançou na fatura às 00:05 (desvio 267). <c>null</c> = não informado: o
/// formatador cai no comportamento antigo (as do cartão que ainda estão pendentes).
/// </param>
public sealed record CheckinSnapshot(
    IReadOnlyList<CheckinBill> PendingBills,
    IReadOnlyList<CheckinBill> OverdueBills,
    IReadOnlyList<OverdueInvoice> OverdueInvoices,
    IReadOnlyList<DueTodayInvoice> InvoicesDueToday,
    IReadOnlyList<CardBillNotice>? CardBillsToday = null)
{
    public bool IsEmpty => PendingBills.Count == 0 && OverdueBills.Count == 0 && OverdueInvoices.Count == 0 && InvoicesDueToday.Count == 0
        && (CardBillsToday is null || CardBillsToday.Count == 0);
}

/// <summary>Regras do check-in noturno (spec 013 §4.5). Funções puras.</summary>
public static class CheckinRules
{
    public const string IncomeGroup = "Renda";

    public const string ExpenseGroup = "Despesa";

    /// <summary>Despesa fixa de cartão: já é cobrada na fatura, não é confirmável (R8).</summary>
    public static bool IsCardExpense(CheckinBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        return bill.AccountType == ScheduleRules.CardAccountType && bill.GroupName == ExpenseGroup;
    }

    /// <summary>
    /// <c>calculate_days_overdue</c>: dias de atraso por <c>dia_execucao</c>, com a virada de mês
    /// (<c>nightly_checkin_service.py</c>).
    /// </summary>
    public static int DaysOverdue(int scheduleDay, DateOnly today)
    {
        if (scheduleDay <= today.Day)
        {
            return today.Day - scheduleDay;
        }

        var previous = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        var lastDay = DateTime.DaysInMonth(previous.Year, previous.Month);
        return lastDay - scheduleDay + today.Day;
    }

    /// <summary><c>categorize_by_delay</c>: <c>Status</c> nulo quando passa de 3 dias.</summary>
    public static (string? Status, int DaysLate) CategorizeByDelay(int scheduleDay, DateOnly today)
    {
        var days = DaysOverdue(scheduleDay, today);
        return days switch
        {
            0 => ("Vence hoje", days),
            1 => ("Venceu ontem", days),
            <= 3 => ($"Venceu dia {scheduleDay.ToString("00", CultureInfo.InvariantCulture)}", days),
            _ => (null, days),
        };
    }

    /// <summary>
    /// Ordem da sessão e da mensagem: receitas (grupo <c>Renda</c>) primeiro, depois as demais sem despesa de cartão
    /// (<c>create_checkin_session</c>).
    /// </summary>
    public static IReadOnlyList<CheckinBill> OrderForSession(IEnumerable<CheckinBill> bills)
    {
        ArgumentNullException.ThrowIfNull(bills);
        var all = bills.ToList();
        var incomes = all.Where(b => b.GroupName == IncomeGroup);
        var others = all.Where(b => b.GroupName != IncomeGroup && !IsCardExpense(b));
        return [.. incomes, .. others];
    }

    /// <summary>Retira de <paramref name="overdue"/> o que já está em <paramref name="pending"/>, pelo <c>id</c> (RN-013-42).</summary>
    public static IReadOnlyList<CheckinBill> Deduplicate(IReadOnlyList<CheckinBill> pending, IEnumerable<CheckinBill> overdue)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(overdue);
        var ids = pending.Select(b => b.Id).ToHashSet();
        return [.. overdue.Where(b => !ids.Contains(b.Id))];
    }

    /// <summary>Descrição com a parcela atual (<c>X (n/total)</c>) para <c>PARCELADO</c> com total.</summary>
    public static string DescriptionWithInstallment(CheckinBill bill)
    {
        ArgumentNullException.ThrowIfNull(bill);
        if (bill.Kind == ScheduleKinds.Installment && bill.TotalInstallments is { } total && total != 0)
        {
            var current = (bill.ExecutedInstallments ?? 0) + 1;
            return $"{bill.Description} ({current.ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)})";
        }

        return bill.Description;
    }
}
