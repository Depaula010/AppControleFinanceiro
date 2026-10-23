namespace MeuSecretario.Api;

/// <summary>
/// Formato das linhas do log da aplicação (console e <c>app-.log</c>). <c>CorrelationPrefix</c> é vazio por padrão (as linhas dos jobs não mudam)
/// e vira <c>"[3f9a1c02] "</c> dentro de uma requisição do webhook (desvio 261): todas as linhas daquela mensagem levam o mesmo id, que também
/// volta no cabeçalho <c>X-Correlation-Id</c> da resposta.
/// </summary>
public static class LogTemplates
{
    public const string Output = "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationPrefix:l}{Message:lj}{NewLine}{Exception}";

    public const string CorrelationProperty = "CorrelationPrefix";

    public const string CorrelationHeader = "X-Correlation-Id";

    /// <summary>Id curto para uma requisição (8 caracteres hexadecimais).</summary>
    public static string NewCorrelationId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>O prefixo que as linhas do log recebem.</summary>
    public static string Prefix(string correlationId) => $"[{correlationId}] ";
}
