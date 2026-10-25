namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Endereço, nome do <c>HttpClient</c> e limites de tempo do Gemini (spec 010 §6.2, decisão D-3). O Python não define
/// timeout. Modelo e versão da API vêm da tabela <c>configuracoes</c> (desvio 263; <c>GEMINI_MODEL</c> é só o
/// padrão). Nota: a chave de cache não inclui o modelo, então trocá-lo não invalida o cache.
/// </summary>
public static class GeminiHttpOptions
{
    public const string ClientName = "gemini";
    public const string DefaultApiVersion = "v1beta";
    public const string DefaultModel = "gemini-2.5-flash-lite";

    public static string BaseUrl(string apiVersion) => $"https://generativelanguage.googleapis.com/{apiVersion}/";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Um retry (duas tentativas no máximo) e só em 503, 504, timeout ou falha de rede.</summary>
    public const int MaxRetries = 1;
}
