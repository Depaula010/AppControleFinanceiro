using System.Numerics;

namespace MeuSecretario.Domain.Routes;

/// <summary>Conversões da resposta do OpenRouteService com a aritmética do Python (<c>travel_time_service.py</c>).</summary>
public static class TravelMath
{
    /// <summary><c>int(seconds / 60)</c>: trunca.</summary>
    public static int DurationMinutes(double seconds) => (int)Math.Truncate(seconds / 60);

    /// <summary><c>round(meters / 1000, 1)</c> do Python.</summary>
    public static double DistanceKm(double meters) => RoundToTenth(meters / 1000);

    /// <summary>
    /// Arredonda para 1 casa como o <c>round</c> do Python: metade para o par, sobre o valor binário EXATO do double
    /// (12,45 vale 12,4499... e dá 12,4; 12,55 vale 12,5500...07 e dá 12,6). <see cref="Math.Round(double,int)"/> e
    /// <see cref="decimal"/> não garantem isso.
    /// </summary>
    internal static double RoundToTenth(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value == 0)
        {
            return value;
        }

        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponentBits = (int)((bits >> 52) & 0x7FF);
        var fraction = bits & 0xFFFFFFFFFFFFFL;
        var mantissa = exponentBits == 0 ? fraction : fraction | (1L << 52);
        var exponent = (exponentBits == 0 ? 1 : exponentBits) - 1075;

        // value = mantissa * 2^exponent; queremos round(value * 10).
        var scaled = new BigInteger(mantissa) * 10;
        BigInteger rounded;
        if (exponent >= 0)
        {
            rounded = scaled << exponent;
        }
        else
        {
            var shift = -exponent;
            var quotient = scaled >> shift;
            var remainder = scaled - (quotient << shift);
            var half = BigInteger.One << (shift - 1);
            var comparison = remainder.CompareTo(half);
            rounded = comparison > 0 || (comparison == 0 && !quotient.IsEven) ? quotient + 1 : quotient;
        }

        var result = (double)rounded / 10;
        return negative ? -result : result;
    }
}
