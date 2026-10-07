using System.Globalization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// <c>formatar_moeda</c> (<c>currency_formatter.py</c>): <c>R$ 1.234,56</c>; nulo vira <c>R$ 0,00</c>. Não depende da
/// cultura do container (o Python caía no fallback manual quando o locale não existia, com o mesmo resultado).
/// </summary>
public static class PtBrMoney
{
    public static string Format(decimal? value) => value is { } v ? Format(v) : "R$ 0,00";

    public static string Format(decimal value)
    {
        var rounded = Math.Round(Math.Abs(value), 2, MidpointRounding.ToEven);
        var text = rounded.ToString("N2", CultureInfo.InvariantCulture)
            .Replace(",", "X", StringComparison.Ordinal)
            .Replace(".", ",", StringComparison.Ordinal)
            .Replace("X", ".", StringComparison.Ordinal);
        return value < 0 ? "-R$ " + text : "R$ " + text;
    }

    /// <summary>Versão para o <c>float</c> do Python (soma e totais); o arredondamento é o do Python.</summary>
    public static string Format(double value)
    {
        var fixedText = PyNumber.Fixed(Math.Abs(value), 2);
        var parts = fixedText.Split('.');
        var integer = PyNumber.GroupThousands(parts[0], ".");
        var text = integer + "," + parts[1];
        return value < 0 ? "-R$ " + text : "R$ " + text;
    }
}
