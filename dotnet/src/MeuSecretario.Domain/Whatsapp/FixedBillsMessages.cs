using System.Globalization;
using System.Text;
using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Conta fixa ainda não executada no mês (<c>get_pending_bills_for_month</c>, só as colunas que o texto usa).</summary>
public sealed record PendingBill(string Description, double Amount, string Category, int Day, string AccountType, string GroupName);

/// <summary>Agendamento ativo do catálogo (<c>get_all_scheduled_bills</c>); <c>Periodicity</c> nulo vale <c>MENSAL</c>.</summary>
public sealed record CatalogBill(string Description, double Amount, int Day, int? Month, string? Periodicity, string Category, string AccountType, string GroupName);

/// <summary>
/// Textos de <c>Consulta Contas Fixas</c> e <c>Consulta Todas Contas</c> (<c>FixedBillsService.list_pending_bills_formatted</c> e
/// <c>list_all_bills_formatted</c>, <c>fixed_bills_service.py</c>). Valores em <see cref="double"/>, como o <c>float</c> do Python.
/// </summary>
public static class FixedBillsMessages
{
    public const string NoPending = "✅ Você não tem contas fixas pendentes este mês! 🎉";

    public const string NoCatalog = "📋 Você não tem contas recorrentes cadastradas.";

    private const string CardAccountType = "Cartão de Crédito";

    private const string IncomeGroup = "Renda";

    private static readonly string[] Months =
        ["", "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho", "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro"];

    private static readonly string[] CatalogOrder = ["SEMANAL", "QUINZENAL", "MENSAL", "ANUAL"];

    /// <summary>Quanto cada periodicidade pesa no total mensal (o mesmo fator de <c>/api/bills/summary</c>); <c>ANUAL</c> fica de fora.</summary>
    private static double? MonthlyFactor(string period) => period switch
    {
        "MENSAL" => 1.0,
        "SEMANAL" => 4.33,
        "QUINZENAL" => 2.16,
        "DIARIA" => 30.0,
        _ => null,
    };

    private static string PeriodLabel(string period) => period switch
    {
        "SEMANAL" => "🔄 SEMANAL",
        "QUINZENAL" => "🔄 QUINZENAL",
        "MENSAL" => "📅 MENSAL",
        "ANUAL" => "📆 ANUAL",
        _ => period,
    };

