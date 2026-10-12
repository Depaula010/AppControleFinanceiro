using MeuSecretario.Api.Contracts;
using MeuSecretario.Api.Http;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Security;
using Microsoft.Extensions.Options;

namespace MeuSecretario.Api.Security;

/// <summary>
/// Filtro de segurança do Flask (<c>security_filter</c>, spec 009 §5): bypass interno, blacklist, bloqueio temporário,
/// requisição suspeita. Roda <b>depois</b> do roteamento (o <c>WebApplication</c> insere o <c>UseRouting</c> no início do
/// pipeline), então "rota registrada" é <c>GetEndpoint() is not null</c>: a whitelist deriva das rotas, sem lista paralela.
/// Fail-open: qualquer falha do store segue para a rota.
/// </summary>
public sealed class SecurityFilterMiddleware(
    RequestDelegate next,
    SecurityFilter filter,
    IClock clock,
    IOptions<SecurityOptions> options,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger(SecurityLog.Category);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!options.Value.BotProtectionEnabled)
        {
            await next(context);
            return;
        }

        var client = ClientIp.Resolve(context);
        if (client.Internal)
        {
            await next(context);
            return;
        }

        var verdict = await EvaluateAsync(context, client.Ip);
        var path = context.Request.Path.Value ?? string.Empty;

        switch (verdict.Kind)
        {
            case SecurityVerdictKind.Blacklisted:
                SecurityLog.Blacklisted(_logger, client.Ip, path);
                await Envelopes.D.Error("Access permanently denied", "Your IP has been permanently blocked", StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;

            case SecurityVerdictKind.TemporarilyBlocked:
                SecurityLog.Blocked(_logger, client.Ip, path);
                await Envelopes.D.Error("Access denied", "Your IP has been temporarily blocked due to suspicious activity", StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;

            case SecurityVerdictKind.AutoBlocked:
                LogAutoBlock(client.Ip, verdict);
                await Envelopes.D.Error("Access denied", "Too many suspicious requests. IP blocked.", StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;

            case SecurityVerdictKind.Suspicious:
                LogAttempt(client.Ip, verdict);
                // 404 em JSON: não revela que a detecção aconteceu.
                await Envelopes.Json(new Dictionary<string, object?> { ["error"] = "Not found" }, StatusCodes.Status404NotFound)
                    .ExecuteAsync(context);
                return;

            default:
                await next(context);
                return;
        }
    }

    private async Task<SecurityVerdict> EvaluateAsync(HttpContext context, string ip)
    {
        try
        {
            return await filter.EvaluateAsync(
                ip,
                context.Request.Path.Value ?? string.Empty,
                context.Request.Headers.UserAgent.ToString(),
                context.GetEndpoint() is not null,
                clock.Now,
                context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SecurityLog.FilterFailed(_logger, SafeError.Describe(exception));
            return SecurityVerdict.Allow;
        }
    }

    private void LogAttempt(string ip, SecurityVerdict verdict)
    {
        var reason = verdict.Reason ?? string.Empty;
        if (verdict.Track is { Outcome: TrackOutcome.Tracked } track)
        {
            SecurityLog.Attempt(_logger, ip, track.Attempts, reason);
        }
        else
        {
            SecurityLog.AttemptWithoutRedis(_logger, ip, reason);
        }
    }

    private void LogAutoBlock(string ip, SecurityVerdict verdict)
    {
        var track = verdict.Track!;
        if (!track.BlockPersisted)
        {
            SecurityLog.BlockNotPersisted(_logger, ip);
        }
        else
        {
            SecurityLog.Block(_logger, ip, track.BlockCount, track.BlockedUntil!.Value);
        }

        SecurityLog.AutoBlock(_logger, ip, track.Attempts);
    }
}
