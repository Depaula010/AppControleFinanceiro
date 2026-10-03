namespace MeuSecretario.Application.Abstractions;

/// <summary>WeatherAPI (spec 010 RN-010-55/56). Qualquer falha devolve <c>null</c>; <paramref name="userId"/> nulo = chave do sistema.</summary>
public interface IWeatherClient
{
    Task<WeatherInfo?> GetWeatherAsync(string city, string? state, int? userId, CancellationToken cancellationToken);

    Task<ForecastInfo?> GetForecastAsync(string city, string? state, int days, int? userId, CancellationToken cancellationToken);
}

/// <summary>Nomes e tipos como o dict do Python (<c>temperatura</c>, <c>sensacao_termica</c>, <c>umidade</c>...).</summary>
public sealed record WeatherInfo(
    int Temperature,
    string Condition,
    string Emoji,
    int FeelsLike,
    int Humidity,
    int? RainChance,
    string FullDescription);

public sealed record ForecastInfo(int RainChance, int MaxTemp, int MinTemp);