    /// <summary>"Outubro/2026" (<c>formatar_mes_ano_pt</c>).</summary>
    public static string MonthYear(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{Months[date.Month]}/{date.Year:0000}");

    public static string Pending(IReadOnlyList<PendingBill> bills, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(bills);
        if (bills.Count == 0)
        {
            return NoPending;
        }

        var normal = new List<Item>();
        var card = new List<Item>();
        var incomes = new List<Item>();
        foreach (var bill in bills)
        {
            var (status, neutral) = Status(bill.Day, today);
            var item = new Item(bill.Description, bill.Amount, bill.Category, status, neutral);
            if (bill.GroupName == IncomeGroup)
            {
                incomes.Add(item);
            }
            else if (bill.AccountType == CardAccountType)
            {
                card.Add(item);
            }
            else
            {
                normal.Add(item);
            }
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"📋 *CONTAS FIXAS PENDENTES - {MonthYear(today).ToUpperInvariant()}* 📋\n\n");

        double totalNormal = 0;
        double totalCard = 0;
        double totalIncomes = 0;

        if (normal.Count > 0)
        {
            sb.Append("💸 *DESPESAS:*\n\n");
            var index = 1;
            foreach (var item in normal)
            {
                totalNormal += item.Amount;
                AppendPending(sb, index++, item, "💰", item.Status);
            }
        }

        if (card.Count > 0)
        {
            sb.Append("💳 *CARTÃO DE CRÉDITO (na fatura):*\n\n");
            var index = 1;
            foreach (var item in card)
            {
                totalCard += item.Amount;
                AppendPending(sb, index++, item, "💰", "ℹ️ " + item.NeutralStatus);
            }
        }

        if (incomes.Count > 0)
        {
            sb.Append("💰 *RECEITAS:*\n\n");
            var index = 1;
            foreach (var item in incomes)
            {
                totalIncomes += item.Amount;
                AppendPending(sb, index++, item, "💵", item.Status);
            }
        }

        sb.Append("━━━━━━━━━━━━━━━━━━━━\n");
        if (normal.Count > 0)
        {
            sb.Append("💸 Total Despesas: ").Append(MoneyText.Brl(totalNormal)).Append('\n');
        }

        if (card.Count > 0)
        {
            sb.Append("💳 Total Cartão: ").Append(MoneyText.Brl(totalCard)).Append('\n');
        }

        if (incomes.Count > 0)
        {
            sb.Append("💰 Total Receitas: ").Append(MoneyText.Brl(totalIncomes)).Append('\n');
        }

        var balance = totalIncomes - (totalNormal + totalCard);
        return sb.Append("📊 Saldo Líquido: ").Append(MoneyText.Brl(balance)).ToString();
    }

    public static string Catalog(IReadOnlyList<CatalogBill> bills)
    {
        ArgumentNullException.ThrowIfNull(bills);
        if (bills.Count == 0)
        {
            return NoCatalog;
        }

        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        foreach (var bill in bills)
        {
            var period = string.IsNullOrEmpty(bill.Periodicity) ? "MENSAL" : bill.Periodicity;
            var item = new CatalogItem(bill.Description, bill.Amount, Due(period, bill.Day, bill.Month));
            if (!groups.TryGetValue(period, out var group))
            {
                group = new Group();
                groups[period] = group;
            }

            if (bill.GroupName == IncomeGroup)
            {
                group.Incomes.Add(item);
            }
            else if (bill.AccountType == CardAccountType)
            {
                group.Card.Add(item);
            }
            else
            {
                group.Expenses.Add(item);
            }
        }

        var sb = new StringBuilder("📋 *TODAS AS SUAS CONTAS RECORRENTES* 📋\n\n");
        double monthlyTotal = 0;
        foreach (var period in CatalogOrder)
        {
            if (!groups.TryGetValue(period, out var group))
            {
                continue;
            }

            var factor = MonthlyFactor(period);
            sb.Append('*').Append(PeriodLabel(period)).Append("*\n");
            sb.Append("─────────────────────\n");

            if (group.Expenses.Count > 0)
            {
                sb.Append("💸 *Despesas:*\n");
                foreach (var item in group.Expenses)
                {
                    AppendCatalog(sb, item);
                    monthlyTotal += factor is { } f ? item.Amount * f : 0;
                }

                sb.Append('\n');
            }

            if (group.Card.Count > 0)
            {
                sb.Append("💳 *Cartão de Crédito:*\n");
                foreach (var item in group.Card)
                {
                    AppendCatalog(sb, item);
                    monthlyTotal += factor is { } f ? item.Amount * f : 0;
                }

                sb.Append('\n');
            }

            if (group.Incomes.Count > 0)
            {
                sb.Append("💰 *Receitas:*\n");
                foreach (var item in group.Incomes)
                {
                    AppendCatalog(sb, item);
                }

                sb.Append('\n');
            }
        }

        if (monthlyTotal != 0)
        {
            sb.Append("━━━━━━━━━━━━━━━━━━━━\n");
            sb.Append("💸 Total despesas mensais: ").Append(MoneyText.Brl(monthlyTotal)).Append('\n');
            sb.Append("_(semanais e quinzenais convertidas para média mensal; anuais ficam de fora)_");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Status do vencimento no mês de <paramref name="today"/> (<c>date(hoje.year, hoje.month, dia_execucao)</c>): dia fora do mês
    /// (0, 31 em mês curto...) é o <c>ValueError</c> do Python e cai no texto simples.
    /// </summary>
    private static (string Status, string Neutral) Status(int day, DateOnly today)
    {
        var dayText = day.ToString(CultureInfo.InvariantCulture);
        if (day < 1 || day > DateTime.DaysInMonth(today.Year, today.Month))
        {
            return ($"Vence dia {dayText}", $"Debitado dia {dayText}");
        }

        var remaining = new DateOnly(today.Year, today.Month, day).DayNumber - today.DayNumber;
        var remainingText = remaining.ToString(CultureInfo.InvariantCulture);
        if (remaining < 0)
        {
            return ("🔴 ATRASADO", $"Debitado dia {dayText}");
        }

        if (remaining == 0)
        {
            return ("⚠️ VENCE HOJE", "Debitado hoje");
        }

        return remaining <= 3
            ? ($"🟡 Vence em {remainingText} dias", $"Será debitado em {remainingText} dias")
            : ($"🟢 Vence dia {dayText}", $"Será debitado dia {dayText}");
    }

    private static string Due(string period, int day, int? month)
    {
        var dayText = day.ToString(CultureInfo.InvariantCulture);
        switch (period)
        {
            case "SEMANAL":
                return $"toda semana (dia {dayText})";
            case "QUINZENAL":
                return $"quinzenal (dia {dayText})";
            case "ANUAL":
                var name = month is { } m && m != 0
                    ? (m is >= 1 and <= 12 ? Months[m] : m.ToString(CultureInfo.InvariantCulture))
                    : "?";
                return $"{name}, dia {dayText}";
            default:
                return $"dia {dayText}";
        }
    }

    private static void AppendPending(StringBuilder sb, int index, Item item, string moneyIcon, string status) =>
        sb.Append(CultureInfo.InvariantCulture, $"{index}. {item.Description}\n")
            .Append("   ").Append(moneyIcon).Append(' ').Append(MoneyText.Brl(item.Amount)).Append('\n')
            .Append("   📊 ").Append(item.Category).Append('\n')
            .Append("   ").Append(status).Append("\n\n");

    private static void AppendCatalog(StringBuilder sb, CatalogItem item) =>
        sb.Append("  • ").Append(item.Description).Append('\n')
            .Append("    ").Append(MoneyText.Brl(item.Amount)).Append(" · ").Append(item.Due).Append('\n');

    private sealed record Item(string Description, double Amount, string Category, string Status, string NeutralStatus);

    private sealed record CatalogItem(string Description, double Amount, string Due);

    private sealed class Group
    {
        public List<CatalogItem> Expenses { get; } = [];

        public List<CatalogItem> Card { get; } = [];

        public List<CatalogItem> Incomes { get; } = [];
    }
}
