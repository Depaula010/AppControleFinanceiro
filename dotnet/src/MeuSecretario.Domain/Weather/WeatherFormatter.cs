using System.Globalization;

namespace MeuSecretario.Domain.Weather;

/// <summary>Formatação do clima para o resumo matinal (<c>weather_service.py</c>).</summary>
public static class WeatherFormatter
{
    /// <summary>
    /// <c>descricao</c> vazia ou nula devolve <c>""</c>; a sensação aparece com diferença &gt;= 3; chuva &gt;= 60 vira
    /// "Alta chance", &gt;= 30 "Possível chuva".
    /// </summary>
    public static string FormatForBriefing(string? fullDescription, int temperature, int feelsLike, int? rainChance)
    {
        if (string.IsNullOrEmpty(fullDescription))
        {
            return string.Empty;
        }

        var text = $"\U0001F321️ Clima: {fullDescription}";
        if (Math.Abs(temperature - feelsLike) >= 3)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" (sensação de {feelsLike}°C)");
        }

        if (rainChance is { } chance)
        {
            if (chance >= 60)
            {
                text += string.Create(CultureInfo.InvariantCulture, $"\n☔ Alta chance de chuva ({chance}%)");
            }
            else if (chance >= 30)
            {
                text += string.Create(CultureInfo.InvariantCulture, $"\n\U0001F326️ Possível chuva ({chance}%)");
            }
        }

        return text;
    }

    /// <summary><c>int()</c> do Python: trunca em direção a zero (24,9 vira 24; -0,5 vira 0).</summary>
    public static int Truncate(double value) => (int)Math.Truncate(value);
}
