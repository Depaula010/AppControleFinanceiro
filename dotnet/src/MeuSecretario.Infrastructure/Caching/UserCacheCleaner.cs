using Dapper;
using MeuSecretario.Application.ApiKeys;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using MeuSecretario.Infrastructure.Database;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Caching;

/// <summary>
/// Apaga as chaves do usuário no Redis (<c>pending_tx</c>, <c>last_pending</c>, <c>pending_event</c>,
/// <c>nightly_checkin</c>, <c>nightly_checkin_active</c>; integrations.md §7). As chaves usam o número de WhatsApp,
/// então ele é lido de <c>usuarios</c>; sem a linha (já apagada) não há o que limpar e o TTL (até 1 h) expira o resto.
/// </summary>
public sealed class UserCacheCleaner(IDbConnectionFactory connections, ICache cache, ILogger<UserCacheCleaner> logger) : IUserCacheCleaner
{
    public async Task ClearAsync(int userId, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var number = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "SELECT numero_whatsapp FROM usuarios WHERE id = @userId", new { userId }, cancellationToken: cancellationToken));
        if (string.IsNullOrEmpty(number))
        {
            CacheLog.UserCacheSkipped(logger, userId);
            return;
        }

        var keys = new List<string> { CacheKeys.LastPending(number), CacheKeys.LastPendingEvent(number), CacheKeys.NightlyCheckinActive(number) };
        keys.AddRange(await cache.KeysByPatternAsync(CacheKeys.PendingTx(number, "*"), cancellationToken));
        keys.AddRange(await cache.KeysByPatternAsync(CacheKeys.PendingEventPattern(number), cancellationToken));
        keys.AddRange(await cache.KeysByPatternAsync(CacheKeys.NightlyCheckin(number, "*"), cancellationToken));

        foreach (var key in keys)
        {
            await cache.DeleteAsync(key, cancellationToken);
        }

        CacheLog.UserCacheCleared(logger, userId, keys.Count);
    }
}
