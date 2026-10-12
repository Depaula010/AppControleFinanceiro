namespace MeuSecretario.Application.Abstractions;

/// <summary>Fluxo OAuth do Google (porta interna da 010d-1; o SDK fica na Infrastructure).</summary>
public interface IGoogleOAuthClient
{
    string BuildAuthorizationUrl(string state);

    Task<GoogleTokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken);

    Task<GoogleTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string token, CancellationToken cancellationToken);
}

/// <summary>Google respondeu <c>invalid_grant</c> (token revogado ou expirado).</summary>
public sealed class GoogleInvalidGrantException() : Exception("invalid_grant");

/// <summary>Outra falha do endpoint OAuth do Google (sem corpo, sem token).</summary>
public sealed class GoogleAuthException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary><c>GOOGLE_CLIENT_ID</c>/<c>GOOGLE_CLIENT_SECRET</c> ausentes (erro de configuração, nunca com valores).</summary>
public sealed class GoogleOAuthConfigurationException() : Exception("Credenciais OAuth2 não configuradas (GOOGLE_CLIENT_ID/SECRET)");
