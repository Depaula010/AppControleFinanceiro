using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Application.Google;

/// <summary>
/// Token assinado compartilhado pelo <c>state</c> do OAuth (desvio 021) e pelo link de conexão (desvio 053):
/// <c>base64url(payload) + "." + base64url(HMAC-SHA256(payload_b64))</c>, payload JSON <c>{"p":..,"u":..,"e":..}</c>.
/// A chave é <c>API_SECRET_KEY</c> em UTF-8; a assinatura é comparada em tempo constante.
/// </summary>
internal static class SignedToken
{
    public static string Create(string key, string purpose, int userId, DateTimeOffset expiresAt)
    {
        var payload = new JsonObject
        {
            ["p"] = purpose,
            ["u"] = userId,
            ["e"] = expiresAt.ToUnixTimeSeconds(),
        };
        var payloadPart = Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()));
        return payloadPart + "." + Base64Url(Sign(key, payloadPart));
    }

    /// <summary>Devolve o <c>usuario_id</c> ou <c>null</c> (adulterado, outra chave, outro propósito, expirado, malformado).</summary>
    public static int? Verify(string key, string purpose, string? token, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return null;
        }

        try
        {
            var signature = FromBase64Url(parts[1]);
            if (!CryptographicOperations.FixedTimeEquals(signature, Sign(key, parts[0])))
            {
                return null;
            }

            var json = JsonNode.Parse(FromBase64Url(parts[0]))?.AsObject();
            if (json is null
                || json["p"]?.GetValue<string>() != purpose
                || json["e"]?.GetValue<long>() is not { } expires
                || json["u"]?.GetValue<int>() is not { } userId)
            {
                return null;
            }

            return expires > now.ToUnixTimeSeconds() ? userId : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] Sign(string key, string payloadPart) =>
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.ASCII.GetBytes(payloadPart));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var padded = text.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
        return Convert.FromBase64String(padded);
    }
}

/// <summary><c>state</c> do OAuth (RN-010-35, desvio 021): validade de 10 minutos.</summary>
public sealed class OAuthStateCodec(IIntegrationSettings settings, TimeProvider time)
{
    public const string Purpose = "gcal-oauth";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Create(int userId) =>
        SignedToken.Create(settings.ApiSecretKey, Purpose, userId, time.GetUtcNow() + Lifetime);

    public int? Verify(string? state) => SignedToken.Verify(settings.ApiSecretKey, Purpose, state, time.GetUtcNow());
}

/// <summary>Token <c>?t=</c> do link de conexão (desvio 053): validade de 7 dias.</summary>
public sealed class ConnectLinkTokenCodec(IIntegrationSettings settings, TimeProvider time)
{
    public const string Purpose = "gcal-connect";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public string Create(int userId) =>
        SignedToken.Create(settings.ApiSecretKey, Purpose, userId, time.GetUtcNow() + Lifetime);

    public int? Verify(string? token) => SignedToken.Verify(settings.ApiSecretKey, Purpose, token, time.GetUtcNow());
}
