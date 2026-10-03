using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Bot;

/// <summary>
/// Descobre o <c>session_id</c> do bot (RN-010-11): <c>BOT_SESSION_ID</c> se definido; senão o cache
/// <c>bot_session_id:{nome}</c> (600 s); senão <c>GET /api/v1/sessions</c> e a primeira sessão com <c>session_name</c>
/// exatamente igual a <c>BOT_SESSION_NAME</c>. Qualquer falha devolve <c>null</c> ("sem sessão", usa o endpoint legado).
/// </summary>
public sealed class BotSessionResolver(IHttpClientFactory httpClients, ICache cache, IIntegrationSettings settings, ILogger<BotSessionResolver> logger)
{
    /// <param name="forceRefresh">Ignora o cache e consulta o bot (usado depois de um 404).</param>
    public async Task<string?> ResolveAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.BotSessionId))
        {
            return settings.BotSessionId;
        }

        var cacheKey = CacheKeys.BotSessionId(settings.BotSessionName);
        if (!forceRefresh)
        {
            var cached = await cache.GetJsonAsync<string>(cacheKey, cancellationToken);
            if (!string.IsNullOrEmpty(cached))
            {
                return cached;
            }
        }

        var sessionId = await QueryBotAsync(cancellationToken);
        if (sessionId is not null)
        {
            await cache.SetJsonAsync(cacheKey, sessionId, BotHttpOptions.SessionCacheTtl, cancellationToken);
        }

        return sessionId;
    }

    private async Task<string?> QueryBotAsync(CancellationToken cancellationToken)
    {
        var baseUrl = settings.BotWhatsappUrl.Trim().TrimEnd('/');
        if (baseUrl.Length == 0)
        {
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/sessions");
            request.Headers.TryAddWithoutValidation("X-API-Key", settings.BotApiKey);
            using var client = httpClients.CreateClient(BotHttpOptions.TextClient);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                BotLog.SessionNotResolved(logger, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var sessionId = FindSession(document.RootElement, settings.BotSessionName);
            if (sessionId is null)
            {
                BotLog.SessionNotResolved(logger, "sessão sem correspondência");
                return null;
            }

            BotLog.SessionResolved(logger, sessionId);
            return sessionId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            BotLog.SessionNotResolved(logger, ex.GetType().Name);
            return null;
        }
    }

    private static string? FindSession(JsonElement root, string sessionName)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var session in data.EnumerateArray())
        {
            if (session.ValueKind == JsonValueKind.Object
                && session.TryGetProperty("session_name", out var name)
                && name.ValueKind == JsonValueKind.String
                && string.Equals(name.GetString(), sessionName, StringComparison.Ordinal)
                && session.TryGetProperty("session_id", out var id)
                && id.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(id.GetString()))
            {
                return id.GetString();
            }
        }

        return null;
    }
}
