namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Leitura (só propriedades) dos valores de configuração que o Application precisa nas integrações. Adaptador de
/// <c>AppSettings</c> na Infrastructure. Os valores são segredos: nunca registrar.
/// </summary>
public interface IIntegrationSettings
{
    string GeminiApiKey { get; }

    string GeminiModel { get; }

    string ApiSecretKey { get; }

    string BotWhatsappUrl { get; }

    string BotApiKey { get; }

    string BotSessionName { get; }

    string BotSessionId { get; }

    string GoogleClientId { get; }

    string GoogleClientSecret { get; }

    string GoogleRedirectUri { get; }

    string WeatherApiKey { get; }

    string OpenRouteApiKey { get; }

    string OpenRouteBaseUrl { get; }

    int TravelTimeDailyLimit { get; }
}
