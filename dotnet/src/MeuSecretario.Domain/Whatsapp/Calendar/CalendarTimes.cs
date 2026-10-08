using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Whatsapp;

/// <summary>
/// Leitura de data e hora ISO dos eventos do Google no fuso de São Paulo (<c>datetime.fromisoformat(...)</c> seguido de
/// <c>.astimezone(America/Sao_Paulo)</c> nos serviços de agenda do Flask). Sem fuso no texto vale São Paulo.
/// </summary>
public static partial class CalendarTimes
{
    /// <summary>Hora de parede de São Paulo (Kind = Unspecified); <c>null</c> se o texto não é um <c>dateTime</c> ISO.</summary>
    public static DateTime? ToLocal(string? text, TimeZoneInfo zone)
    {
        if (!TryParse(text, out var offsetValue, out var wall))
        {
            return null;
        }

        return offsetValue is { } instant ? TimeZoneInfo.ConvertTime(instant, zone).DateTime : wall;
    }

    /// <summary>Hora de parede como está escrita no texto, sem converter o fuso (<c>datetime.fromisoformat(x).strftime(...)</c>).</summary>
    public static DateTime? Wall(string? text)
    {
        if (!TryParse(text, out var offsetValue, out var wall))
        {
            return null;
        }

        return offsetValue?.DateTime ?? wall;
    }

    private static bool TryParse(string? text, out DateTimeOffset? withOffset, out DateTime wall)
    {
        withOffset = null;
        wall = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var tee = text.IndexOf('T', StringComparison.Ordinal);
        if (tee < 0)
        {
            return false;
        }

        var normalized = text.Replace("Z", "+00:00", StringComparison.Ordinal);
        if (OffsetSuffix().IsMatch(normalized[(tee + 1)..]))
        {
            if (!DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return false;
            }

            withOffset = parsed;
            return true;
        }

        if (!DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return true;
    }

    [GeneratedRegex(@"[+-]\d{2}(?::?\d{2})?$")]
    private static partial Regex OffsetSuffix();
}

/// <summary><c>datetime.strptime(texto, '%H:%M')</c> e <c>'%H:%M:%S'</c> do Python (hora de 1 ou 2 dígitos, texto inteiro).</summary>
public static partial class PyTimeText
{
    /// <summary><c>%H:%M</c>: o texto inteiro precisa casar; espaços nas pontas e sobras são erro (<c>ValueError</c>).</summary>
    public static bool TryParseHourMinute(string? text, out TimeOnly time)
    {
        time = default;
        if (text is null)
        {
            return false;
        }

        var match = HourMinute().Match(text);
        if (!match.Success)
        {
            return false;
        }

        time = new TimeOnly(Number(match.Groups[1].Value), Number(match.Groups[2].Value));
        return true;
    }

    /// <summary><c>%H:%M:%S</c>; segundos 60 e 61 casam no padrão do Python mas o <c>datetime</c> os recusa.</summary>
    public static bool TryParseHourMinuteSecond(string? text, out TimeOnly time)
    {
        time = default;
        if (text is null)
        {
            return false;
        }

        var match = HourMinuteSecond().Match(text);
        if (!match.Success)
        {
            return false;
        }

        var second = Number(match.Groups[3].Value);
        if (second > 59)
        {
            return false;
        }

        time = new TimeOnly(Number(match.Groups[1].Value), Number(match.Groups[2].Value), second);
        return true;
    }

    private static int Number(string digits)
    {
        var value = 0;
        foreach (var ch in digits)
        {
            value = (value * 10) + (int)char.GetNumericValue(ch);
        }

        return value;
    }

    [GeneratedRegex(@"^(2[0-3]|[0-1]\d|\d):([0-5]\d|\d)\z")]
    private static partial Regex HourMinute();

    [GeneratedRegex(@"^(2[0-3]|[0-1]\d|\d):([0-5]\d|\d):(6[0-1]|[0-5]\d|\d)\z")]
    private static partial Regex HourMinuteSecond();
}
