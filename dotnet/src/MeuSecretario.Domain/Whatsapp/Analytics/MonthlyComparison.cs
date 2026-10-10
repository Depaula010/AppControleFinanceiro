using System.Globalization;
using MeuSecretario.Domain.Jobs;

namespace MeuSecretario.Domain.Whatsapp.Analytics;

/// <summary>Total de despesas de uma macro categoria num mês (<c>get_monthly_comparison</c>), na ordem <c>ORDER BY total DESC</c> do SQL.</summary>
public sealed record MacroMonthTotal(string Month, string Macro, double Total);

/// <summary>
/// <c>Comparação Mensal</c> (<c>analytics_service.get_monthly_comparison</c>): mês de hoje contra o anterior, as 5 maiores variações por
/// categoria. Desvio 232: despesas em valor absoluto, números em pt-BR (<c>formatar_moeda</c>) e desempate das variações iguais pelo
/// nome da categoria (o Flask iterava um <c>set</c>, ordem que muda a cada processo).
/// </summary>
public static class MonthlyComparison
{
    private sealed record Variation(string Category, double Reference, double Previous, double Percent);

    public static string MonthKey(DateOnly day) => string.Create(CultureInfo.InvariantCulture, $"{day.Year:D4}-{day.Month:D2}");

    public static string PreviousMonthKey(DateOnly day) => MonthKey(new DateOnly(day.Year, day.Month, 1).AddDays(-1));

    public static string NoData(string month) => $"❌ Não encontrei gastos para comparar em {month}.";

    private static string Name(string monthKey)
    {
        var year = int.Parse(monthKey[..4], CultureInfo.InvariantCulture);
        var month = int.Parse(monthKey[5..], CultureInfo.InvariantCulture);
        return $"{MonthlyReportRules.MonthNames[month]}/{year:D4}";
    }

    public static string Format(string referenceMonth, string previousMonth, IReadOnlyList<MacroMonthTotal> rows)
    {
        // Dois dicionários que preservam a ordem de inserção; chave repetida só troca o valor (não ocorre: o SQL agrupa).
        var reference = new List<(string Key, double Value)>();
        var previous = new List<(string Key, double Value)>();
        var appearance = new List<string>();
        foreach (var row in rows)
        {
            Upsert(row.Month == referenceMonth ? reference : previous, row.Macro, Math.Abs(row.Total));
            if (!appearance.Contains(row.Macro))
            {
                appearance.Add(row.Macro);
            }
        }

        var totalReference = SumInOrder(reference.Select(r => r.Value));
        var totalPrevious = SumInOrder(previous.Select(r => r.Value));

        var referenceName = Name(referenceMonth);
        var previousName = Name(previousMonth);

        var totalVariation = totalPrevious != 0 ? (totalReference - totalPrevious) / totalPrevious * 100 : 0.0;
        var totalEmoji = totalVariation > 0 ? "📈" : "📉";

        var text = new System.Text.StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"📊 *Comparação: {referenceName} vs {previousName}*\n\n");
        text.Append(CultureInfo.InvariantCulture, $"💰 *{referenceName}:* {PtBrMoney.Format(totalReference)}\n");
        text.Append(CultureInfo.InvariantCulture, $"💰 *{previousName}:* {PtBrMoney.Format(totalPrevious)}\n");
        text.Append(CultureInfo.InvariantCulture, $"{totalEmoji} *Variação:* {PyNumber.WithSignPtBr(totalVariation)}%\n\n");
        text.Append("*Maiores mudanças por categoria:*\n");

        var variations = new List<Variation>();
        foreach (var category in appearance)
        {
            var valueReference = Find(reference, category);
            var valuePrevious = Find(previous, category);
            double percent;
            if (valuePrevious != 0)
            {
                percent = (valueReference - valuePrevious) / valuePrevious * 100;
            }
            else if (valueReference != 0)
            {
                percent = 100;
            }
            else
            {
                percent = 0;
            }

            variations.Add(new Variation(category, valueReference, valuePrevious, percent));
        }

        foreach (var variation in variations.OrderByDescending(v => Math.Abs(v.Percent)).ThenBy(v => v.Category, StringComparer.Ordinal).Take(5))
        {
            var emoji = variation.Percent > 10 ? "🔴" : variation.Percent < -10 ? "🟢" : "⚪";
            text.Append(CultureInfo.InvariantCulture, $"{emoji} *{variation.Category}:* {PtBrMoney.Format(variation.Reference)} ({PyNumber.WithSignPtBr(variation.Percent)}%)\n");
        }

        return text.ToString();
    }

    private static void Upsert(List<(string Key, double Value)> list, string key, double value)
    {
        var index = list.FindIndex(e => e.Key == key);
        if (index >= 0)
        {
            list[index] = (key, value);
        }
        else
        {
            list.Add((key, value));
        }
    }

    private static double Find(List<(string Key, double Value)> list, string key)
    {
        var index = list.FindIndex(e => e.Key == key);
        return index >= 0 ? list[index].Value : 0;
    }

    private static double SumInOrder(IEnumerable<double> values)
    {
        var total = 0.0;
        foreach (var value in values)
        {
            total += value;
        }

        return total;
    }
}
