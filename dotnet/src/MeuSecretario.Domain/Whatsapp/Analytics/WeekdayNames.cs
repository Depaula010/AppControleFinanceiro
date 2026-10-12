namespace MeuSecretario.Domain.Whatsapp.Analytics;

/// <summary>
/// Dia da semana em português para o prompt da análise (desvio 233). O <c>TO_CHAR(..., 'Day')</c> do Postgres devolve sempre inglês;
/// a tradução fica no código, não no banco.
/// </summary>
public static class WeekdayNames
{
    private static readonly Dictionary<string, string> Portuguese = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sunday"] = "Domingo",
        ["Monday"] = "Segunda-feira",
        ["Tuesday"] = "Terça-feira",
        ["Wednesday"] = "Quarta-feira",
        ["Thursday"] = "Quinta-feira",
        ["Friday"] = "Sexta-feira",
        ["Saturday"] = "Sábado",
    };

    /// <summary>Nome em português; um valor desconhecido volta sem alteração.</summary>
    public static string ToPortuguese(string englishName)
    {
        var key = englishName.Trim();
        return Portuguese.TryGetValue(key, out var name) ? name : englishName;
    }
}
