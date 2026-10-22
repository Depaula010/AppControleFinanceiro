namespace MeuSecretario.Domain.Weather;

/// <summary>Condição traduzida: nome em português e emoji.</summary>
public readonly record struct WeatherCondition(string Name, string Emoji);

/// <summary>
/// Tradução das condições do WeatherAPI (<c>weather_service.py</c>, 22 entradas). Desvio 054: o Flask só casava o
/// texto em inglês, mas envia <c>lang=pt</c>, então nunca casava. Aqui casa pelo texto em inglês, pelo texto em
/// português (comparação sem diferenciar maiúsculas) e pelo <c>condition.code</c>; sem casamento, vale o texto da
/// API com o termômetro, como no Flask.
/// </summary>
public static class WeatherConditions
{
    public const string FallbackEmoji = "\U0001F321️";
    public const string UnknownText = "Desconhecido";

    private const string Sun = "☀️";
    private const string Moon = "\U0001F319";
    private const string PartlyCloudy = "⛅";
    private const string Cloud = "☁️";
    private const string SunRain = "\U0001F326️";
    private const string Rain = "\U0001F327️";
    private const string Storm = "⛈️";
    private const string Fog = "\U0001F32B️";
    private const string SnowCloud = "\U0001F328️";
    private const string Snow = "❄️";
    private const string Ice = "\U0001F9CA";

    /// <summary>Mapa literal do Python: inglês, português, emoji.</summary>
    public static readonly IReadOnlyList<(string English, string Portuguese, string Emoji)> Entries =
    [
        ("Sunny", "Ensolarado", Sun),
        ("Clear", "Céu limpo", Moon),
        ("Partly cloudy", "Parcialmente nublado", PartlyCloudy),
        ("Cloudy", "Nublado", Cloud),
        ("Overcast", "Encoberto", Cloud),
        ("Patchy rain possible", "Possível chuva", SunRain),
        ("Light rain", "Chuva leve", Rain),
        ("Moderate rain", "Chuva moderada", Rain),
        ("Heavy rain", "Chuva forte", Rain),
        ("Torrential rain shower", "Chuva torrencial", Storm),
        ("Thundery outbreaks possible", "Possível trovoada", Storm),
        ("Patchy light rain with thunder", "Chuva leve com trovoada", Storm),
        ("Moderate or heavy rain with thunder", "Chuva forte com trovoada", Storm),
        ("Mist", "Névoa", Fog),
        ("Fog", "Neblina", Fog),
        ("Freezing fog", "Neblina congelante", Fog),
        ("Patchy snow possible", "Possível neve", SnowCloud),
        ("Light snow", "Neve leve", Snow),
        ("Heavy snow", "Neve forte", Snow),
        ("Ice pellets", "Granizo", Ice),
        ("Light sleet", "Granizo leve", Ice),
        ("Moderate or heavy sleet", "Granizo forte", Ice),
    ];

    private static readonly Dictionary<string, WeatherCondition> ByText = BuildByText();

    private static readonly Dictionary<int, string> CodeToEnglish = new()
    {
        [1000] = "Sunny",
        [1003] = "Partly cloudy",
        [1006] = "Cloudy",
        [1009] = "Overcast",
        [1030] = "Mist",
        [1063] = "Patchy rain possible",
        [1066] = "Patchy snow possible",
        [1069] = "Light sleet",
        [1072] = "Light rain",
        [1087] = "Thundery outbreaks possible",
        [1114] = "Heavy snow",
        [1117] = "Heavy snow",
        [1135] = "Fog",
        [1147] = "Freezing fog",
        [1150] = "Patchy rain possible",
        [1153] = "Light rain",
        [1168] = "Light rain",
        [1171] = "Heavy rain",
        [1180] = "Patchy rain possible",
        [1183] = "Light rain",
        [1186] = "Moderate rain",
        [1189] = "Moderate rain",
        [1192] = "Heavy rain",
        [1195] = "Heavy rain",
        [1198] = "Light rain",
        [1201] = "Heavy rain",
        [1204] = "Light sleet",
        [1207] = "Moderate or heavy sleet",
        [1210] = "Patchy snow possible",
        [1213] = "Light snow",
        [1216] = "Light snow",
        [1219] = "Light snow",
        [1222] = "Heavy snow",
        [1225] = "Heavy snow",
        [1237] = "Ice pellets",
        [1240] = "Light rain",
        [1243] = "Heavy rain",
        [1246] = "Torrential rain shower",
        [1249] = "Light sleet",
        [1252] = "Moderate or heavy sleet",
        [1255] = "Light snow",
        [1258] = "Heavy snow",
        [1261] = "Ice pellets",
        [1264] = "Ice pellets",
        [1273] = "Patchy light rain with thunder",
        [1276] = "Moderate or heavy rain with thunder",
        [1279] = "Patchy light rain with thunder",
        [1282] = "Moderate or heavy rain with thunder",
    };

    /// <summary>
    /// Traduz pelo texto (inglês ou português), depois pelo código; <paramref name="isDay"/> <c>false</c> troca o código
    /// 1000 (céu limpo) pela versão noturna.
    /// </summary>
    public static WeatherCondition Translate(string? text, int? code, bool? isDay)
    {
        if (!string.IsNullOrWhiteSpace(text) && ByText.TryGetValue(text.Trim(), out var byText))
        {
            return byText;
        }

        if (code is { } c && CodeToEnglish.TryGetValue(c, out var english))
        {
            if (c == 1000 && isDay == false)
            {
                english = "Clear";
            }

            return ByText[english];
        }

        return new WeatherCondition(string.IsNullOrWhiteSpace(text) ? UnknownText : text, FallbackEmoji);
    }

    private static Dictionary<string, WeatherCondition> BuildByText()
    {
        var map = new Dictionary<string, WeatherCondition>(StringComparer.OrdinalIgnoreCase);
        foreach (var (english, portuguese, emoji) in Entries)
        {
            var condition = new WeatherCondition(portuguese, emoji);
            map[english] = condition;
            map[portuguese] = condition;
        }

        return map;
    }
}
