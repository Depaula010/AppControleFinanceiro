using System.Globalization;
using System.Text.RegularExpressions;

namespace MeuSecretario.Domain.Bills;

/// <summary>Parcela lida do fim de uma descrição: <c>"KABUM (3/5)"</c> → base <c>KABUM</c>, parcela 3 de 5.</summary>
public sealed record InstallmentSuffix(string BaseDescription, int Number, int Total);

/// <summary>
/// Rótulo de parcela das contas parceladas (desvio 269). Listas mostram as <b>já pagas</b> (<see cref="Paid"/>);
/// avisos de uma conta que vence agora mostram a <b>parcela da vez</b> (<see cref="Next"/>), o mesmo nome que vai
/// para o extrato. Sem total, nada é acrescentado; descrição que já termina em <c>(n/N)</c> não ganha outro sufixo.
/// </summary>
public static partial class InstallmentLabel
{
    /// <summary>Maior total de parcelas aceito pelo leitor do sufixo (o front aceita até 360; compra parcelada real fica bem abaixo).</summary>
    public const int MaxSuffixTotal = 99;

    /// <summary><c>Robo aspirador (4/18)</c>: parcelas já pagas.</summary>
    public static string Paid(string description, int? executed, int? total) =>
        Append(description, executed ?? 0, total);

    /// <summary><c>Robo aspirador (5/18)</c>: a parcela que vence agora.</summary>
    public static string Next(string description, int? executed, int? total) =>
        Append(description, (executed ?? 0) + 1, total);

    /// <summary>Verdadeiro se a descrição já termina em <c>(n/N)</c>.</summary>
    public static bool HasSuffix(string description) =>
        description is not null && TrailingSuffix().IsMatch(description);

    /// <summary>
    /// Lê a parcela <c>(n/N)</c> do fim da descrição. Aceita um grupo entre parênteses sem barra depois dela, como o
    /// <c>(Cartão Inter)</c> que a baixa por SMS acrescenta. Só vale com <c>2 ≤ N ≤ 99</c> e <c>1 ≤ n ≤ N</c>.
    /// </summary>
    public static InstallmentSuffix? Parse(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var match = SuffixWithTail().Match(description);
        if (!match.Success
            || !int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || !int.TryParse(match.Groups["t"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total))
        {
            return null;
        }

        if (total < 2 || total > MaxSuffixTotal || number < 1 || number > total)
        {
            return null;
        }

        var baseDescription = match.Groups["base"].Value.Trim();
        return baseDescription.Length == 0 ? null : new InstallmentSuffix(baseDescription, number, total);
    }

    private static string Append(string description, int number, int? total)
    {
        ArgumentNullException.ThrowIfNull(description);
        if (total is not { } t || t <= 0 || HasSuffix(description))
        {
            return description;
        }

        return $"{description} ({number.ToString(CultureInfo.InvariantCulture)}/{t.ToString(CultureInfo.InvariantCulture)})";
    }

    [GeneratedRegex(@"\(\d+/\d+\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSuffix();

    [GeneratedRegex(@"^(?<base>.*?)\s*\((?<n>\d{1,3})/(?<t>\d{1,3})\)\s*(?:\([^()/]*\)\s*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex SuffixWithTail();
}
