using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Google;

public static class GoogleApiClientsRegistration
{
    /// <summary>Registros de <c>ICalendarClient</c> e <c>IDriveClient</c> (010d-2). Vazio na 010-0.</summary>
    public static IServiceCollection AddGoogleApiClients(this IServiceCollection services) => services;
}
