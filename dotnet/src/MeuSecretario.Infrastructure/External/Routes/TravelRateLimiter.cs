using System.Globalization;
using System.Text.Json;
using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Caching;
using Microsoft.Extensions.Logging;

namespace MeuSecretario.Infrastructure.External.Routes;

/// <summary>
/// Limite diário de rotas (RN-010-63, desvio 055). Portado e SEM USO, como no Flask. Chave com a data de São Paulo;
/// o contador é lido como <c>3</c> ou <c>"3"</c>; qualquer erro vira <c>(true, 10, 0)</c> (o 10 é fixo). O incremento
/// é atômico (<see cref="ITravelCounterStore"/>) e grava <c>"N"</c> com TTL de 86400 s.
/// </summary>
public sealed class TravelRateLimiter(
    ICache cache,
    ITravelCounterStore counter,
    IClock clock,
    IIntegrationSettings settings,
    ILogger<TravelRateLimiter> logger) : ITravelRateLimiter
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(86400);

    public async Task<TravelRateStatus> CheckAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var element = await cache.GetJsonElementAsync(CacheKeys.TravelTimeLimit(userId, clock.Today), cancellationToken).ConfigureAwait(false);
            var used = element is { } value ? Parse(value) : 0;
            var remaining = settings.TravelTimeDailyLimit - used;
            return new TravelRateStatus(remaining > 0, remaining, used);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExternalLog.RateLimitFailed(logger, "check", ex.GetType().Name);
            return new TravelRateStatus(true, 10, 0);
        }
    }

    public async Task<bool> IncrementAsync(int userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await counter.IncrementAsync(CacheKeys.TravelTimeLimit(userId, clock.Today), Ttl, cancellationToken).ConfigureAwait(false);
            return result is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ExternalLog.RateLimitFailed(logger, "increment", ex.GetType().Name);
            return false;
        }
    }

    /// <summary><c>int(valor)</c>: número JSON (trunca) ou texto numérico; valor vazio vale 0.</summary>
    private static int Parse(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => (int)Math.Truncate(value.GetDouble()),
        JsonValueKind.String when string.IsNullOrEmpty(value.GetString()) => 0,
        JsonValueKind.String => int.Parse(value.GetString()!, NumberStyles.Integer, CultureInfo.InvariantCulture),
        _ => throw new FormatException("Contador inválido."),
    };
}
