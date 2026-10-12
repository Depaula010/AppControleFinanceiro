using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Domain.Common;

namespace MeuSecretario.Application.Google;

/// <summary>Diagnóstico da 010d sem dados sensíveis: só ids, tipo da exceção e rótulos fixos (o Application não tem logger).</summary>
public interface IGoogleDiagnostics
{
    void TokenMarkedInvalid(int userId, string reason);

    void RefreshFailed(int userId, Exception exception);

    void NotificationSent(int userId, string maskedNumber);

    void NotificationFailed(int userId, Exception exception);

    void RevokeFailed(int userId, Exception exception);

    void ScopeCheckFailed(int userId, Exception exception);
}

/// <summary>
/// Credenciais Google do usuário (RN-010-36 a 40, desvios 029 e 058): lê e renova o token, marca <c>needs_reconnect</c>,
/// notifica e revoga. É o <b>único</b> lugar que renova token (D-4): Calendar e Drive só recebem o <c>access_token</c>.
/// </summary>
public sealed class GoogleAccountService(
    IGoogleTokenRepository tokens,
    IGoogleOAuthClient oauth,
    OAuthStateCodec stateCodec,
    TokenRevokedNotifier notifier,
    TimeProvider time,
    IGoogleDiagnostics diagnostics) : IGoogleAccountService
{
    public const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar";

    /// <summary>Margem de renovação da biblioteca do Python (3 min 45 s).</summary>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(225);

    public async Task<GoogleCredentialResult> GetCredentialsAsync(int userId, CancellationToken cancellationToken)
    {
        var record = await tokens.FindAsync(userId, cancellationToken);
        if (record is null)
        {
            return GoogleCredentialResult.Of(GoogleCredentialStatus.NotConnected);
        }

        var scopes = ParseScopes(record.Scopes);
        if (!IsExpired(record))
        {
            return new GoogleCredentialResult(GoogleCredentialStatus.Connected, record.AccessToken, scopes);
        }

        // Desvio 058: já marcado e vencido, não chama o Google de novo (a reconexão zera o flag).
        if (record.NeedsReconnect)
        {
            return GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect);
        }

        if (string.IsNullOrEmpty(record.RefreshToken))
        {
            await MarkInvalidAsync(userId, "no_refresh_token", cancellationToken);
            return GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect);
        }

        GoogleTokenSet refreshed;
        try
        {
            refreshed = await oauth.RefreshAsync(record.RefreshToken, cancellationToken);
        }
        catch (GoogleInvalidGrantException)
        {
            await MarkInvalidAsync(userId, "invalid_grant", cancellationToken);
            return GoogleCredentialResult.Of(GoogleCredentialStatus.NeedsReconnect);
        }
        catch (Exception ex) when (ex is GoogleAuthException or GoogleOAuthConfigurationException)
        {
            // Pode ser temporário: não marca, não notifica.
            diagnostics.RefreshFailed(userId, ex);
            return GoogleCredentialResult.Of(GoogleCredentialStatus.Unavailable);
        }

        var keptScopes = refreshed.Scopes.Count > 0 ? refreshed.Scopes : scopes;
        await tokens.SaveAsync(
            userId,
            new GoogleTokenSet(refreshed.AccessToken, refreshed.RefreshToken ?? record.RefreshToken, refreshed.ExpiresAtUtc, keptScopes),
            cancellationToken);
        return new GoogleCredentialResult(GoogleCredentialStatus.Connected, refreshed.AccessToken, keptScopes);
    }

    public async Task<bool> IsConnectedAsync(int userId, CancellationToken cancellationToken) =>
        (await GetCredentialsAsync(userId, cancellationToken)).Status == GoogleCredentialStatus.Connected;

    public async Task<bool> HasDriveScopeAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await tokens.GetScopesAsync(userId, cancellationToken);
            return !string.IsNullOrEmpty(stored) && ParseStoredArray(stored)?.Contains(DriveScope) == true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.ScopeCheckFailed(userId, ex);
            return false;
        }
    }

    /// <summary>Revoga no Google se houver credencial (falha ignorada) e apaga a linha, sempre (RN-010-40).</summary>
    public async Task<bool> RevokeAsync(int userId, CancellationToken cancellationToken)
    {
        var credentials = await GetCredentialsAsync(userId, cancellationToken);
        if (credentials is { Status: GoogleCredentialStatus.Connected, AccessToken: { } accessToken })
        {
            try
            {
                await oauth.RevokeAsync(accessToken, cancellationToken);
            }
            catch (Exception ex) when (ex is GoogleAuthException)
            {
                diagnostics.RevokeFailed(userId, ex);
            }
        }

        await tokens.DeleteAsync(userId, cancellationToken);
        return true;
    }

    /// <summary>URL do consentimento com <c>state</c> assinado de 10 minutos (desvio 021). Sem credenciais de OAuth lança <see cref="GoogleOAuthConfigurationException"/>.</summary>
    public string BuildAuthorizationUrl(int userId) => oauth.BuildAuthorizationUrl(stateCodec.Create(userId));

    /// <summary>
    /// Valida o <c>state</c> (nenhuma chamada ao Google se for inválido), troca o código e grava os tokens (RN-010-36).
    /// Devolve o <c>usuario_id</c> do <c>state</c>, ou <c>null</c> se ele for inválido, adulterado ou expirado.
    /// </summary>
    public async Task<int?> ExchangeAndSaveAsync(string code, string state, CancellationToken cancellationToken)
    {
        if (stateCodec.Verify(state) is not { } userId)
        {
            return null;
        }

        var tokenSet = await oauth.ExchangeCodeAsync(code, cancellationToken);
        await tokens.SaveAsync(userId, tokenSet, cancellationToken);
        return userId;
    }

    private bool IsExpired(GoogleTokenRecord record) =>
        record.TokenExpiry is { } expiry
        && new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc)) <= time.GetUtcNow() + ExpiryMargin;

    private async Task MarkInvalidAsync(int userId, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await tokens.MarkNeedsReconnectAsync(userId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Como o Python: falha ao marcar não derruba o fluxo e então não notifica.
            diagnostics.RefreshFailed(userId, ex);
            return;
        }

        diagnostics.TokenMarkedInvalid(userId, reason);
        await notifier.NotifyAsync(userId, cancellationToken);
    }

    private static List<string> ParseScopes(string? stored) =>
        (string.IsNullOrEmpty(stored) ? null : ParseStoredArray(stored)) ?? [CalendarScope, DriveScope];

    private static List<string>? ParseStoredArray(string stored)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(stored);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Notificação de token revogado (RN-010-38): no máximo 1 por semana, nunca propaga erro.</summary>
public sealed class TokenRevokedNotifier(
    ICache cache,
    IGoogleUserLookup users,
    IBotClient bot,
    GoogleLinks links,
    IGoogleDiagnostics diagnostics)
{
    public static readonly TimeSpan Throttle = TimeSpan.FromDays(7);

    public async Task NotifyAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var key = CacheKeys.CalendarTokenRevokedAlert(userId);
            if (await cache.ExistsAsync(key, cancellationToken))
            {
                return;
            }

            var user = await users.FindAsync(userId, cancellationToken);
            if (string.IsNullOrEmpty(user?.WhatsappNumber))
            {
                return;
            }

            var sent = await bot.SendTextAsync(
                user.WhatsappNumber, GoogleNotificationMessages.TokenRevoked(links.ConnectUrl(userId)), cancellationToken);
            if (!sent)
            {
                diagnostics.NotificationFailed(userId, new InvalidOperationException("bot não confirmou o envio"));
                return;
            }

            await cache.SetJsonAsync(key, "1", Throttle, cancellationToken);
            diagnostics.NotificationSent(userId, PhoneMask.Last4(user.WhatsappNumber));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.NotificationFailed(userId, ex);
        }
    }
}
