using MeuSecretario.Application.Abstractions;
using MeuSecretario.Domain.Security;

namespace MeuSecretario.Application.Admin;

public abstract record ClearLlmCacheResult
{
    private ClearLlmCacheResult()
    {
    }

    /// <summary><c>usuario_id</c> truthy e não inteiro positivo (desvio 079).</summary>
    public sealed record InvalidUserId : ClearLlmCacheResult;

    /// <summary><c>pattern</c> presente, não nulo e que não é texto.</summary>
    public sealed record InvalidPattern : ClearLlmCacheResult;

    public sealed record Cleared(CacheClearPlan Plan, int Deleted) : ClearLlmCacheResult;
}

/// <summary><c>POST /admin/gemini-cache-clear</c> (RN-009-16): ramo usuário &gt; padrão &gt; tudo.</summary>
public sealed class ClearLlmCache(ILlmCacheAdmin cache)
{
    public async Task<ClearLlmCacheResult> ExecuteAsync(AdminField userId, AdminField pattern, CancellationToken cancellationToken)
    {
        if (pattern.Kind is not (AdminFieldKind.Absent or AdminFieldKind.Null or AdminFieldKind.Text))
        {
            return new ClearLlmCacheResult.InvalidPattern();
        }

        int? id = null;
        if (userId.Truthy)
        {
            if (userId.Kind != AdminFieldKind.WholeNumber || userId.Whole is < 1 or > int.MaxValue)
            {
                return new ClearLlmCacheResult.InvalidUserId();
            }

            id = (int)userId.Whole;
        }

        var plan = CacheClearPlan.From(id, pattern.Text);
        var deleted = plan switch
        {
            CacheClearPlan.ForUser user => await cache.InvalidateUserAsync(user.UserId, user.Pattern, cancellationToken),
            CacheClearPlan.ForPattern p => await cache.InvalidatePatternAsync(p.Value, cancellationToken),
            _ => await cache.InvalidatePatternAsync("*", cancellationToken),
        };
        return new ClearLlmCacheResult.Cleared(plan, deleted);
    }
}

/// <summary><c>GET /admin/gemini-cache-stats</c>: métricas da instância (desvio 004).</summary>
public sealed class GetLlmCacheStats(ILlmCacheAdmin cache)
{
    public LlmCacheMetrics Execute() => cache.GetMetrics();
}

/// <summary>Sessão do bot em <c>baileys_auth</c>.</summary>
public interface IBotSessionRepository
{
    /// <summary>Apaga as linhas da sessão <c>baileys_session</c>; devolve quantas.</summary>
    Task<int> ClearAsync(CancellationToken cancellationToken);
}

/// <summary><c>POST /admin/clear-bot-session</c> (spec §4.7).</summary>
public sealed class ClearBotSession(IBotSessionRepository repository)
{
    public Task<int> ExecuteAsync(CancellationToken cancellationToken) => repository.ClearAsync(cancellationToken);
}
