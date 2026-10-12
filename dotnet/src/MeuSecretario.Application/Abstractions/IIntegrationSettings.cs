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

    /// <summary><c>GOOGLE_CONNECT_LEGACY_UNTIL</c> (desvio 053): links <c>/connect-calendar</c> sem <c>t</c> valem até este instante. Nulo = desligado.</summary>
    DateTimeOffset? GoogleConnectLegacyUntil { get; }

    string WeatherApiKey { get; }

    string OpenRouteApiKey { get; }

    string OpenRouteBaseUrl { get; }

    int TravelTimeDailyLimit { get; }
}
