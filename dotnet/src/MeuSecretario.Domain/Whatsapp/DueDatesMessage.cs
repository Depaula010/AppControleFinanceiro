using System.Globalization;
using System.Text;
using MeuSecretario.Domain.Automation;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>Conta fixa que vence no período (<c>get_vencimentos_periodo</c>); valor nulo vale zero.</summary>
public sealed record DueBill(string Description, decimal? Amount, int Day, string GroupName);

/// <summary>Fatura aberta que vence no período, com o valor das despesas lançadas nela.</summary>
public sealed record DueInvoice(string Card, DateOnly DueDate, decimal Value);

/// <summary>
/// Um trecho da busca de contas fixas por dia do mês. O Flask faz uma consulta quando o período cabe num mês e duas quando
/// cruza a virada (<c>finance_service.py:1469-1580</c>): <c>DayFrom</c> sem <c>DayTo</c> é o fim do primeiro mês, <c>DayTo</c> sem
/// <c>DayFrom</c> é o começo do seguinte. <c>Month</c> e <c>Year</c> são os da checagem de "já pago" e da periodicidade anual.
/// </summary>
public sealed record DueSlice(int? DayFrom, int? DayTo, int Month, int Year);

/// <summary>Plano de consultas e texto de <c>Vencimentos Hoje</c>, <c>Vencimentos Amanhã</c> e <c>Vencimentos Essa Semana</c>.</summary>
public static class DueDatesMessage
{
    public const string TodayPeriod = "HOJE";

    public const string TomorrowPeriod = "AMANHÃ";

    public const string WeekPeriod = "NOS PRÓXIMOS 7 DIAS";

    private const string IncomeGroup = "Renda";

    /// <summary>
    /// Os trechos de consulta do período. Cruzando a virada, o primeiro trecho usa mês e ano do início e o segundo os do fim
    /// (o Flask ignora os dias intermediários: do dia inicial ao fim do primeiro mês e do dia 1 ao dia final do seguinte).
    /// </summary>
    public static IReadOnlyList<DueSlice> Plan(DateOnly start, DateOnly end)
    {
        if (start.Month != end.Month)
        {
            return
            [
                new DueSlice(start.Day, null, start.Month, start.Year),
                new DueSlice(null, end.Day, end.Month, end.Year),
            ];
        }

        return [new DueSlice(start.Day, end.Day, start.Month, start.Year)];
    }

    public static string Nothing(string period)
    {
        ArgumentNullException.ThrowIfNull(period);
        return $"✅ Nenhuma conta vence {period.ToLowerInvariant()}!";
    }

    public static string Format(IReadOnlyList<DueBill> bills, IReadOnlyList<DueInvoice> invoices, string period, DateOnly reference)
    {
        ArgumentNullException.ThrowIfNull(bills);
        ArgumentNullException.ThrowIfNull(invoices);
        if (bills.Count == 0 && invoices.Count == 0)
        {
            return Nothing(period);
        }

        var incomes = bills.Where(b => b.GroupName == IncomeGroup).ToList();
        var expenses = bills.Where(b => b.GroupName != IncomeGroup).ToList();

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"📋 *CONTAS QUE VENCEM {period}* ({reference:dd/MM})\n\n");

        if (incomes.Count > 0)
        {
            sb.Append("*💵 Receitas Previstas:*\n");
            AppendBills(sb, incomes);
            sb.Append('\n');
        }

        if (expenses.Count > 0)
        {
            sb.Append("*💰 Despesas Fixas:*\n");
            AppendBills(sb, expenses);
            sb.Append('\n');
        }

        if (invoices.Count > 0)
        {
            sb.Append("*💳 Faturas:*\n");
            foreach (var invoice in invoices)
            {
                sb.Append("• ").Append(invoice.Card).Append(" - ").Append(Brl(invoice.Value))
                    .Append(" (vence ").Append(invoice.DueDate.ToString("dd/MM", CultureInfo.InvariantCulture)).Append(")\n");
            }

            sb.Append('\n');
        }

        var totalIncomes = incomes.Sum(b => b.Amount ?? 0m);
        var totalExpenses = expenses.Sum(b => b.Amount ?? 0m);
        var totalInvoices = invoices.Sum(i => i.Value);

        if (incomes.Count > 0)
        {
            sb.Append("*Receitas:* ").Append(Brl(totalIncomes)).Append('\n');
        }

        if (expenses.Count > 0)
        {
            sb.Append("*Despesas:* ").Append(Brl(totalExpenses)).Append('\n');
        }

        if (invoices.Count > 0)
        {
            sb.Append("*Faturas:* ").Append(Brl(totalInvoices)).Append('\n');
        }

        return sb.Append("*Saldo Previsto:* ").Append(Brl(totalIncomes - totalExpenses - totalInvoices)).ToString();
    }

    private static void AppendBills(StringBuilder sb, List<DueBill> bills)
    {
        foreach (var bill in bills)
        {
            sb.Append("• ").Append(bill.Description).Append(" - ").Append(Brl(bill.Amount ?? 0m))
                .Append(CultureInfo.InvariantCulture, $" (dia {bill.Day})\n");
        }
    }

    /// <summary><c>formatar_moeda(Decimal)</c>: o valor do banco tem duas casas, então passar por <see cref="double"/> não muda o texto.</summary>
    private static string Brl(decimal value) => MoneyText.Brl((double)value);
}

/// <summary><c>Contas Atrasadas</c> (<c>notification_intents.py:233-306</c>): o snapshot e o texto do check-in noturno em modo leitura.</summary>
public static class CheckinSnapshotMessage
{
    public const string AllPaid = "✅ Você não tem contas atrasadas! Tudo em dia! 🎉";

    /// <summary>
    /// Snapshot vazio, ou formatador sem conteúdo, vira a frase positiva. Com fatura vencida o formatador lança (desvio 130,
    /// proposto): a exceção sobe e a intenção responde o texto genérico, como o <c>KeyError</c> do Flask.
    /// </summary>
    public static string Format(CheckinSnapshot snapshot, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.IsEmpty)
        {
            return AllPaid;
        }

        return CheckinMessageFormatter.Format(snapshot, today, null) is { Length: > 0 } message ? message : AllPaid;
    }
}
