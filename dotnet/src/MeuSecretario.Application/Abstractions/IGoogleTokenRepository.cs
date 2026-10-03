namespace MeuSecretario.Application.Abstractions;

/// <summary>Tabela <c>googlecalendartokens</c> (tokens em texto puro, desvio 029). Porta interna da 010d-1.</summary>
public interface IGoogleTokenRepository
{
    Task<GoogleTokenRecord?> FindAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Upsert; mantém o refresh token anterior quando o novo é nulo e zera <c>needs_reconnect</c>.</summary>
    Task SaveAsync(int userId, GoogleTokenSet tokens, CancellationToken cancellationToken);

    Task MarkNeedsReconnectAsync(int userId, CancellationToken cancellationToken);

    Task<string?> GetScopesAsync(int userId, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int userId, CancellationToken cancellationToken);
}

/// <summary>Linha da tabela. <paramref name="Scopes"/> é o texto guardado (array JSON).</summary>
public sealed record GoogleTokenRecord(string AccessToken, string? RefreshToken, DateTime? TokenExpiry, string? Scopes, bool NeedsReconnect)
{
    /// <summary>Nunca inclui os tokens.</summary>
    public override string ToString() => $"GoogleTokenRecord {{ NeedsReconnect = {NeedsReconnect}, Tokens = *** }}";
}

/// <summary>Resultado de troca de código ou renovação.</summary>
public sealed record GoogleTokenSet(string AccessToken, string? RefreshToken, DateTime? ExpiresAtUtc, IReadOnlyList<string> Scopes)
{
    /// <summary>Nunca inclui os tokens.</summary>
    public override string ToString() => "GoogleTokenSet { Tokens = *** }";
}
