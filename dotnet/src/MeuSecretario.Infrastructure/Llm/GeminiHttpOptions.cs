namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Endereço, nome do <c>HttpClient</c> e limites de tempo do Gemini (spec 010 §6.2, decisão D-3). O Python não define
/// timeout. Nota: a chave de cache não inclui o modelo, então trocar <c>GEMINI_MODEL</c> não invalida o cache.
/// </summary>
public static class GeminiHttpOptions
{
    public const string ClientName = "gemini";
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";
    public const string DefaultModel = "gemini-2.5-flash-lite";

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Um retry (duas tentativas no máximo) e só em 503, 504, timeout ou falha de rede.</summary>
    public const int MaxRetries = 1;
}
