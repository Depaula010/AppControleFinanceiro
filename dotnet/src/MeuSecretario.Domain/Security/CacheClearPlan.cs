namespace MeuSecretario.Domain.Security;

/// <summary>
/// Ramo de <c>POST /admin/gemini-cache-clear</c> (RN-009-16, <c>cache_management.py</c>), nesta ordem:
/// usuário (com tipo opcional), só o padrão, ou tudo.
/// </summary>
public abstract record CacheClearPlan
{
    private CacheClearPlan()
    {
    }

    /// <summary>Apaga <c>gemini_cache:{pattern ou *}:{userId}:*</c>. Com usuário o padrão é o tipo puro (<c>category</c>).</summary>
    public sealed record ForUser(int UserId, string? Pattern) : CacheClearPlan;

    /// <summary>Apaga <c>gemini_cache:{pattern}:*</c>; sem usuário aceita <c>intent</c> e <c>intent:*</c>.</summary>
    public sealed record ForPattern(string Value) : CacheClearPlan;

    /// <summary>Apaga <c>gemini_cache:*:*</c>.</summary>
    public sealed record Everything : CacheClearPlan;

    /// <param name="userId">Já validado: inteiro positivo, ou <c>null</c>/<c>0</c> para "sem usuário".</param>
    /// <param name="pattern">Texto; vazio ou <c>null</c> vale como ausente.</param>
    public static CacheClearPlan From(int? userId, string? pattern)
    {
        var cleanPattern = string.IsNullOrEmpty(pattern) ? null : pattern;
        if (userId is > 0)
        {
            return new ForUser(userId.Value, cleanPattern);
        }

        return cleanPattern is null ? new Everything() : new ForPattern(cleanPattern);
    }
}
