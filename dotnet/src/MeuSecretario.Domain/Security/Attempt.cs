namespace MeuSecretario.Domain.Security;

/// <summary>Uma tentativa suspeita registrada em <c>security:attempts:{ip}</c>.</summary>
public sealed record Attempt(DateTime Timestamp, string? Reason);

/// <summary>Janela de 10 minutos e limite de 5 tentativas (RN-009-05). Constantes fixas, como no Flask.</summary>
public static class AttemptWindow
{
    public const int BlockThreshold = 5;
    public const int WindowMinutes = 10;
    public const int BlockMinutes = 60;

    /// <summary>Descarta as tentativas com 10 minutos ou mais (o Flask mantém só <c>timestamp &gt; agora - 10 min</c>).</summary>
    public static IReadOnlyList<Attempt> Prune(IReadOnlyList<Attempt> attempts, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(attempts);
        var cutoff = now - TimeSpan.FromMinutes(WindowMinutes);
        return [.. attempts.Where(a => a.Timestamp > cutoff)];
    }
}
