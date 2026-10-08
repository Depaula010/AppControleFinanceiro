using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>Feature 012h (Drive): a intenção <c>Upload Drive</c>. Sem rotas.</summary>
public sealed class WhatsAppDriveModule : IFeatureModule
{
    public void AddServices(IServiceCollection services) => services.AddScoped<IWhatsAppIntent, DriveUploadIntent>();

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sem rotas: a entrada é o POST /webhook-whatsapp (WhatsAppModule).
    }
}
