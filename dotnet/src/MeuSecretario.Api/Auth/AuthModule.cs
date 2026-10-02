using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Abstractions;

namespace MeuSecretario.Api.Auth;

/// <summary>
/// Feature 001. A 001a registra só os blocos compartilhados (usuário atual); as rotas
/// <c>/auth/*</c> entram na 001b.
/// </summary>
public sealed class AuthModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // 001b: POST /auth/register, /auth/login, /auth/verify
    }
}
