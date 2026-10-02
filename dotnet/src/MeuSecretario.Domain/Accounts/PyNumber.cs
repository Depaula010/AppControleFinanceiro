using System.Globalization;
using System.Numerics;

namespace MeuSecretario.Domain.Accounts;

/// <summary>
/// Conversões estilo Python (spec 002, RN-002-09): <c>float()</c> para valores monetários e <c>int()</c> para dias.
/// </summary>
public static class PyNumber
{
    /// <summary>Módulo máximo (exclusivo) de <c>numeric(15,2)</c>.</summary>
    private static readonly decimal MoneyLimit = 10_000_000_000_000m;

    /// <summary>
    /// <c>float(valor)</c> que também cabe em <c>numeric(15,2)</c>: número JSON, booleano ou texto numérico; não finito
    /// (<c>inf</c>, <c>nan</c>) ou de módulo ≥ 10^13 depois de arredondar a 2 casas é inválido (desvio 033).
    /// O <see cref="decimal"/> vem da representação mais curta do <see cref="double"/>, como o driver do Python envia
    /// o <c>float</c>, para o arredondamento do <c>numeric</c> do banco ser o mesmo (<c>10.005</c> vira <c>10.01</c>).
    /// </summary>
    public static bool TryMoney(RawValue raw, out decimal value)
    {
        value = 0;
        switch (raw)
        {
            case RawValue.BoolValue b:
                value = b.Value ? 1 : 0;
                return true;
            case RawValue.IntegerValue i:
                if (BigInteger.Abs(i.Value) >= new BigInteger(MoneyLimit))
                {
                    return false;
                }
                value = (decimal)i.Value;
                return true;
            case RawValue.DecimalValue d:
                return FromDouble(d.Value, out value);
            case RawValue.TextValue t:
                return double.TryParse(t.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && FromDouble(parsed, out value);
            default:
                return false;
        }
    }

    /// <summary><c>int(valor)</c>: inteiro, número com parte decimal (truncado), texto inteiro ou booleano.</summary>
    public static bool TryInt(RawValue raw, out int value)
    {
        value = 0;
        switch (raw)
        {
            case RawValue.BoolValue b:
                value = b.Value ? 1 : 0;
                return true;
            case RawValue.IntegerValue i:
                return FitsInt(i.Value, out value);
            case RawValue.DecimalValue d:
                return double.IsFinite(d.Value)
                    && Math.Abs(d.Value) < int.MaxValue
                    && FitsInt(new BigInteger(Math.Truncate(d.Value)), out value);
            case RawValue.TextValue t:
                return BigInteger.TryParse(t.Value.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var big)
                    && FitsInt(big, out value);
            default:
                return false;
        }
    }

    private static bool FromDouble(double number, out decimal value)
    {
        value = 0;
        if (!double.IsFinite(number))
        {
            return false;
        }

        if (!decimal.TryParse(number.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        return Math.Abs(Math.Round(value, 2, MidpointRounding.AwayFromZero)) < MoneyLimit;
    }

    private static bool FitsInt(BigInteger number, out int value)
    {
        value = 0;
        if (number < int.MinValue || number > int.MaxValue)
        {
            return false;
        }
        value = (int)number;
        return true;
    }
}
