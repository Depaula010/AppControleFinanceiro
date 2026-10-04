using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.External;

/// <summary>
/// Logs do clima, das rotas e da validação de chaves (EventIds 10500 a 10599). Só serviço, status HTTP, tipo da
/// exceção e motivo da falha de chave. Nunca a chave, a URL (<c>key=</c>/<c>api_key=</c>), o endereço nem as
/// coordenadas (RN-010-65, desvio 008).
/// </summary>
internal static partial class ExternalLog
{
    [LoggerMessage(EventId = 10501, Level = LogLevel.Warning, Message = "WEATHER_API_KEY não configurada; clima desativado.")]
    public static partial void WeatherDisabled(ILogger logger);

    [LoggerMessage(EventId = 10502, Level = LogLevel.Warning, Message = "Chave de {Service} do usuário indisponível ({Reason}); chamada não realizada.")]
    public static partial void KeyUnavailable(ILogger logger, string service, string reason);

    [LoggerMessage(EventId = 10503, Level = LogLevel.Warning, Message = "{Service} respondeu HTTP {Status}.")]
    public static partial void HttpStatus(ILogger logger, string service, int status);

    [LoggerMessage(EventId = 10504, Level = LogLevel.Warning, Message = "Falha ao consultar {Service}: {Error}")]
    public static partial void Failed(ILogger logger, string service, string error);

    [LoggerMessage(EventId = 10505, Level = LogLevel.Warning, Message = "OPENROUTE_API_KEY não configurada; {Operation} não realizado.")]
    public static partial void RouteDisabled(ILogger logger, string operation);

    [LoggerMessage(EventId = 10506, Level = LogLevel.Information, Message = "OpenRoute ({Operation}) falhou na tentativa {Attempt}: {Reason}; tentando novamente.")]
    public static partial void RouteRetry(ILogger logger, string operation, int attempt, string reason);

    [LoggerMessage(EventId = 10507, Level = LogLevel.Information, Message = "OpenRoute ({Operation}) sem resultado utilizável: {Reason}.")]
    public static partial void RouteNoResult(ILogger logger, string operation, string reason);

    [LoggerMessage(EventId = 10508, Level = LogLevel.Warning, Message = "Validação de chave de {Provider} indisponível: {Error}")]
    public static partial void ValidationUnavailable(ILogger logger, string provider, string error);

    [LoggerMessage(EventId = 10509, Level = LogLevel.Warning, Message = "Falha no limite diário de rotas ({Operation}): {Error}")]
    public static partial void RateLimitFailed(ILogger logger, string operation, string error);
}
