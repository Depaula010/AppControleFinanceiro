namespace MeuSecretario.Application.Security;

public enum TrackOutcome
{
    /// <summary>Tentativa gravada, ainda abaixo do limite.</summary>
    Tracked,

    /// <summary>Essa foi a 5ª tentativa na janela: o IP foi bloqueado.</summary>
    Blocked,

    /// <summary>Sem Redis: nada foi gravado e ninguém é bloqueado.</summary>
    RedisUnavailable,
}

/// <summary>Resultado de <see cref="ISecurityStore.TrackAttemptAsync"/>.</summary>
/// <param name="Outcome">O que aconteceu.</param>
/// <param name="Attempts">Tentativas na janela, já com a atual.</param>
/// <param name="BlockCount">Para <see cref="TrackOutcome.Blocked"/>: <c>count</c> acumulado do bloqueio.</param>
/// <param name="BlockedUntil">Para <see cref="TrackOutcome.Blocked"/>: fim do bloqueio.</param>
/// <param name="BlockPersisted"><c>false</c> se o Redis não confirmou a gravação do bloqueio.</param>
public sealed record TrackResult(
    TrackOutcome Outcome,
    int Attempts = 0,
    int BlockCount = 0,
    DateTime? BlockedUntil = null,
    bool BlockPersisted = true)
{
    public static TrackResult Unavailable { get; } = new(TrackOutcome.RedisUnavailable);

    public static TrackResult Tracked(int attempts) => new(TrackOutcome.Tracked, attempts);

    public static TrackResult Blocked(int attempts, int blockCount, DateTime blockedUntil, bool persisted) =>
        new(TrackOutcome.Blocked, attempts, blockCount, blockedUntil, persisted);
}

public sealed record BlacklistedIp(string Ip, string? Reason, string? BlacklistedAt, bool Permanent);

public sealed record BlockedIp(string Ip, int Attempts, string BlockedUntil, string TimeRemaining);

public sealed record SuspiciousIp(string Ip, int RecentAttempts, string LastAttempt, IReadOnlyList<string?> Reasons);

/// <summary>Estatísticas de <c>GET /admin/security-stats</c>; com <see cref="RedisAvailable"/> falso as listas vêm vazias.</summary>
public sealed record SecurityStats(
    bool RedisAvailable,
    IReadOnlyList<BlacklistedIp> Blacklisted,
    IReadOnlyList<BlockedIp> Blocked,
    IReadOnlyList<SuspiciousIp> Suspicious)
{
    public static SecurityStats Unavailable { get; } = new(false, [], [], []);
}

/// <summary>
/// Estado de segurança no Redis (<c>security:{blocked,attempts,blacklist}:{ip}</c>) atrás de uma porta única: o filtro e as
/// rotas admin usam a mesma regra. Fail-open: nenhuma operação lança; sem Redis devolve o valor neutro.
/// </summary>
public interface ISecurityStore
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    Task<bool> IsBlacklistedAsync(string ip, CancellationToken cancellationToken);

    /// <summary>Apaga a chave se o valor estiver corrompido; chave expirada (agora &gt;= <c>blocked_until</c>) não bloqueia.</summary>
    Task<bool> IsBlockedAsync(string ip, DateTime now, CancellationToken cancellationToken);

    Task<TrackResult> TrackAttemptAsync(string ip, string reason, DateTime now, CancellationToken cancellationToken);

    /// <summary><c>false</c> = Redis indisponível (a gravação não foi confirmada).</summary>
    Task<bool> BlacklistAsync(string ip, string? reason, DateTime now, CancellationToken cancellationToken);

    /// <summary><c>false</c> = Redis indisponível; com Redis é <c>true</c> mesmo que a chave não existisse.</summary>
    Task<bool> RemoveFromBlacklistAsync(string ip, CancellationToken cancellationToken);

    /// <summary>Apaga <c>blocked</c> e <c>attempts</c> do IP.</summary>
    Task<bool> RemoveTempBlockAsync(string ip, CancellationToken cancellationToken);

    Task<SecurityStats> GetStatsAsync(DateTime now, CancellationToken cancellationToken);
}
