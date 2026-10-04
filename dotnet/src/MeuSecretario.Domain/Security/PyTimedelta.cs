using System.Globalization;

namespace MeuSecretario.Domain.Security;

/// <summary>
/// <c>str(timedelta)</c> do Python sem os microssegundos (<c>H:MM:SS</c>, hora sem zero à esquerda; com dias,
/// <c>1 day, 2:03:04</c>). Só para durações positivas.
/// </summary>
public static class PyTimedelta
{
    public static string Format(TimeSpan value)
    {
        var total = (long)Math.Floor(value.TotalSeconds);
        var days = total / 86_400;
        var rest = total % 86_400;
        var clock = string.Create(
            CultureInfo.InvariantCulture,
            $"{rest / 3600}:{rest % 3600 / 60:00}:{rest % 60:00}");
        return days == 0
            ? clock
            : string.Create(CultureInfo.InvariantCulture, $"{days} day{(days == 1 ? string.Empty : "s")}, {clock}");
    }
}
