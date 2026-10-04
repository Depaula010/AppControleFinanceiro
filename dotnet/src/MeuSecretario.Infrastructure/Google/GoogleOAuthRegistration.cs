using MeuSecretario.Application.Abstractions;
using MeuSecretario.Application.Google;
using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Google;

public static class GoogleOAuthRegistration
{
    /// <summary>
    /// Registros do OAuth, credenciais e repositório de tokens (010d-1). O <c>IBotClient</c> e o <c>ICache</c> vêm
    /// das trilhas 010b e 010a; os casos de uso e as rotas ficam em <c>Api/Google/GoogleModule</c>.
    /// </summary>
    public static IServiceCollection AddGoogleOAuth(this IServiceCollection services)
    {
        services.AddSingleton<IGoogleTokenRepository, GoogleTokenRepository>();
        services.AddSingleton<IGoogleUserLookup, GoogleUserLookup>();
        services.AddSingleton<IGoogleOAuthClient, GoogleOAuthClient>();
        services.AddSingleton<IGoogleDiagnostics, GoogleDiagnostics>();

        services.AddSingleton<OAuthStateCodec>();
        services.AddSingleton<ConnectLinkTokenCodec>();
        services.AddSingleton<GoogleLinks>();
        // Fábrica explícita: o ICache (010a) e o IBotClient (010b) são registrados por outros métodos de AddInfrastructure e a
        // validação de escopo do host de teste não deve exigir que já existam quando só o OAuth é montado.
        services.AddSingleton(sp => new TokenRevokedNotifier(
            sp.GetRequiredService<ICache>(),
            sp.GetRequiredService<IGoogleUserLookup>(),
            sp.GetRequiredService<IBotClient>(),
            sp.GetRequiredService<GoogleLinks>(),
            sp.GetRequiredService<IGoogleDiagnostics>()));
        services.AddSingleton<GoogleAccountService>();
        services.AddSingleton<IGoogleAccountService>(sp => sp.GetRequiredService<GoogleAccountService>());
        return services;
    }
}
