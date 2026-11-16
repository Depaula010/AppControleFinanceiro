using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// O que o texto de "gastei/recebi" diz além de valor e descrição (desvio 281, antigo 154): a data ("ontem", "dia 10", "10/11"), o número
/// de parcelas ("em 10x") e o valor de cada parcela ("10x de 300"). A leitura é determinística, não do Gemini: o cache da extração é global
/// por mensagem (uma data relativa ficaria errada no dia seguinte) e a regra fica testável.
/// </summary>
/// <param name="Date">Data da compra ou do recebimento, ou <c>null</c> (vale hoje).</param>
/// <param name="Installments">Parcelas (2 a 99; a faixa válida, 2 a 48, é conferida na validação), ou <c>null</c>.</param>
/// <param name="InstallmentValue">Valor de cada parcela quando o texto diz "10x de 300".</param>
/// <param name="CleanText">A mensagem sem os trechos de data e parcelas (para achar a conta citada sem confundir "dia 10" com um nome).</param>
public sealed record TextHints(DateOnly? Date, int? Installments, decimal? InstallmentValue, string CleanText)
{
    public static TextHints None(string? message) => new(null, null, null, message ?? string.Empty);
}

public static partial class TransactionTextHints
{
    private static readonly string[] Months =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    public static TextHints Parse(string? message, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return TextHints.None(message);
        }

