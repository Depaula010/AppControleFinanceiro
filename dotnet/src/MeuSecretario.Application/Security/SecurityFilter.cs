using MeuSecretario.Domain.Security;

namespace MeuSecretario.Application.Security;

public enum SecurityVerdictKind
{
    /// <summary>Segue para o roteamento.</summary>
    Allow,

    /// <summary>403 permanente (blacklist).</summary>
    Blacklisted,

    /// <summary>403 temporário (bloqueio de 60 min).</summary>
    TemporarilyBlocked,

    /// <summary>Suspeita registrada: 404 JSON.</summary>
    Suspicious,

    /// <summary>Suspeita que completou 5 tentativas: o IP foi bloqueado agora (403).</summary>
    AutoBlocked,
}

public sealed record SecurityVerdict(SecurityVerdictKind Kind, string? Reason = null, TrackResult? Track = null)
{
    public static SecurityVerdict Allow { get; } = new(SecurityVerdictKind.Allow);
}

/// <summary>
/// Regra do filtro (RN-009-02), sem HTTP: blacklist, depois bloqueio temporário, depois suspeita (tentativa; a 5ª bloqueia).
/// </summary>
public sealed class SecurityFilter(ISecurityStore store)
{
    public async Task<SecurityVerdict> EvaluateAsync(
        string ip,
        string path,
        string? userAgent,
        bool isRegisteredRoute,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (await store.IsBlacklistedAsync(ip, cancellationToken))
        {
            return new SecurityVerdict(SecurityVerdictKind.Blacklisted);
        }

        if (await store.IsBlockedAsync(ip, now, cancellationToken))
        {
            return new SecurityVerdict(SecurityVerdictKind.TemporarilyBlocked);
        }

        var suspicion = SuspiciousRules.Evaluate(path, userAgent, isRegisteredRoute);
        if (suspicion is null)
        {
            return SecurityVerdict.Allow;
        }

        var track = await store.TrackAttemptAsync(ip, suspicion.Reason, now, cancellationToken);
        return new SecurityVerdict(
            track.Outcome == TrackOutcome.Blocked ? SecurityVerdictKind.AutoBlocked : SecurityVerdictKind.Suspicious,
            suspicion.Reason,
            track);
    }
}
