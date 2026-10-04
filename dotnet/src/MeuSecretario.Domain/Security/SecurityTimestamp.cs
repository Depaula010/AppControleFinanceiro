using System.Globalization;

namespace MeuSecretario.Domain.Security;

/// <summary>
/// <c>datetime.isoformat()</c> naive do Python (<c>2026-10-04T10:00:00.123456</c>; fração só se não zero) usado nos
/// valores <c>security:*</c> do Redis. Lê também o formato sem fração.
/// </summary>
public static class SecurityTimestamp
{
    private static readonly string[] Formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
    ];

    public static string Format(DateTime value)
    {
        var text = value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var microseconds = value.Ticks % TimeSpan.TicksPerSecond / 10;
        return microseconds == 0 ? text : text + "." + microseconds.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
