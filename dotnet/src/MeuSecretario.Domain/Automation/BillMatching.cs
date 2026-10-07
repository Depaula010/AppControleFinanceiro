namespace MeuSecretario.Domain.Automation;

/// <summary>Conta fixa ativa candidata à quitação por SMS (<c>find_matching_bill</c>).</summary>
public sealed record FixedBill(
    int ScheduleId,
    string Description,
    decimal? ExpectedAmount,
    int DayOfMonth,
    string ScheduleType,
    int AccountId,
    string Category);

/// <summary>Casamento difuso da descrição do SMS com as contas fixas (<c>fixed_bills_service.py:80-145</c>, spec 011 §7.3).</summary>
public static class BillMatching
{
    public const double Threshold = 65;

    private static readonly string[] Articles = ["a ", "o ", "da ", "do ", "de ", "conta de ", "conta "];

    /// <summary><c>lower().strip()</c> e remoção, em sequência e uma vez cada, dos artigos iniciais.</summary>
    public static string CleanDescription(string paymentDescription)
    {
        var cleaned = paymentDescription.ToLowerInvariant().Trim();
        foreach (var article in Articles)
        {
            if (cleaned.StartsWith(article, StringComparison.Ordinal))
            {
                cleaned = cleaned[article.Length..].Trim();
            }
        }

        return cleaned;
    }

    /// <summary>
    /// <c>process.extractOne(limpa, {descricao: linha}.keys(), WRatio, 65)</c>. O <c>dict</c> do Python mantém a posição da
    /// primeira ocorrência e o valor da última: descrições repetidas colapsam na última linha.
    /// </summary>
    public static FixedBill? FindBest(string cleanedDescription, IReadOnlyList<FixedBill> bills)
    {
        if (bills.Count == 0)
        {
            return null;
        }

        var byDescription = new Dictionary<string, FixedBill>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var bill in bills)
        {
            if (!byDescription.ContainsKey(bill.Description))
            {
                order.Add(bill.Description);
            }

            byDescription[bill.Description] = bill;
        }

        var match = FuzzyScorer.ExtractOne(cleanedDescription, order, Threshold);
        return match is null ? null : byDescription[match.Choice];
    }
}
