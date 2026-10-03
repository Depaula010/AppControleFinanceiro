using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Llm;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Invalidação e métricas do cache do Gemini (RN-010-29). Como o Python: varre as chaves pelo padrão e apaga uma a uma,
/// devolvendo quantas apagou; Redis fora ou erro ⇒ 0.
/// </summary>
public sealed class LlmCacheAdmin(ICache cache, LlmCacheMetricsCollector metrics, ILogger<LlmCacheAdmin> logger) : ILlmCacheAdmin
{
    public async Task<int> InvalidateUserAsync(int userId, string? pattern, CancellationToken cancellationToken)
    {
        var user = userId.ToString(CultureInfo.InvariantCulture);
        var full = string.IsNullOrEmpty(pattern)
            ? $"{LlmCacheKey.Root}:*:{user}:*"
            : $"{LlmCacheKey.Root}:{pattern}:{user}:*";
        var deleted = await DeleteMatchingAsync(full, cancellationToken).ConfigureAwait(false);
        LlmLog.Invalidated(logger, deleted, userId, string.IsNullOrEmpty(pattern) ? "all" : pattern);
        return deleted;
    }

    public async Task<int> InvalidatePatternAsync(string pattern, CancellationToken cancellationToken)
    {
        var deleted = await DeleteMatchingAsync($"{LlmCacheKey.Root}:{pattern}:*", cancellationToken).ConfigureAwait(false);
        LlmLog.Invalidated(logger, deleted, null, pattern);
        return deleted;
    }

    public LlmCacheMetrics GetMetrics() => metrics.Snapshot();

    private async Task<int> DeleteMatchingAsync(string fullPattern, CancellationToken cancellationToken)
    {
        if (!await cache.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var key in await cache.KeysByPatternAsync(fullPattern, cancellationToken).ConfigureAwait(false))
        {
            if (await cache.DeleteAsync(key, cancellationToken).ConfigureAwait(false))
            {
                deleted++;
            }
        }

        return deleted;
    }
}
