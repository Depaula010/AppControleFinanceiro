using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.Domain.Security;

namespace MeuSecretario.Infrastructure.Security;

/// <summary>
/// <see cref="ISecurityStore"/> sobre o <see cref="ICache"/> (spec 009 §5.5 a §5.7). O valor interno é um JSON compacto
/// que o <c>ICache</c> codifica de novo (<c>SetJsonAsync&lt;string&gt;</c>), igual à codificação dupla do Python
/// (<c>json.dumps(json.dumps(...))</c>). Fail-open: o <c>ICache</c> nunca lança; JSON inválido vira ausente/corrompido.
/// </summary>
public sealed class RedisSecurityStore(ICache cache) : ISecurityStore
{
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => cache.IsAvailableAsync(cancellationToken);

    public Task<bool> IsBlacklistedAsync(string ip, CancellationToken cancellationToken) =>
        cache.ExistsAsync(SecurityKeys.Blacklist(ip), cancellationToken);

    public async Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken cancellationToken)
    {
        var key = SecurityKeys.Blocked(ip);
        if (!await cache.ExistsAsync(key, cancellationToken))
        {
            return false;
        }

        var blockedUntil = ParseObject(await cache.GetJsonAsync<string>(key, cancellationToken)) is { } data
            && data.TryGetProperty("blocked_until", out var until)
            && until.ValueKind == JsonValueKind.String
            && SecurityTimestamp.TryParse(until.GetString(), out var parsed)
                ? parsed
                : (DateTime?)null;

        if (blockedUntil is null)
        {
            // Valor corrompido ou sem blocked_until: apaga a chave e segue (AC-009-11).
            await cache.DeleteAsync(key, cancellationToken);
            return false;
        }

        return now < blockedUntil.Value;
    }

    public async Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken cancellationToken)
    {
        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            return TrackResult.Unavailable;
        }

        var key = SecurityKeys.Attempts(ip);
        var previous = ParseAttempts(await cache.GetJsonAsync<string>(key, cancellationToken));
        var attempts = AttemptWindow.Prune(previous, now).Append(new Attempt(now, reason)).ToList();

        var saved = await cache.SetJsonAsync(key, SerializeAttempts(attempts), SecurityKeys.AttemptsTtl, cancellationToken);
        if (!saved)
        {
            return TrackResult.Unavailable;
        }

        if (attempts.Count < AttemptWindow.BlockThreshold)
        {
            return TrackResult.Tracked(attempts.Count);
        }

        var (count, until, persisted) = await BlockAsync(ip, now, cancellationToken);
        await cache.DeleteAsync(key, cancellationToken);
        return TrackResult.Blocked(attempts.Count, count, until, persisted);
    }

    public Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object?>
        {
            ["ip"] = ip,
            ["reason"] = reason,
            ["blacklisted_at"] = SecurityTimestamp.Format(now),
            ["permanent"] = true,
        };
        return cache.SetJsonAsync(SecurityKeys.Blacklist(ip), JsonSerializer.Serialize(data), SecurityKeys.BlacklistTtl, cancellationToken);
    }

    public Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken cancellationToken) =>
        cache.DeleteAsync(SecurityKeys.Blacklist(ip), cancellationToken);

    public async Task<bool> RemoveTempBlockAsync(string ip, CancellationToken cancellationToken)
    {
        var blocked = await cache.DeleteAsync(SecurityKeys.Blocked(ip), cancellationToken);
        var attempts = await cache.DeleteAsync(SecurityKeys.Attempts(ip), cancellationToken);
        return blocked && attempts;
    }

    public async Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (!await cache.IsAvailableAsync(cancellationToken))
        {
            return SecurityStats.Unavailable;
        }

        var blacklisted = new List<BlacklistedIp>();
        foreach (var key in await cache.KeysByPatternAsync(SecurityKeys.BlacklistPrefix + "*", cancellationToken))
        {
            if (ParseObject(await cache.GetJsonAsync<string>(key, cancellationToken)) is { } data)
            {
                blacklisted.Add(ToBlacklisted(key[SecurityKeys.BlacklistPrefix.Length..], data));
            }
        }

        var blocked = new List<BlockedIp>();
        foreach (var key in await cache.KeysByPatternAsync(SecurityKeys.BlockedPrefix + "*", cancellationToken))
        {
            if (ToBlocked(key[SecurityKeys.BlockedPrefix.Length..], ParseObject(await cache.GetJsonAsync<string>(key, cancellationToken)), now) is { } entry)
            {
                blocked.Add(entry);
            }
        }

        var suspicious = new List<SuspiciousIp>();
        foreach (var key in await cache.KeysByPatternAsync(SecurityKeys.AttemptsPrefix + "*", cancellationToken))
        {
            if (ToSuspicious(key[SecurityKeys.AttemptsPrefix.Length..], await cache.GetJsonAsync<string>(key, cancellationToken)) is { } entry)
            {
                suspicious.Add(entry);
            }
        }

        return new SecurityStats(
            true,
            [.. blacklisted.OrderBy(e => e.Ip, StringComparer.Ordinal)],
            [.. blocked.OrderBy(e => e.Ip, StringComparer.Ordinal)],
            [.. suspicious.OrderBy(e => e.Ip, StringComparer.Ordinal)]);
    }

    /// <summary>
    /// <c>block_ip</c>: <c>count</c> anterior + 1, <c>first_blocked</c> regravado com agora (o Flask sobrescreve a cada
    /// bloqueio), TTL de 3600 s.
    /// </summary>
    private async Task<(int Count, DateTime Until, bool Persisted)> BlockAsync(string ip, DateTime now, CancellationToken cancellationToken)
    {
        var key = SecurityKeys.Blocked(ip);
        var until = now.AddMinutes(AttemptWindow.BlockMinutes);

        var count = 1;
        if (ParseObject(await cache.GetJsonAsync<string>(key, cancellationToken)) is { } existing)
        {
            count = existing.TryGetProperty("count", out var previous) && previous.TryGetInt32(out var number) ? number + 1 : 1;
        }

        var data = new Dictionary<string, object?>
        {
            ["blocked_until"] = SecurityTimestamp.Format(until),
            ["count"] = count,
            ["first_blocked"] = SecurityTimestamp.Format(now),
        };
        var persisted = await cache.SetJsonAsync(key, JsonSerializer.Serialize(data), SecurityKeys.BlockedTtl, cancellationToken);
        return (count, until, persisted);
    }

    private static JsonElement? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Qualquer problema (JSON inválido, não lista, elemento sem <c>timestamp</c> válido) zera a lista, como o Flask.</summary>
    private static List<Attempt> ParseAttempts(string? json)
    {
        var result = new List<Attempt>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("timestamp", out var timestamp)
                    || timestamp.ValueKind != JsonValueKind.String
                    || !SecurityTimestamp.TryParse(timestamp.GetString(), out var parsed))
                {
                    return [];
                }

                var reason = item.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                result.Add(new Attempt(parsed, reason));
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeAttempts(IEnumerable<Attempt> attempts) =>
        JsonSerializer.Serialize(attempts.Select(a => new Dictionary<string, object?>
        {
            ["timestamp"] = SecurityTimestamp.Format(a.Timestamp),
            ["reason"] = a.Reason,
        }));

    private static BlacklistedIp ToBlacklisted(string ip, JsonElement data)
    {
        string? Text(string name) => data.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : null
            : "Unknown";

        var permanent = !data.TryGetProperty("permanent", out var flag) || flag.ValueKind != JsonValueKind.False;
        return new BlacklistedIp(ip, Text("reason"), Text("blacklisted_at"), permanent);
    }

    private static BlockedIp? ToBlocked(string ip, JsonElement? data, DateTime now)
    {
        if (data is not { } block
            || !block.TryGetProperty("blocked_until", out var untilText)
            || untilText.ValueKind != JsonValueKind.String
            || !SecurityTimestamp.TryParse(untilText.GetString(), out var until)
            || now >= until)
        {
            return null;
        }

        var attempts = block.TryGetProperty("count", out var count) && count.TryGetInt32(out var number) ? number : 1;
        return new BlockedIp(ip, attempts, SecurityTimestamp.Format(until), PyTimedelta.Format(until - now));
    }

    private static SuspiciousIp? ToSuspicious(string ip, string? json)
    {
        // Aqui o TTL é o único filtro: a lista inteira conta, mesmo com tentativas fora da janela de 10 min.
        var attempts = ParseAttempts(json);
        if (attempts.Count == 0)
        {
            return null;
        }

        var reasons = attempts.TakeLast(3).Select(a => a.Reason).ToList();
        return new SuspiciousIp(ip, attempts.Count, SecurityTimestamp.Format(attempts[^1].Timestamp), reasons);
    }
}
