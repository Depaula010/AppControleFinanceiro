using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// <c>datetime.fromisoformat(texto.replace('Z', '+00:00'))</c> do Python 3.10: aceita <c>AAAA-MM-DD</c>, com hora opcional
/// e deslocamento <c>±HH:MM</c>. Texto sem deslocamento é "ingênuo" (<c>Aware = false</c>): o Python não subtrai ingênuo de
/// com fuso (lança <c>TypeError</c>); quem usa confere <see cref="IsoValue.Aware"/>.
/// </summary>
public static partial class PyIso
{
    /// <summary>Instante lido; <see cref="Aware"/> diz se o texto tinha deslocamento. A hora escrita fica em <see cref="IsoValue.Value"/>.</summary>
    public readonly record struct IsoValue(DateTimeOffset Value, bool Aware);

    [GeneratedRegex(
        @"^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2})(?::(\d{2})(?::(\d{2})(?:[.](\d{3}|\d{6}))?)?)?(?:(Z)|([+-])(\d{2}):(\d{2}))?)?\z",
        RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out IsoValue value)
    {
        value = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var match = Pattern().Match(text);
        if (!match.Success)
        {
            return false;
        }

        try
        {
            var year = Int(match, 1);
            var month = Int(match, 2);
            var day = Int(match, 3);
            var hour = match.Groups[4].Success ? Int(match, 4) : 0;
            var minute = match.Groups[5].Success ? Int(match, 5) : 0;
            var second = match.Groups[6].Success ? Int(match, 6) : 0;
            var micro = 0;
            if (match.Groups[7].Success)
            {
                var digits = match.Groups[7].Value;
                micro = int.Parse(digits.PadRight(6, '0'), CultureInfo.InvariantCulture);
            }

            var aware = match.Groups[8].Success || match.Groups[9].Success;
            var offset = TimeSpan.Zero;
            if (match.Groups[9].Success)
            {
                offset = new TimeSpan(Int(match, 10), Int(match, 11), 0);
                if (match.Groups[9].Value == "-")
                {
                    offset = -offset;
                }
            }

            var local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(micro * 10L);
            value = new IsoValue(new DateTimeOffset(local, offset), aware);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary><c>strftime('%H:%M')</c> da hora escrita no texto (sem converter de fuso).</summary>
    public static string Hhmm(DateTimeOffset value) => value.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static int Int(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
