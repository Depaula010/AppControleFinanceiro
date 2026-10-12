using System.Globalization;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Llm;

namespace MeuSecretario.Infrastructure.Llm;

/// <summary>
/// Contadores de hit/miss/save/error do cache do Gemini, em memória por instância (zeram no restart, como no Python) e
/// seguros para uso concorrente (RN-010-28).
/// </summary>
public sealed class LlmCacheMetricsCollector
{
    private readonly Dictionary<string, TypeCounters> _types =
        LlmCacheKinds.MetricPrefixes.ToDictionary(p => p, _ => new TypeCounters(), StringComparer.Ordinal);

    private long _hits;
    private long _misses;
    private long _saves;
    private long _errors;

    public void RecordHit(string prefix)
    {
        Interlocked.Increment(ref _hits);
        Interlocked.Increment(ref _types[LlmCacheKinds.MetricKey(prefix)].Hits);
    }

    public void RecordMiss(string prefix)
    {
        Interlocked.Increment(ref _misses);
        Interlocked.Increment(ref _types[LlmCacheKinds.MetricKey(prefix)].Misses);
    }

    public void RecordSave() => Interlocked.Increment(ref _saves);

    public void RecordError() => Interlocked.Increment(ref _errors);

    public LlmCacheMetrics Snapshot()
    {
        var hits = Interlocked.Read(ref _hits);
        var misses = Interlocked.Read(ref _misses);
        var rate = Rate(hits, hits + misses);
        var breakdown = LlmCacheKinds.MetricPrefixes.ToDictionary(
            prefix => prefix,
            prefix =>
            {
                var counters = _types[prefix];
                var typeHits = Interlocked.Read(ref counters.Hits);
                var typeMisses = Interlocked.Read(ref counters.Misses);
                return new LlmCacheTypeMetrics(typeHits, typeMisses, typeHits + typeMisses, Rate(typeHits, typeHits + typeMisses));
            },
            StringComparer.Ordinal);

        return new LlmCacheMetrics(
            hits + misses,
            hits,
            misses,
            Interlocked.Read(ref _saves),
            Interlocked.Read(ref _errors),
            rate,
            breakdown,
            new LlmCacheSavings(hits, rate));
    }

    /// <summary><c>f"{x:.1f}%"</c> do Python, com ponto decimal.</summary>
    private static string Rate(long hits, long total) =>
        (total > 0 ? (double)hits / total * 100 : 0).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private sealed class TypeCounters
    {
        public long Hits;
        public long Misses;
    }
}
