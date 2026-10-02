namespace MeuSecretario.Application.Abstractions;

/// <summary>Limite diário de cálculos de rota (spec 010 RN-010-63). Erro de cache vira <c>(true, 10, 0)</c>.</summary>
public interface ITravelRateLimiter
{
    Task<TravelRateStatus> CheckAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Soma 1 e grava <c>"N"</c> com TTL de 86400 s; devolve se gravou.</summary>
    Task<bool> IncrementAsync(int userId, CancellationToken cancellationToken);
}

public sealed record TravelRateStatus(bool Allowed, int Remaining, int Used);
