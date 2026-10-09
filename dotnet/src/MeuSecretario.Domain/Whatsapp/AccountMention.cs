using MeuSecretario.Domain.Automation;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// <c>finance_service.extract_mentioned_account</c> (<c>finance_service.py:1695-1759</c>, spec 012 §7.6): conta citada na mensagem
/// depois de uma palavra-chave ("com o", "no", "da"...), casada por <c>WRatio</c> (corte 60) com os nomes das contas ativas.
/// </summary>
public static class AccountMention
{
    public const double Cutoff = 60;

    private static readonly string[] Keywords =
    [
        "com o ", "com a ", "usando o ", "usando a ", "usando ",
        "pelo ", "pela ", "no ", "na ", "do ", "da ",
        "paguei com ", "gastei com ", "recebi com ", "entrou no ", "caiu no ",
    ];

    /// <summary>
    /// A primeira palavra-chave (na ordem da lista) que está no texto; o que vem depois dela (até 4 palavras) é o nome citado.
    /// <c>null</c> quando nenhuma aparece ou nada vem depois.
    /// </summary>
    public static string? ExtractName(string text)
    {
        var lower = text.ToLowerInvariant();
        foreach (var keyword in Keywords)
        {
            var index = lower.IndexOf(keyword, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var rest = lower[(index + keyword.Length)..].Trim();
            var words = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(4);
            var name = string.Join(' ', words);
            return name.Length == 0 ? null : name;
        }

        return null;
    }

    /// <summary>
    /// <c>process.extractOne(nome, nomes_contas.keys(), WRatio, 60)</c> sobre <c>{nome_conta: linha}</c>: nomes repetidos colapsam
    /// na última linha, mantendo a posição da primeira.
    /// </summary>
    public static AccountRef? Match(string text, IReadOnlyList<AccountRef> accounts)
    {
        var name = ExtractName(text);
        if (name is null || accounts.Count == 0)
        {
            return null;
        }

        var byName = new Dictionary<string, AccountRef>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var account in accounts)
        {
            if (!byName.ContainsKey(account.Name))
            {
                order.Add(account.Name);
            }

            byName[account.Name] = account;
        }

        var best = FuzzyScorer.ExtractOne(name, order, Cutoff);
        return best is null ? null : byName[best.Choice];
    }
}
