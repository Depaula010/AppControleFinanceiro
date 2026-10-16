using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Dia e mês citados na pergunta da agenda ("tenho compromisso dia 15 de novembro?", "agenda 15/11"), sem chamar o Gemini: o prompt
/// de período do Flask só devolve períodos fixos e não pode mudar sem quebrar a paridade. Datas com ano ("15/11/2027") ficam de
/// fora, porque o serviço só resolve a próxima ocorrência e responderia o ano errado.
/// </summary>
public static partial class AgendaDateParser
{
    private static readonly string[] Months =
    [
        "janeiro", "fevereiro", "marco", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro",
    ];

    /// <summary><c>true</c> quando a mensagem cita um dia e um mês; devolve só os números (a validade da data é do serviço).</summary>
    public static bool TryParse(string message, out int day, out int month)
    {
        ArgumentNullException.ThrowIfNull(message);
        day = 0;
        month = 0;
        var text = Normalize(message);

        var named = NamedMonth().Match(text);
        if (named.Success)
        {
            day = int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            month = Array.IndexOf(Months, named.Groups[2].Value) + 1;
            return true;
        }

        var numeric = Numeric().Match(text);
        if (numeric.Success)
        {
            day = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
            month = int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"\b(\d{1,2})\s+de\s+(janeiro|fevereiro|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\b(?!\s+de\s+\d)")]
    private static partial Regex NamedMonth();

    [GeneratedRegex(@"(?<![\d/])(\d{1,2})/(\d{1,2})(?![\d/])")]
    private static partial Regex Numeric();
}
