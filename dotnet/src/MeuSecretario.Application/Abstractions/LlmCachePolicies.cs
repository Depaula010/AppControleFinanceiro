using MeuSecretario.Domain.Llm;

namespace MeuSecretario.Application.Abstractions;

/// <summary>As 5 políticas de cache que o Flask usa (RN-010-27); as demais chamadas não usam cache (<c>Cache = null</c>).</summary>
public static class LlmCachePolicies
{
    public static LlmCachePolicy Intent { get; } = For(LlmCacheKinds.Intent, userSpecific: false);

    /// <summary>Exige <c>UserId</c> na <see cref="LlmRequest"/>.</summary>
    public static LlmCachePolicy Category { get; } = For(LlmCacheKinds.Category, userSpecific: true);

    public static LlmCachePolicy ExtractTransaction { get; } = For(LlmCacheKinds.ExtractTransaction, userSpecific: false);

    public static LlmCachePolicy PeriodQuery { get; } = For(LlmCacheKinds.PeriodQuery, userSpecific: false);

    public static LlmCachePolicy ChartType { get; } = For(LlmCacheKinds.ChartType, userSpecific: false);

    private static LlmCachePolicy For(string prefix, bool userSpecific) =>
        new(prefix, LlmCacheKinds.TtlFor(prefix), userSpecific);
}
