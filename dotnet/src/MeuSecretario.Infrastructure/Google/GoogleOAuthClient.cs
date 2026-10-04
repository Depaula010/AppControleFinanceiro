using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using MeuSecretario.Application.Abstractions;
using GoogleHttp = Google.Apis.Http;

namespace MeuSecretario.Infrastructure.Google;

/// <summary>
/// Fluxo OAuth do Google (RN-010-34, 36, 37 e 40). Fala com <c>accounts.google.com</c> e <c>oauth2.googleapis.com</c> por
/// formulário, pelo <see cref="GoogleHttp.IHttpClientFactory"/> do Google (timeout de 60 s, substituível em teste).
/// <b>Sem PKCE</b>: não envia <c>code_challenge</c> nem <c>code_verifier</c> (decisoes-onda-1 §7).
/// Nenhuma exceção carrega corpo de resposta, token, <c>code</c> ou segredo.
/// </summary>
public sealed partial class GoogleOAuthClient(IIntegrationSettings settings, GoogleHttp.IHttpClientFactory httpFactory, TimeProvider time)
    : IGoogleOAuthClient
{
    public const string AuthorizationUrl = "https://accounts.google.com/o/oauth2/auth";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string CalendarScope = "https://www.googleapis.com/auth/calendar";
    public const string DriveScope = "https://www.googleapis.com/auth/drive.file";

    public static readonly TimeSpan RevokeTimeout = TimeSpan.FromSeconds(10);

    public string BuildAuthorizationUrl(string state)
    {
        RequireConfiguration();

        var query = new (string Key, string Value)[]
        {
            ("response_type", "code"),
            ("client_id", settings.GoogleClientId),
            ("redirect_uri", settings.GoogleRedirectUri),
            ("scope", CalendarScope + " " + DriveScope),
            ("state", state),
            ("access_type", "offline"),
            ("include_granted_scopes", "true"),
            ("prompt", "consent"),
        };
        return AuthorizationUrl + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    public Task<GoogleTokenSet> ExchangeCodeAsync(string code, CancellationToken cancellationToken)
    {
        RequireConfiguration();
        return RequestTokensAsync(
            [
                ("grant_type", "authorization_code"),
                ("code", code),
                ("redirect_uri", settings.GoogleRedirectUri),
                ("client_id", settings.GoogleClientId),
                ("client_secret", settings.GoogleClientSecret),
            ],
            previousRefreshToken: null,
            cancellationToken);
    }

    public Task<GoogleTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RequireConfiguration();
        return RequestTokensAsync(
            [
                ("grant_type", "refresh_token"),
                ("refresh_token", refreshToken),
                ("client_id", settings.GoogleClientId),
                ("client_secret", settings.GoogleClientSecret),
            ],
            previousRefreshToken: refreshToken,
            cancellationToken);
    }

    public async Task RevokeAsync(string token, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RevokeTimeout);

        try
        {
            using var client = httpFactory.CreateHttpClient(new GoogleHttp.CreateHttpClientArgs());
            using var content = new FormUrlEncodedContent([new("token", token)]);
            using var response = await client.PostAsync(new Uri(RevokeUrl), content, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new GoogleAuthException($"revoke respondeu HTTP {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GoogleAuthException("falha de rede ao revogar", ex);
        }
    }

    private async Task<GoogleTokenSet> RequestTokensAsync(
        (string Key, string Value)[] form, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        HttpStatusCode status;
        string body;
        try
        {
            using var client = httpFactory.CreateHttpClient(new GoogleHttp.CreateHttpClientArgs());
            using var content = new FormUrlEncodedContent(form.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));
            using var response = await client.PostAsync(new Uri(TokenUrl), content, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new GoogleAuthException("falha de rede no endpoint de token", ex);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new GoogleAuthException($"resposta do token não é JSON (HTTP {(int)status})");
        }

        if ((int)status is < 200 or > 299)
        {
            var error = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
            if (error == "invalid_grant")
            {
                throw new GoogleInvalidGrantException();
            }

            // Só o código curto do OAuth (invalid_client, ...), nunca a descrição nem o corpo.
            var safe = error is not null && ErrorCode().IsMatch(error) ? $", {error}" : string.Empty;
            throw new GoogleAuthException($"endpoint de token respondeu HTTP {(int)status}{safe}");
        }

        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("access_token", out var access)
            || access.ValueKind != JsonValueKind.String
            || string.IsNullOrEmpty(access.GetString()))
        {
            throw new GoogleAuthException("resposta do token sem access_token");
        }

        var refresh = root.TryGetProperty("refresh_token", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        DateTimeOffset? expiresAt = root.TryGetProperty("expires_in", out var ex2) && ex2.ValueKind == JsonValueKind.Number && ex2.TryGetInt64(out var seconds)
            ? time.GetUtcNow().AddSeconds(seconds)
            : null;
        IReadOnlyList<string> scopes = root.TryGetProperty("scope", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

        return new GoogleTokenSet(access.GetString()!, refresh ?? previousRefreshToken, expiresAt, scopes);
    }

    private void RequireConfiguration()
    {
        if (string.IsNullOrWhiteSpace(settings.GoogleClientId) || string.IsNullOrWhiteSpace(settings.GoogleClientSecret))
        {
            throw new GoogleOAuthConfigurationException();
        }
    }

    [GeneratedRegex("^[a-z_]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorCode();
}
