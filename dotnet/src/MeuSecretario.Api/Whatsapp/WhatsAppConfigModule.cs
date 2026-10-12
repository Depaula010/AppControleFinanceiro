using MeuSecretario.Api.Modules;
using MeuSecretario.Application.Whatsapp;
using MeuSecretario.Infrastructure.Whatsapp;

namespace MeuSecretario.Api.Whatsapp;

/// <summary>Feature 012f (configurações): intenções de notificações, localização, relatório mensal, endereços e API key. Sem rotas.</summary>
public sealed class WhatsAppConfigModule : IFeatureModule
{
    public void AddServices(IServiceCollection services)
    {
        services.AddScoped<IWhatsAppConfigStore, WhatsAppConfigStore>();

        services.AddScoped<IWhatsAppIntent, NotificationsIntent>();
        services.AddScoped<IWhatsAppIntent, LocationIntent>();
        services.AddScoped<IWhatsAppIntent, MonthlyReportIntent>();
        services.AddScoped<IWhatsAppIntent, AddressConfigIntent>();
        services.AddScoped<IWhatsAppIntent, AddressListIntent>();
        services.AddScoped<IWhatsAppIntent, AddressDeleteIntent>();
        services.AddScoped<IWhatsAppIntent, ApiKeyIntent>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Sem rotas: a entrada é o POST /webhook-whatsapp (WhatsAppModule).
    }
}
