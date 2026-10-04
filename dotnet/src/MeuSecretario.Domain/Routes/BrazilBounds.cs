namespace MeuSecretario.Domain.Routes;

/// <summary>Caixa do Brasil usada para descartar geocodificações fora do país (<c>travel_time_service.py:67</c>).</summary>
public static class BrazilBounds
{
    public static bool Contains(double lat, double lon) => lat is >= -33 and <= 5 && lon is >= -74 and <= -34;
}
