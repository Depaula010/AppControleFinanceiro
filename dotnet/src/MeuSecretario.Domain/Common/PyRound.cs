using System.Globalization;

namespace MeuSecretario.Domain.Common;

/// <summary>
/// <c>round(x, 2)</c> do Python: meio-par sobre o valor binário <b>exato</b> do <see cref="double"/>.
/// <c>Math.Round(double, 2)</c> não serve: escala por 100 e erra em casos como <c>1.115</c> (que é 1.11499...).
/// </summary>
public static class PyRound
{
    /// <summary>Acima disto o <see cref="decimal"/> não representa o valor; a esta grandeza o double já não tem casas úteis.</summary>
    private const double DecimalSafeLimit = 1e27;

    public static double Round2(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= DecimalSafeLimit)
        {
            return value;
        }

        // "F30" imprime os dígitos exatos do binário (a expansão decimal de um double é finita).
        var exact = decimal.Parse(value.ToString("F30", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);
        var rounded = decimal.Round(exact, 2, MidpointRounding.ToEven);
        return (double)rounded;
    }
}
