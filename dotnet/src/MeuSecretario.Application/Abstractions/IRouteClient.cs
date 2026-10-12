namespace MeuSecretario.Application.Abstractions;

/// <summary>
/// OpenRouteService (spec 010 RN-010-60/61). <c>null</c> em falha; falha de chave do usuário propaga
/// <see cref="ApiKeyUnavailableException"/>. <paramref name="userId"/> nulo = chave do sistema.
/// </summary>
public interface IRouteClient
{
    Task<GeocodeResult?> GeocodeAsync(string address, int? userId, CancellationToken cancellationToken);

    Task<TravelResult?> CalculateTravelTimeAsync(Coordinates origin, Coordinates destination, int? userId, CancellationToken cancellationToken);
}

public sealed record Coordinates(double Lat, double Lon);

public sealed record GeocodeResult(double Lat, double Lon, string Label);

public sealed record TravelResult(int DurationMinutes, double DistanceKm, string? RouteSummary);

/// <summary>Falha do 008 ao resolver a chave do usuário (mensagem literal do 008).</summary>
public sealed class ApiKeyUnavailableException(string message) : Exception(message);