        var text = message;
        var date = ParseDate(ref text, today);
        var (installments, perInstallment) = ParseInstallments(ref text);
        return new TextHints(date, installments, perInstallment, Regex.Replace(text, @"\s{2,}", " ").Trim());
    }

    private static DateOnly? ParseDate(ref string text, DateOnly today)
    {
        // 10/11, 10/11/2026, 10/11/26
        var slash = Slash().Match(text);
        if (slash.Success)
        {
            var day = int.Parse(slash.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = int.Parse(slash.Groups[2].Value, CultureInfo.InvariantCulture);
            DateOnly? parsed;
            if (slash.Groups[3].Success)
            {
                var year = int.Parse(slash.Groups[3].Value, CultureInfo.InvariantCulture);
                parsed = year < 100 ? Build(2000 + year, month, day) : Build(year, month, day);
            }
            else
            {
                parsed = LatestUpTo(month, day, today);
            }

            if (parsed is not null)
            {
                text = text.Remove(slash.Index, slash.Length);
                return parsed;
            }
        }

        // 10 de novembro, dia 10 de novembro
        var named = NamedMonth().Match(text);
        if (named.Success)
        {
            var day = int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture);
            var month = Array.FindIndex(Months, m => string.Equals(Strip(m), Strip(named.Groups[2].Value), StringComparison.OrdinalIgnoreCase)) + 1;
            if (month > 0 && LatestUpTo(month, day, today) is { } parsed)
            {
                text = text.Remove(named.Index, named.Length);
                return parsed;
            }
        }

        var relative = Relative().Match(text);
        if (relative.Success)
        {
            var offset = Strip(relative.Value.ToLowerInvariant()) switch
            {
                "anteontem" => 2,
                "ontem" => 1,
                _ => 0,
            };
            text = text.Remove(relative.Index, relative.Length);
            return today.AddDays(-offset);
        }

        var dayOnly = DayOfMonth().Match(text);
        if (dayOnly.Success)
        {
            var day = int.Parse(dayOnly.Groups[1].Value, CultureInfo.InvariantCulture);
            if (day is >= 1 and <= 31)
            {
                text = text.Remove(dayOnly.Index, dayOnly.Length);
                return LatestDayUpTo(day, today);
            }
        }

        var weekday = Weekday().Match(text);
        if (weekday.Success)
        {
            var wanted = Strip(weekday.Groups["d"].Value) switch
            {
                "segunda" => DayOfWeek.Monday,
                "terca" => DayOfWeek.Tuesday,
                "quarta" => DayOfWeek.Wednesday,
                "quinta" => DayOfWeek.Thursday,
                "sexta" => DayOfWeek.Friday,
                "sabado" => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday,
            };
            var back = ((int)today.DayOfWeek - (int)wanted + 7) % 7;
            text = text.Remove(weekday.Index, weekday.Length);
            return today.AddDays(-(back == 0 ? 7 : back));
        }

        return null;
    }

    private static (int? Installments, decimal? PerInstallment) ParseInstallments(ref string text)
    {
        var each = EachInstallment().Match(text);
        if (each.Success && int.Parse(each.Groups[1].Value, CultureInfo.InvariantCulture) is var n and >= 2 && ParseAmount(each.Groups[2].Value) is { } per)
        {
            text = text.Remove(each.Index, each.Length);
            return (n, per);
        }

        foreach (var pattern in new[] { Times(), TimesPlain(), InstallmentsWord(), ParceladoEm() })
        {
            var match = pattern.Match(text);
            if (match.Success && int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) is var count and >= 2)
            {
                text = text.Remove(match.Index, match.Length);
                return (count, null);
            }
        }

        return (null, null);
    }

    /// <summary>"1.234,56", "300", "89,9", "1234.5" (ponto de milhar só quando há três dígitos depois dele).</summary>
    private static decimal? ParseAmount(string raw)
    {
        var s = raw.Trim().TrimEnd('.', ',');
        if (s.Contains(',', StringComparison.Ordinal))
        {
            s = s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        }
        else if (ThousandsOnly().IsMatch(s))
        {
            s = s.Replace(".", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0
            ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    private static DateOnly? Build(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;

    /// <summary>A ocorrência mais recente do dia/mês que não passa de hoje (sem ano, "10/12" em janeiro é de dezembro passado).</summary>
    private static DateOnly? LatestUpTo(int month, int day, DateOnly today) =>
        Build(today.Year, month, day) is { } thisYear && thisYear <= today
            ? thisYear
            : Build(today.Year - 1, month, day);

    /// <summary>"dia 10": este mês se já chegou, senão o mês anterior (dia que não existe lá cai no último dia).</summary>
    private static DateOnly LatestDayUpTo(int day, DateOnly today)
    {
        if (day <= today.Day)
        {
            return new DateOnly(today.Year, today.Month, day);
        }

        var previous = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
        return new DateOnly(previous.Year, previous.Month, Math.Min(day, DateTime.DaysInMonth(previous.Year, previous.Month)));
    }

    private static string Strip(string value) => value.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c)).ToString().ToLowerInvariant();

    [GeneratedRegex(@"(?<![\d/])(\d{1,2})/(\d{1,2})(?:/(\d{4}|\d{2}))?(?![\d/])", RegexOptions.CultureInvariant)]
    private static partial Regex Slash();

    [GeneratedRegex(@"\b(?:dia\s+)?(\d{1,2})\s+de\s+(janeiro|fevereiro|mar[çc]o|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedMonth();

    [GeneratedRegex(@"\b(anteontem|ontem|hoje)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Relative();

    [GeneratedRegex(@"\b(?:no\s+|n[oa]\s+)?dia\s+(\d{1,2})\b(?!\s*(?:/|de\s))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayOfMonth();

    [GeneratedRegex(@"(?:\b(?:na|no|nessa|nesse|d[aoe])\s+(?<d>segunda|ter[çc]a|quarta|quinta|sexta|s[áa]bado|domingo)(?:-feira)?(?!\s+(?:parcela|vez|feira))\b|\b(?<d>segunda|ter[çc]a|quarta|quinta|sexta|s[áa]bado|domingo)(?:-feira)?\s+passad[oa]\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Weekday();

    [GeneratedRegex(@"\b(\d{1,2})\s*(?:x|vezes)\s+de\s+(?:r\$\s*)?(\d[\d.,]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EachInstallment();

    [GeneratedRegex(@"\b(?:em|parcelad[oa]\s+em)\s+(\d{1,2})\s*(?:x|vezes|parcelas?)(?![\w])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Times();

    [GeneratedRegex(@"\b(\d{1,2})\s*x(?![\w\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TimesPlain();

    [GeneratedRegex(@"\b(\d{1,2})\s*parcelas\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallmentsWord();

    [GeneratedRegex(@"\bparcelad[oa]\s+em\s+(\d{1,2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ParceladoEm();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$", RegexOptions.CultureInvariant)]
    private static partial Regex ThousandsOnly();
}
