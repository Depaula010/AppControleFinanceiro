namespace MeuSecretario.Application.Abstractions;

/// <summary>Administração do cache do LLM (usada pela 009 em <c>/admin/gemini-cache-*</c>).</summary>
public interface ILlmCacheAdmin
{
    /// <summary>Apaga <c>gemini_cache:{pattern ou *}:{userId}:*</c>; devolve quantas chaves apagou.</summary>
    Task<int> InvalidateUserAsync(int userId, string? pattern, CancellationToken cancellationToken);

    /// <summary>Apaga <c>gemini_cache:{pattern}:*</c>; devolve quantas chaves apagou.</summary>
    Task<int> InvalidatePatternAsync(string pattern, CancellationToken cancellationToken);

    LlmCacheMetrics GetMetrics();
}

/// <summary>Mesma forma do dicionário de <c>get_metrics</c> do Python (<c>hit_rate</c> como texto <c>"12.3%"</c>).</summary>
public sealed record LlmCacheMetrics(
    long TotalRequests,
    long CacheHits,
    long CacheMisses,
    long CacheSaves,
    long CacheErrors,
    string HitRate,
    IReadOnlyDictionary<string, LlmCacheTypeMetrics> BreakdownByType,
    LlmCacheSavings EstimatedSavings);

public sealed record LlmCacheTypeMetrics(long Hits, long Misses, long Total, string HitRate);

public sealed record LlmCacheSavings(long CallsSaved, string QuotaSavedPct);
