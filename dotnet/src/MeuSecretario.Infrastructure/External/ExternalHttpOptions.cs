using Polly.Timeout;

namespace MeuSecretario.Infrastructure.External;

/// <summary>Nomes e limites dos <c>HttpClient</c> do clima, das rotas e da validação de chaves (spec 010 §8.2).</summary>
public static class ExternalHttpOptions
{
    public const string WeatherClient = "weather";
    public const string RouteClient = "openroute";
    public const string KeyValidationClient = "key-validation";

    public const string WeatherBaseUrl = "https://api.weatherapi.com/v1";

    public static readonly TimeSpan WeatherTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan RouteTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Duas tentativas no OpenRoute, sem espera (RN-010-62; só em timeout, rede e 5xx pelo desvio 064).</summary>
    public const int RouteMaxAttempts = 2;

    /// <summary>Timeout ou falha de rede (não o cancelamento do chamador).</summary>
    internal static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or TimeoutRejectedException or TimeoutException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);
}
