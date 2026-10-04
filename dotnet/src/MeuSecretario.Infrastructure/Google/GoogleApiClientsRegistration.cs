using MeuSecretario.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Google;

public static class GoogleApiClientsRegistration
{
    /// <summary>
    /// <c>ICalendarClient</c> e <c>IDriveClient</c> (010d-2). Ambos usam a <c>Google.Apis.Http.IHttpClientFactory</c> da 010-0
    /// (timeout de 60 s, substituível em teste) e o <c>IGoogleAccountService</c> da 010d-1 para o <c>access_token</c>.
    /// </summary>
    public static IServiceCollection AddGoogleApiClients(this IServiceCollection services)
    {
        services.AddSingleton<ICalendarClient, CalendarClient>();
        services.AddSingleton<IDriveClient, DriveClient>();
        return services;
    }
}
