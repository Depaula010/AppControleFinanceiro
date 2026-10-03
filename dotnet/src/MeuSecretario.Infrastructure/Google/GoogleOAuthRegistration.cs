using Microsoft.Extensions.DependencyInjection;

namespace MeuSecretario.Infrastructure.Google;

public static class GoogleOAuthRegistration
{
    /// <summary>Registros do OAuth, credenciais e repositório de tokens (010d-1). Vazio na 010-0.</summary>
    public static IServiceCollection AddGoogleOAuth(this IServiceCollection services) => services;
}
