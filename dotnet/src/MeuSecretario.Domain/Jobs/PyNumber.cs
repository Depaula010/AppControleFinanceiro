using System.Globalization;

namespace MeuSecretario.Domain.Jobs;

/// <summary>
/// Formatos numéricos do Python sobre <see cref="double"/> (<c>{x:.2f}</c>, <c>{x:,.2f}</c>, <c>{x:+.1f}</c>): meio-par sobre
/// o valor binário <b>exato</b> (a mesma ideia do <c>PyRound</c>). Os textos das mensagens usam estes formatos.
/// </summary>
public static class PyNumber
{
    private const double SafeLimit = 1e27;

    /// <summary><c>f"{value:.{digits}f}"</c> (ponto decimal, sem milhar).</summary>
    public static string Fixed(double value, int digits)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        if (Math.Abs(value) >= SafeLimit)
        {
            return value.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        var exact = decimal.Parse(value.ToString("F30", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        var rounded = decimal.Round(exact, digits, MidpointRounding.ToEven);
        var text = rounded.ToString("F" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        // Python escreve "-0.00" para -0.001 (sinal preservado); o decimal perde o sinal de zero.
        if (value < 0 && !text.StartsWith('-'))
        {
            text = "-" + text;
        }

        return text;
    }

    /// <summary><c>f"{value:.2f}".replace('.', ',')</c>: a forma usada nos alertas de vencimento.</summary>
    public static string FixedComma(double value) => Fixed(value, 2).Replace('.', ',');

    /// <summary><c>f"{value:,.2f}"</c>: milhar com vírgula e decimal com ponto (padrão inglês, só para a paridade das fixtures).</summary>
    public static string Thousands(double value, int digits = 2)
    {
        var text = Fixed(value, digits);
        var negative = text.StartsWith('-');
        if (negative)
        {
            text = text[1..];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : text[dot..];
        return (negative ? "-" : string.Empty) + GroupThousands(integer, ",") + fraction;
    }

    /// <summary>Milhar com ponto e decimal com vírgula (<c>1.234,56</c>, pt-BR; desvio 133), independente da cultura do container.</summary>
    public static string ThousandsPtBr(double value, int digits = 2)
    {
        var text = Fixed(value, digits);
        var negative = text.StartsWith('-');
        if (negative)
        {
            text = text[1..];
        }

        var dot = text.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? string.Empty : "," + text[(dot + 1)..];
        return (negative ? "-" : string.Empty) + GroupThousands(integer, ".") + fraction;
    }

    /// <summary>Número fixo com vírgula decimal (<c>12,5</c>; desvio 133).</summary>
    public static string FixedPtBr(double value, int digits) => Fixed(value, digits).Replace('.', ',');

    /// <summary>Como <see cref="WithSign"/> com vírgula decimal (<c>+12,5</c>; desvio 133).</summary>
    public static string WithSignPtBr(double value, int digits = 1) => WithSign(value, digits).Replace('.', ',');

    /// <summary><c>f"{value:+.1f}"</c>: sempre com sinal.</summary>
    public static string WithSign(double value, int digits = 1)
    {
        var text = Fixed(value, digits);
        return text.StartsWith('-') ? text : "+" + text;
    }

    public static string GroupThousands(string digits, string separator)
    {
        if (digits.Length <= 3)
        {
            return digits;
        }

        var builder = new System.Text.StringBuilder();
        var first = digits.Length % 3;
        if (first > 0)
        {
            builder.Append(digits, 0, first);
        }

        for (var i = first; i < digits.Length; i += 3)
        {
            if (builder.Length > 0)
            {
                builder.Append(separator);
            }

            builder.Append(digits, i, 3);
        }

        return builder.ToString();
    }
}
