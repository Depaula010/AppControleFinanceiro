using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Infrastructure.Configuration;

/// <summary>Adaptador de <see cref="AppSettings"/> para <see cref="IIntegrationSettings"/>.</summary>
public sealed class AppSettingsIntegrationSettings(AppSettings settings) : IIntegrationSettings
{
    public string GeminiApiKey => settings.GeminiApiKey;

    public string GeminiModel => settings.GeminiModel;

    public string ApiSecretKey => settings.ApiSecretKey;

    public string BotWhatsappUrl => settings.BotWhatsappUrl;

    public string BotApiKey => settings.BotApiKey;

    public string BotSessionName => settings.BotSessionName;

    public string BotSessionId => settings.BotSessionId;

    public string GoogleClientId => settings.GoogleClientId;

    public string GoogleClientSecret => settings.GoogleClientSecret;

    public string GoogleRedirectUri => settings.GoogleRedirectUri;

    public string WeatherApiKey => settings.WeatherApiKey;

    public string OpenRouteApiKey => settings.OpenRouteApiKey;

    public string OpenRouteBaseUrl => settings.OpenRouteBaseUrl;

    public int TravelTimeDailyLimit => settings.TravelTimeDailyLimit;
}
