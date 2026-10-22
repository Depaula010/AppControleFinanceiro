using System.Globalization;
using System.Text;

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

/// <summary>Casamento difuso da descrição do SMS com as contas fixas (<c>fixed_bills_service.py</c>, spec 011 §7.3; desvio 099).</summary>
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

        // Desvio 099: o rapidfuzz 3.x é sensível à caixa e aos acentos; os dois lados são minúsculos e sem acento antes do WRatio.
        // Mesma regra do extractOne (a primeira com a maior nota que alcança o corte).
        var query = Fold(cleanedDescription);
        string? best = null;
        var bestScore = 0.0;
        foreach (var description in order)
        {
            var score = FuzzyScorer.WRatio(query, Fold(description));
            if (score >= Threshold && (best is null || score > bestScore))
            {
                best = description;
                bestScore = score;
                if (score >= 100)
                {
                    break;
                }
            }
        }

        return best is null ? null : byDescription[best];
    }

    /// <summary>Minúsculas e sem acentos (decomposição Unicode sem as marcas): <c>Água</c> vira <c>agua</c>.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
