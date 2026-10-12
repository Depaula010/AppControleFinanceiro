using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using MeuSecretario.Domain.Security;

namespace MeuSecretario.Application.Admin;

/// <summary>Problema na validação do <c>ip</c> das rotas <c>security-*</c> (desvios 078 e 079).</summary>
public enum IpInputProblem
{
    /// <summary>Ausente, nulo, vazio, ou de outro tipo que não texto.</summary>
    Missing,

    /// <summary>Texto que não é um endereço IP (desvio 078).</summary>
    Invalid,
}

public abstract record UnblockIpResult
{
    private UnblockIpResult()
    {
    }

    public sealed record BadInput(IpInputProblem Problem) : UnblockIpResult;

    /// <param name="Ip">IP normalizado (<c>trim</c> e forma canônica).</param>
    /// <param name="TempBlockRemoved"><c>false</c> sem Redis.</param>
    /// <param name="BlacklistRemoved"><c>false</c> sem Redis.</param>
    public sealed record Done(string Ip, bool TempBlockRemoved, bool BlacklistRemoved) : UnblockIpResult;
}

/// <summary>
/// <c>POST /admin/security-unblock-ip</c> (RN-009-15): apaga bloqueio temporário, tentativas <b>e</b> blacklist. O resultado
/// das remoções é ignorado: sem Redis ou sem chaves a resposta continua sendo sucesso.
/// </summary>
public sealed class UnblockIp(ISecurityStore store)
{
    public async Task<UnblockIpResult> ExecuteAsync(AdminField ip, CancellationToken cancellationToken)
    {
        if (ip.NonEmptyText(trim: true) is not { } text)
        {
            return new UnblockIpResult.BadInput(IpInputProblem.Missing);
        }

        if (!IpClassifier.TryParseInput(text, out var normalized))
        {
            return new UnblockIpResult.BadInput(IpInputProblem.Invalid);
        }

        var temp = await store.RemoveTempBlockAsync(normalized, cancellationToken);
        var blacklist = await store.RemoveFromBlacklistAsync(normalized, cancellationToken);
        return new UnblockIpResult.Done(normalized, temp, blacklist);
    }
}

/// <summary><c>GET /admin/security-stats</c>: delega ao store (a mesma regra do filtro) com o relógio de São Paulo.</summary>
public sealed class GetSecurityStats(ISecurityStore store, IClock clock)
{
    public Task<SecurityStats> ExecuteAsync(CancellationToken cancellationToken) =>
        store.GetStatsAsync(clock.Now, cancellationToken);
}

public abstract record BlacklistIpResult
{
    private BlacklistIpResult()
    {
    }

    public sealed record BadIp(IpInputProblem Problem) : BlacklistIpResult;

    /// <summary><c>reason</c> presente, não nulo e que não é texto (desvio 079).</summary>
    public sealed record ReasonNotText : BlacklistIpResult;

    public sealed record RedisUnavailable : BlacklistIpResult;

    public sealed record Added(string Ip, string? Reason) : BlacklistIpResult;
}

/// <summary><c>POST /admin/security-blacklist-add</c> (spec §4.3).</summary>
public sealed class BlacklistIp(ISecurityStore store, IClock clock)
{
    public const string DefaultReason = "Manual block via API";

    public async Task<BlacklistIpResult> ExecuteAsync(AdminField ip, AdminField reason, CancellationToken cancellationToken)
    {
        if (!ip.Truthy || ip.Kind != AdminFieldKind.Text)
        {
            return new BlacklistIpResult.BadIp(IpInputProblem.Missing);
        }

        if (!IpClassifier.TryParseInput(ip.Text, out var normalized))
        {
            return new BlacklistIpResult.BadIp(IpInputProblem.Invalid);
        }

        string? finalReason;
        switch (reason.Kind)
        {
            case AdminFieldKind.Absent:
                finalReason = DefaultReason;
                break;
            case AdminFieldKind.Null:
                finalReason = null; // gravado e devolvido como null, como o Flask
                break;
            case AdminFieldKind.Text:
                finalReason = reason.Text;
                break;
            default:
                return new BlacklistIpResult.ReasonNotText();
        }

        return await store.BlacklistAsync(normalized, finalReason, clock.Now, cancellationToken)
            ? new BlacklistIpResult.Added(normalized, finalReason)
            : new BlacklistIpResult.RedisUnavailable();
    }
}

public abstract record RemoveBlacklistedIpResult
{
    private RemoveBlacklistedIpResult()
    {
    }

    public sealed record BadIp(IpInputProblem Problem) : RemoveBlacklistedIpResult;

    public sealed record RedisUnavailable : RemoveBlacklistedIpResult;

    public sealed record Removed(string Ip) : RemoveBlacklistedIpResult;
}

/// <summary><c>POST /admin/security-blacklist-remove</c> (spec §4.4). Com Redis é sucesso mesmo que o IP não estivesse.</summary>
public sealed class RemoveBlacklistedIp(ISecurityStore store)
{
    public async Task<RemoveBlacklistedIpResult> ExecuteAsync(AdminField ip, CancellationToken cancellationToken)
    {
        if (!ip.Truthy || ip.Kind != AdminFieldKind.Text)
        {
            return new RemoveBlacklistedIpResult.BadIp(IpInputProblem.Missing);
        }

        if (!IpClassifier.TryParseInput(ip.Text, out var normalized))
        {
            return new RemoveBlacklistedIpResult.BadIp(IpInputProblem.Invalid);
        }

        return await store.RemoveFromBlacklistAsync(normalized, cancellationToken)
            ? new RemoveBlacklistedIpResult.Removed(normalized)
            : new RemoveBlacklistedIpResult.RedisUnavailable();
    }
}
