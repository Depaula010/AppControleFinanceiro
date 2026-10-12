namespace MeuSecretario.Application.Abstractions;

/// <summary>Credenciais Google do usuário, já renovadas quando preciso (spec 010 RN-010-37 a 40).</summary>
public interface IGoogleAccountService
{
    Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Pode renovar o token e disparar WhatsApp (RN-010-39, como o Flask).</summary>
    Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken);

    Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken);

    Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken);
}

public enum GoogleCredentialStatus
{
    Connected,
    NotConnected,
    NeedsReconnect,
    Unavailable,
}

/// <param name="AccessToken">Só preenchido com <see cref="GoogleCredentialStatus.Connected"/>; nunca registrar.</param>
public sealed record GoogleCredentialResult(GoogleCredentialStatus Status, string? AccessToken, IReadOnlyList<string> Scopes)
{
    public static GoogleCredentialResult Of(GoogleCredentialStatus status) => new(status, null, []);

    /// <summary>Nunca inclui o token.</summary>
    public override string ToString() => $"GoogleCredentialResult {{ Status = {Status}, AccessToken = ***, Scopes = {Scopes.Count} }}";
}

/// <summary>Usuário sem Google conectado (ou sem escopo do Drive); a mensagem é o texto literal de RN-010-46.</summary>
public sealed class GoogleNotConnectedException(string message) : Exception(message);
