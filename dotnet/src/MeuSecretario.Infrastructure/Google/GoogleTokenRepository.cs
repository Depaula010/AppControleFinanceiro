using System.Text.Json;
using Dapper;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Infrastructure.Database;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>
/// Tabela <c>googlecalendartokens</c> com Dapper (spec 010 plan §5.3). Tokens em texto puro (desvio 029): todo acesso
/// passa por aqui, então cifrar depois não altera nenhum chamador.
/// </summary>
public sealed class GoogleTokenRepository(IDbConnectionFactory connections) : IGoogleTokenRepository
{
    private const string FindSql =
        """
        SELECT access_token AS AccessToken, refresh_token AS RefreshToken, token_expiry AS TokenExpiry,
               scopes AS Scopes, needs_reconnect AS NeedsReconnect
        FROM googlecalendartokens WHERE usuario_id = @uid
        """;

    private const string SaveSql =
        """
        INSERT INTO googlecalendartokens (usuario_id, access_token, refresh_token, token_expiry, scopes, needs_reconnect, updated_at)
        VALUES (@uid, @access, @refresh, @expiry, @scopes, FALSE, CURRENT_TIMESTAMP)
        ON CONFLICT (usuario_id) DO UPDATE SET
          access_token = EXCLUDED.access_token,
          refresh_token = COALESCE(EXCLUDED.refresh_token, googlecalendartokens.refresh_token),
          token_expiry = EXCLUDED.token_expiry,
          scopes = EXCLUDED.scopes,
          needs_reconnect = FALSE,
          updated_at = CURRENT_TIMESTAMP
        """;

    public async Task<GoogleTokenRecord?> FindAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GoogleTokenRecord>(
            new CommandDefinition(FindSql, new { uid = userId }, cancellationToken: cancellationToken));
    }

    public async Task SaveAsync(int userId, GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            SaveSql,
            new
            {
                uid = userId,
                access = tokens.AccessToken,
                refresh = tokens.RefreshToken,
                expiry = tokens.ExpiresAtUtc?.UtcDateTime,
                scopes = ScopesJson.Serialize(tokens.Scopes),
            },
            cancellationToken: cancellationToken));
    }

    public async Task MarkNeedsReconnectAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE googlecalendartokens SET needs_reconnect = TRUE, updated_at = CURRENT_TIMESTAMP WHERE usuario_id = @uid",
            new { uid = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> GetScopesAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT scopes FROM googlecalendartokens WHERE usuario_id = @uid",
            new { uid = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> DeleteAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM googlecalendartokens WHERE usuario_id = @uid",
            new { uid = userId },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}

/// <summary>Array JSON de textos como o <c>json.dumps</c> do Python (separador <c>", "</c>), que o Flask lê com <c>json.loads</c>.</summary>
internal static class ScopesJson
{
    public static string Serialize(IReadOnlyList<string> scopes) =>
        "[" + string.Join(", ", scopes.Select(scope => JsonSerializer.Serialize(scope))) + "]";
}

/// <summary><c>SELECT nome, numero_whatsapp FROM usuarios WHERE id = @id</c> para as páginas e notificações do Google.</summary>
public sealed class GoogleUserLookup(IDbConnectionFactory connections) : IGoogleUserLookup
{
    public async Task<GoogleUserInfo?> FindAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<GoogleUserInfo>(new CommandDefinition(
            "SELECT nome AS Name, numero_whatsapp AS WhatsappNumber FROM usuarios WHERE id = @id",
            new { id = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<string?> FindNameAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT nome FROM usuarios WHERE id = @id",
            new { id = userId },
            cancellationToken: cancellationToken));
    }
}
