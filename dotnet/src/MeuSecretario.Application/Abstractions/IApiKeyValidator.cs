namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// Valida o header <c>X-API-KEY</c> contra a <c>API_SECRET_KEY</c> (credencial servidor-a-servidor).
/// Reutilizável por <c>/api-keys/*</c> (008), <c>/admin/*</c> (009) e webhooks (012).
/// </summary>
public interface IApiKeyValidator
{
    /// <summary>Compara em tempo constante; <c>null</c> ou vazio nunca é válido.</summary>
    bool IsValid(string? presented);
}
